import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { del, post, put } from '../api/client';
import { useDashboards, useInvalidate, useItemTypes, useLocations, useWidgetData, useWidgetTypes } from '../api/hooks';
import type { DashboardDef, DashboardWidget, WidgetResult } from '../api/types';
import { useAuth } from '../auth';
import { useT } from '../i18n';
import { Badge, Bars, ErrorBox, Modal, TrendChart, fmt, toneForSeverity } from '../components/ui';

type Row = { label: string; count: number };
const nid = () => Math.random().toString(36).slice(2, 10);

function Widget({ w, r, editing, onRemove, onEdit, onMove, onResize }: { w: DashboardWidget; r?: WidgetResult; editing: boolean; onRemove: () => void; onEdit: () => void; onMove: (d: -1 | 1) => void; onResize: () => void }) {
  const t = useT();
  const d = r?.data as never;
  const body = () => {
    if (r?.error) return <div className="error">{r.error}</div>;
    if (!r) return <span className="muted small">Loading…</span>;
    switch (w.type) {
      case 'stat': { const s = d as { value: number; link?: string | null; tone?: string | null }; const cls = s.tone && s.value > 0 ? s.tone : ''; return <div className={'stat ' + cls}><span className="value">{s.link ? <Link to={s.link} style={{ color: 'inherit' }}>{s.value}</Link> : s.value}</span></div>; }
      case 'breakdown': case 'presence': case 'fences': return <Bars rows={(d as Row[]) ?? []} />;
      case 'trend': { const t = d as { days: number; rows: { day: string; count: number }[] }; const buckets: { start: string; count: number }[] = []; for (let i = t.days - 1; i >= 0; i--) { const dt = new Date(); dt.setUTCHours(0, 0, 0, 0); dt.setUTCDate(dt.getUTCDate() - i); const key = dt.toISOString().slice(0, 10); buckets.push({ start: key, count: t.rows.find((x) => x.day.slice(0, 10) === key)?.count ?? 0 }); } return <TrendChart buckets={buckets} height={w.h > 1 ? 220 : 110} label={String(w.config.metric ?? '')} />; }
      case 'devices': { const v = d as { total: number; byHealth: { health: string; count: number }[]; attention: { id: string; name: string; health: string; lastHeartbeatAt?: string | null; lastSeenAt?: string | null }[] }; return <><div className="row" style={{ marginBottom: 6 }}>{v.byHealth.map((b) => <Badge key={b.health} tone={b.health === 'Online' ? 'ok' : b.health === 'Degraded' ? 'warn' : b.health === 'Offline' ? 'crit' : ''}>{b.health} {b.count}</Badge>)}</div>{v.attention.map((a) => <div key={a.id} className="small" style={{ padding: '3px 0', borderBottom: '1px solid var(--border)' }}><Link to="/devices">{a.name}</Link> <span className="muted">· {a.health} · last {fmt.ago(a.lastHeartbeatAt ?? a.lastSeenAt)}</span></div>)}{v.attention.length === 0 && <span className="muted small">All readers healthy</span>}</>; }
      case 'stocktakes': { const rows = d as { id: string; name: string; status: string; expectedCount: number; foundCount: number; missingCount: number; accuracy?: number | null; startedAt: string }[]; return <table><tbody>{rows.map((s) => <tr key={s.id}><td><Link to={`/stocktakes/${s.id}`}>{s.name}</Link><div className="muted small">{fmt.ago(s.startedAt)} · {s.status}</div></td><td className="right">{s.accuracy != null ? <Badge tone={s.accuracy >= 98 ? 'ok' : s.accuracy >= 90 ? 'warn' : 'crit'}>{s.accuracy}%</Badge> : <span className="muted small">{s.foundCount}/{s.expectedCount}</span>}</td></tr>)}</tbody></table>; }
      case 'list': {
        if (w.config.source === 'events') { const rows = d as { id: string; itemId: string; itemName?: string | null; type: string; occurredAt: string; toState?: string | null }[]; return <ul className="timeline">{rows.map((e) => <li key={e.id}><span className="when">{fmt.ago(e.occurredAt)}</span><span><Badge>{e.type}</Badge> <Link to={`/items/${e.itemId}`}>{e.itemName ?? e.itemId}</Link>{e.toState ? <span className="muted"> → {e.toState}</span> : null}</span></li>)}</ul>; }
        const rows = d as { id: string; severity: string; message: string; raisedAt: string; source?: string | null; escalationLevel: number }[];
        return <>{rows.map((a) => <div key={a.id} className="row" style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><Badge tone={toneForSeverity(a.severity)}>{a.severity}</Badge><span style={{ flex: 1 }}>{a.message}</span>{a.escalationLevel > 0 && <Badge tone="info">esc {a.escalationLevel}</Badge>}<span className="muted small">{fmt.ago(a.raisedAt)}</span></div>)}{rows.length === 0 && <span className="muted">No open alerts</span>}</>;
      }
      case 'text': return <p style={{ whiteSpace: 'pre-wrap', margin: 0 }}>{String((d as { text: string }).text)}</p>;
      default: return <pre className="small">{JSON.stringify(d, null, 1)}</pre>;
    }
  };
  return (
    <div className="panel" style={{ gridColumn: `span ${Math.min(4, w.w)}`, minHeight: w.h > 1 ? 300 : undefined, position: 'relative' }}>
      <div className="topbar" style={{ marginBottom: 6 }}><h3 style={{ margin: 0 }}>{t(w.title || w.type)}</h3>{editing && <span className="row" style={{ gap: 2 }}><button className="sm" title="Move left" onClick={() => onMove(-1)}>◀</button><button className="sm" title="Move right" onClick={() => onMove(1)}>▶</button><button className="sm" title="Resize" onClick={onResize}>{w.w}×{w.h}</button><button className="sm" onClick={onEdit}>Edit</button><button className="sm danger" onClick={onRemove}>✕</button></span>}</div>
      {body()}
    </div>
  );
}

function WidgetEditor({ w, onSave, onClose }: { w: DashboardWidget; onSave: (w: DashboardWidget) => void; onClose: () => void }) {
  const [e, setE] = useState<DashboardWidget>({ ...w, config: { ...w.config } });
  const types = useWidgetTypes(); const itemTypes = useItemTypes(); const locs = useLocations();
  const cfg = (k: string, v: unknown) => setE({ ...e, config: { ...e.config, [k]: v === '' ? undefined : v } });
  const c = e.config;
  return (
    <Modal title={w.title ? `Edit widget · ${w.title}` : 'New widget'} onClose={onClose}>
      <form className="form" onSubmit={(ev) => { ev.preventDefault(); onSave(e); }}>
        <div className="form cols-2">
          <label>Type<select value={e.type} onChange={(ev) => setE({ ...e, type: ev.target.value, config: {} })}>{types.data?.types.map((t) => <option key={t}>{t}</option>)}</select></label>
          <label>Title<input value={e.title} onChange={(ev) => setE({ ...e, title: ev.target.value })} /></label>
          <label>Width (columns)<select value={e.w} onChange={(ev) => setE({ ...e, w: Number(ev.target.value) })}>{[1, 2, 3, 4].map((n) => <option key={n} value={n}>{n}</option>)}</select></label>
          <label>Height<select value={e.h} onChange={(ev) => setE({ ...e, h: Number(ev.target.value) })}><option value={1}>normal</option><option value={2}>tall</option></select></label>
          {e.type === 'stat' && <><label>Measure<select value={String(c.filter ?? 'items')} onChange={(ev) => cfg('filter', ev.target.value)}>{types.data?.statFilters.map((f) => <option key={f}>{f}</option>)}</select></label><label>Highlight when &gt; 0<select value={String(c.tone ?? '')} onChange={(ev) => cfg('tone', ev.target.value)}><option value="">none</option><option value="warn">warning</option><option value="crit">critical</option><option value="ok">good</option></select></label><label>Link<input value={String(c.link ?? '')} onChange={(ev) => cfg('link', ev.target.value)} placeholder="/items?filter=overdue" /></label></>}
          {e.type === 'breakdown' && <><label>Group by<select value={String(c.by ?? 'type')} onChange={(ev) => cfg('by', ev.target.value)}>{types.data?.breakdowns.map((f) => <option key={f}>{f}</option>)}</select></label><label>Rows<input type="number" min={1} max={30} value={Number(c.take ?? 10)} onChange={(ev) => cfg('take', Number(ev.target.value))} /></label></>}
          {(e.type === 'stat' || e.type === 'breakdown') && <><label>Item type (scope)<select value={String(c.itemTypeId ?? '')} onChange={(ev) => cfg('itemTypeId', ev.target.value)}><option value="">all</option>{itemTypes.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label><label>Location subtree (scope)<select value={String(c.locationId ?? '')} onChange={(ev) => cfg('locationId', ev.target.value)}><option value="">all</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name}</option>)}</select></label><label>State (scope)<input value={String(c.state ?? '')} onChange={(ev) => cfg('state', ev.target.value)} placeholder="e.g. InService" /></label></>}
          {e.type === 'trend' && <><label>Metric<select value={String(c.metric ?? 'reads')} onChange={(ev) => cfg('metric', ev.target.value)}>{types.data?.trendMetrics.map((f) => <option key={f}>{f}</option>)}</select></label><label>Days<input type="number" min={1} max={365} value={Number(c.days ?? 14)} onChange={(ev) => cfg('days', Number(ev.target.value))} /></label></>}
          {e.type === 'presence' && <label>Location subtree<select value={String(c.locationId ?? '')} onChange={(ev) => cfg('locationId', ev.target.value)}><option value="">all</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>}
          {e.type === 'list' && <><label>Source<select value={String(c.source ?? 'alerts')} onChange={(ev) => cfg('source', ev.target.value)}>{types.data?.listSources.map((f) => <option key={f}>{f}</option>)}</select></label><label>Rows<input type="number" min={1} max={50} value={Number(c.take ?? 8)} onChange={(ev) => cfg('take', Number(ev.target.value))} /></label></>}
          {e.type === 'stocktakes' && <label>Rows<input type="number" min={1} max={20} value={Number(c.take ?? 6)} onChange={(ev) => cfg('take', Number(ev.target.value))} /></label>}
          {e.type === 'text' && <label style={{ gridColumn: '1 / -1' }}>Text<textarea rows={4} value={String(c.text ?? '')} onChange={(ev) => cfg('text', ev.target.value)} /></label>}
        </div>
        <div className="row end"><button type="button" onClick={onClose}>Cancel</button><button className="primary">Apply</button></div>
      </form>
    </Modal>
  );
}

export default function Dashboard() {
  const { user } = useAuth(); const isAdmin = user?.role === 'Admin'; const t = useT();
  const dashboards = useDashboards(); const inv = useInvalidate();
  const [selected, setSelected] = useState<string>(() => { try { return localStorage.getItem('rfid.dashboard') ?? ''; } catch { return ''; } });
  const current: DashboardDef | undefined = dashboards.data?.find((d) => d.id === selected) ?? dashboards.data?.[0];
  useEffect(() => { if (current) try { localStorage.setItem('rfid.dashboard', current.id); } catch { /* ignore */ } }, [current]);
  const [draft, setDraft] = useState<DashboardDef | null>(null); // editing copy
  const [editW, setEditW] = useState<DashboardWidget | null>(null);
  const [error, setError] = useState<unknown>(null);
  const view = draft ?? current;
  const widgets = useMemo(() => view?.widgets, [view]);
  const data = useWidgetData(widgets);
  const results = data.data ?? [];
  const setWidgets = (ws: DashboardWidget[]) => setDraft({ ...(draft ?? current!), widgets: ws });
  const save = async () => {
    if (!draft) return; setError(null);
    try {
      const body = { name: draft.name, isDefault: draft.isDefault, shared: draft.shared, widgets: draft.widgets };
      if (draft.id === 'new') { const created = await post<DashboardDef>('/api/dashboards', body); setSelected(created.id); } else await put(`/api/dashboards/${draft.id}`, body);
      inv('dashboards'); setDraft(null);
    } catch (e) { setError(e); }
  };
  const copy = () => { if (!current) return; setDraft({ ...current, id: 'new', name: current.name + ' (copy)', shared: false, isDefault: false, editable: true, widgets: current.widgets.map((w) => ({ ...w, id: nid() })) }); };
  const remove = async () => { if (!current || !confirm('Delete this dashboard?')) return; await del(`/api/dashboards/${current.id}`); setSelected(''); inv('dashboards'); };
  if (!view) return <div className="muted">Loading dashboard…</div>;
  return (
    <div>
      <div className="topbar">
        <div className="row"><h1 style={{ margin: 0 }}>{draft ? <input value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} style={{ fontSize: 20, fontWeight: 700 }} /> : view.name}</h1>
          {!draft && (dashboards.data?.length ?? 0) > 1 && <select value={current?.id} onChange={(e) => setSelected(e.target.value)} style={{ maxWidth: 260 }}>{dashboards.data?.map((d) => <option key={d.id} value={d.id}>{d.name}{d.shared ? '' : ' (mine)'}</option>)}</select>}
          {!draft && view.shared && <Badge tone="info">shared</Badge>}{!draft && view.isDefault && <Badge>default</Badge>}</div>
        <div className="row">
          {draft ? <>
            {isAdmin && <label className="row small" style={{ gap: 4 }}><input type="checkbox" style={{ width: 'auto' }} checked={draft.shared} onChange={(e) => setDraft({ ...draft, shared: e.target.checked })} />Shared</label>}
            <label className="row small" style={{ gap: 4 }}><input type="checkbox" style={{ width: 'auto' }} checked={draft.isDefault} onChange={(e) => setDraft({ ...draft, isDefault: e.target.checked })} />Default</label>
            <button onClick={() => setEditW({ id: nid(), type: 'stat', title: '', w: 1, h: 1, config: { filter: 'items' } })}>+ Widget</button>
            <button onClick={() => setDraft(null)}>{t('Cancel')}</button><button className="primary" onClick={save}>{t('Save')}</button>
          </> : <>
            <span className="muted small">{t('Auto-refreshing')}</span>
            {view.editable && user?.role !== 'Viewer' && <button onClick={() => setDraft({ ...view })}>{t('Edit')}</button>}
            {user?.role !== 'Viewer' && <button onClick={copy}>Save as copy</button>}
            {view.editable && !view.shared && <button className="danger" onClick={remove}>{t('Delete')}</button>}
          </>}
        </div>
      </div>
      <ErrorBox error={error ?? data.error} />
      <div className="grid cols-4">
        {view.widgets.map((w, i) => <Widget key={w.id} w={w} r={results.find((r) => r.id === w.id)} editing={!!draft}
          onRemove={() => setWidgets(view.widgets.filter((x) => x.id !== w.id))} onEdit={() => setEditW(w)}
          onMove={(d) => { const ws = [...view.widgets]; const j = i + d; if (j < 0 || j >= ws.length) return; [ws[i], ws[j]] = [ws[j], ws[i]]; setWidgets(ws); }}
          onResize={() => setWidgets(view.widgets.map((x) => x.id === w.id ? { ...x, w: x.w >= 4 ? 1 : x.w + 1 } : x))} />)}
        {view.widgets.length === 0 && <div className="panel empty" style={{ gridColumn: 'span 4' }}>Empty dashboard – add widgets.</div>}
      </div>
      {editW && <WidgetEditor w={editW} onClose={() => setEditW(null)} onSave={(w) => { const exists = view.widgets.some((x) => x.id === w.id); setWidgets(exists ? view.widgets.map((x) => x.id === w.id ? w : x) : [...view.widgets, w]); setEditW(null); }} />}
    </div>
  );
}

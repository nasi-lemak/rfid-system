import { useState } from 'react';
import { post, put } from '../api/client';
import { useAudit, useAuditSummary, useInvalidate, useRetention } from '../api/hooks';
import type { AuditEntry, RetentionRow } from '../api/types';
import { Badge, Bars, ErrorBox, Pager, fmt, useDebounce } from '../components/ui';

export default function Audit() {
  const [tab, setTab] = useState<'log' | 'retention'>('log');
  const [q, setQ] = useState(''); const [action, setAction] = useState(''); const [userId, setUserId] = useState(''); const [page, setPage] = useState(1);
  const dq = useDebounce(q); const audit = useAudit({ q: dq, action, userId, page, pageSize: 50 }); const summary = useAuditSummary();
  const [open, setOpen] = useState<AuditEntry | null>(null);
  const retention = useRetention(); const inv = useInvalidate();
  const [edits, setEdits] = useState<Record<string, { retainDays: number | null; enabled: boolean }>>({}); const [error, setError] = useState<unknown>(null); const [busy, setBusy] = useState(false); const [runResult, setRunResult] = useState<Record<string, number> | null>(null);
  const eff = (r: RetentionRow) => edits[r.dataset] ?? { retainDays: r.retainDays ?? null, enabled: r.enabled };
  const save = async () => { setBusy(true); setError(null); try { await put('/api/retention', Object.entries(edits).map(([dataset, e]) => ({ dataset, ...e }))); setEdits({}); inv('retention'); } catch (e) { setError(e); } finally { setBusy(false); } };
  const run = async () => { if (!confirm('Delete all expired rows now? This cannot be undone.')) return; setBusy(true); try { setRunResult(await post<Record<string, number>>('/api/retention/run')); inv('retention'); } finally { setBusy(false); } };
  const statusTone = (c: number): 'ok' | 'warn' | 'crit' => c < 300 ? 'ok' : c < 500 ? 'warn' : 'crit';
  return (
    <div>
      <div className="topbar"><h1>Audit & retention</h1><span className="muted small">Every change made through the API, and how long high-volume data is kept</span></div>
      <div className="tabs">{(['log', 'retention'] as const).map((t) => <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t === 'log' ? 'Audit log' : 'Retention policies'}</button>)}</div>
      {tab === 'log' && <>
        <div className="grid cols-3" style={{ marginBottom: 16 }}>
          <div className="panel stat"><span className="label">Changes · last 7 days</span><span className="value">{summary.data?.total ?? '–'}</span></div>
          <div className="panel"><h3>By user</h3><Bars rows={(summary.data?.byUser ?? []).slice(0, 6).map((u) => ({ label: u.userName ?? 'device', count: u.count }))} /></div>
          <div className="panel"><h3>By action</h3><Bars rows={(summary.data?.byAction ?? []).slice(0, 6).map((a) => ({ label: a.action, count: a.count }))} /></div>
        </div>
        <div className="row" style={{ marginBottom: 12 }}>
          <input placeholder="Search path, action, body, user…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} style={{ maxWidth: 320 }} />
          <select value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }} style={{ maxWidth: 260 }}><option value="">All actions</option>{summary.data?.actions.map((a) => <option key={a}>{a}</option>)}</select>
          <select value={userId} onChange={(e) => { setUserId(e.target.value); setPage(1); }} style={{ maxWidth: 220 }}><option value="">All users</option>{summary.data?.byUser.filter((u) => u.userId).map((u) => <option key={u.userId!} value={u.userId!}>{u.userName}</option>)}</select>
          <span className="muted small">{audit.data?.total ?? 0} entries</span>
        </div>
        <div className="panel table-wrap"><table><thead><tr><th>When</th><th>User</th><th>Action</th><th>Path</th><th>Status</th><th>Entity</th><th></th></tr></thead>
          <tbody>{audit.data?.items.map((a) => <tr key={a.id} className="clickable" onClick={() => setOpen(open?.id === a.id ? null : a)}><td className="small muted">{fmt.dt(a.at)}</td><td>{a.userName ?? (a.deviceId ? 'device' : '—')}</td><td className="mono small">{a.action}</td><td className="mono small">{a.method} {a.path}</td><td><Badge tone={statusTone(a.statusCode)}>{a.statusCode}</Badge></td><td className="small">{a.entityType}{a.entityId ? <span className="muted"> {a.entityId.slice(0, 8)}…</span> : ''}</td><td className="small muted">{a.durationMs} ms</td></tr>)}
            {audit.data?.items.length === 0 && <tr><td colSpan={7} className="empty">No audit entries match.</td></tr>}</tbody></table>
          {audit.data && <Pager page={audit.data.page} pageSize={audit.data.pageSize} total={audit.data.total} onPage={setPage} />}</div>
        {open && <div className="panel" style={{ marginTop: 12 }}><div className="topbar"><h3 style={{ margin: 0 }}>{open.action} · {fmt.dt(open.at)}</h3><button className="sm" onClick={() => setOpen(null)}>✕</button></div>
          <dl className="kv"><dt>User</dt><dd>{open.userName ?? '—'} {open.userId && <span className="mono small muted">{open.userId}</span>}</dd><dt>Request</dt><dd className="mono small">{open.method} {open.path}</dd><dt>From</dt><dd className="small">{open.ipAddress} · {open.userAgent}</dd><dt>Result</dt><dd>HTTP {open.statusCode} in {open.durationMs} ms</dd></dl>
          <h4 style={{ margin: '8px 0 4px' }}>Request body (secrets redacted)</h4><pre className="small" style={{ whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>{open.body ? (() => { try { return JSON.stringify(JSON.parse(open.body!), null, 2); } catch { return open.body; } })() : '—'}</pre></div>}
      </>}
      {tab === 'retention' && <>
        <div className="topbar"><p className="muted small" style={{ margin: 0 }}>Rows older than the retention window are deleted in batches by the retention job (every 6 hours, on the lease holder). Item events are the chain of custody and are kept forever unless you deliberately set a window.</p><div className="row">{Object.keys(edits).length > 0 && <button className="primary" disabled={busy} onClick={save}>Save changes</button>}<button disabled={busy} onClick={run}>Run retention now</button></div></div>
        <ErrorBox error={error ?? retention.error} />
        {runResult && <div className="panel" style={{ marginBottom: 12 }}>Deleted: {Object.entries(runResult).filter(([, v]) => v > 0).map(([k, v]) => `${k} ${v}`).join(', ') || 'nothing was expired'} <button className="sm" onClick={() => setRunResult(null)}>✕</button></div>}
        <div className="panel table-wrap"><table><thead><tr><th>Dataset</th><th>Rows</th><th>Keep for (days)</th><th>Enabled</th><th>Expired now</th><th>Last run</th><th>Deleted total</th></tr></thead>
          <tbody>{retention.data?.map((r) => { const e = eff(r); return <tr key={r.dataset}><td><b className="mono">{r.dataset}</b>{r.protected && <Badge tone="info">protected</Badge>}<div className="muted small">{r.description}</div></td><td>{r.rows.toLocaleString()}</td><td><input type="number" min={1} style={{ width: 90 }} value={e.retainDays ?? ''} placeholder="forever" onChange={(ev) => setEdits({ ...edits, [r.dataset]: { retainDays: ev.target.value ? Number(ev.target.value) : null, enabled: ev.target.value ? e.enabled : false } })} /></td><td><input type="checkbox" style={{ width: 'auto' }} checked={e.enabled && e.retainDays != null} disabled={e.retainDays == null} onChange={(ev) => setEdits({ ...edits, [r.dataset]: { ...e, enabled: ev.target.checked } })} /></td><td>{r.expired > 0 ? <Badge tone="warn">{r.expired.toLocaleString()}</Badge> : <span className="muted">0</span>}</td><td className="small muted">{r.lastRunAt ? `${fmt.ago(r.lastRunAt)} · ${r.lastDeleted}` : 'never'}</td><td className="small">{r.totalDeleted.toLocaleString()}</td></tr>; })}</tbody></table></div>
      </>}
    </div>
  );
}

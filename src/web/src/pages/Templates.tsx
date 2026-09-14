import { useState } from 'react';
import { useApplyTemplate, useTemplates } from '../api/hooks';
import { Badge, ErrorBox } from '../components/ui';

export default function Templates() {
  const { data } = useTemplates();
  const apply = useApplyTemplate();
  const [msg, setMsg] = useState<string | null>(null);
  const run = async (code: string) => { const r = await apply.mutateAsync(code); setMsg(`${code}: ${r.itemTypesCreated} item types and ${r.rulesCreated} rules installed (${r.skipped} already present).`); };
  return (
    <div>
      <div className="topbar"><h1>Solution templates</h1></div>
      <p className="muted small">A template installs the item types, lifecycles and rules for a vertical. Everything is editable afterwards, and several templates can be combined in one tenant (e.g. a hospital running linen + medical assets + surgical instruments).</p>
      {msg && <div className="success" style={{ marginBottom: 12 }}>{msg}</div>}
      <ErrorBox error={apply.error} />
      <div className="grid cols-2">
        {data?.map((t) => <div className="panel" key={t.code}>
          <div className="topbar"><div><h2 style={{ marginBottom: 2 }}>{t.name}</h2><span className="muted small">{t.vertical}</span></div>{t.installed ? <Badge tone="ok">Installed</Badge> : <button className="primary sm" disabled={apply.isPending} onClick={() => run(t.code)}>Install</button>}</div>
          <p>{t.description}</p>
          <h3>Item types</h3>
          {t.itemTypes.map((i) => <div key={i.code} style={{ marginBottom: 4 }}><b>{i.name}</b> <span className="muted small">{i.code}</span> {i.installed && <Badge tone="ok">✓</Badge>} {i.isContainer && <Badge tone="info">container</Badge>} {i.tracksCycles && <Badge>cycles</Badge>} {i.tracksExpiry && <Badge>expiry</Badge>} {i.requiresInspection && <Badge>inspection</Badge>}{i.states && i.states.length > 0 && <div className="muted small">{i.states.join(' → ')}</div>}</div>)}
          <h3 style={{ marginTop: 10 }}>Rules</h3>
          {t.rules.map((r) => <div key={r.name} className="small"><Badge tone={r.severity === 'Critical' ? 'crit' : r.severity === 'Warning' ? 'warn' : 'info'}>{r.severity}</Badge> {r.name} <span className="muted">on {r.trigger}</span></div>)}
          {t.rules.length === 0 && <span className="muted small">None</span>}
          <h3 style={{ marginTop: 10 }}>Operations</h3><div className="chips">{t.operations.map((o) => <span key={o} className="chip">{o}</span>)}</div>
        </div>)}
      </div>
    </div>
  );
}

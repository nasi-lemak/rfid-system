import { useRef, useState } from 'react';
import { get, post } from '../api/client';
import { useApplyTemplate, useSeedDemo, useTemplates } from '../api/hooks';
import { Badge, ErrorBox } from '../components/ui';

export default function Templates() {
  const { data } = useTemplates();
  const apply = useApplyTemplate();
  const seed = useSeedDemo();
  const [msg, setMsg] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const exportCfg = async () => { const version = prompt('Package version', '1.0.0') ?? '1.0.0'; const d = await get<{ signature?: { keyId: string } | null }>('/api/templates/export', { version }); const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([JSON.stringify(d, null, 2)], { type: 'application/json' })); a.download = 'rfid-template-export.json'; a.click(); URL.revokeObjectURL(a.href); };
  const importCfg = async (f: File) => { try { const j = JSON.parse(await f.text()); const r = await post<{ itemTypesCreated: number; rulesCreated: number; operationsCreated: number; workflowsCreated: number; skipped: number; signature: { status: string; publisher?: string | null; reason?: string | null }; version?: string }>('/api/templates/import', j.definition ? j : { code: 'custom', vertical: 'Custom', definition: j }); const sig = r.signature.status === 'Valid' ? `signed by ${r.signature.publisher}` : r.signature.status === 'Unsigned' ? 'unsigned' : `${r.signature.status.toLowerCase()} signature`; setMsg(`Imported v${r.version ?? '?'} (${sig}): ${r.itemTypesCreated} item types, ${r.rulesCreated} rules, ${r.operationsCreated} operations, ${r.workflowsCreated} workflows (${r.skipped} skipped).`); } catch (e) { setMsg(`Import failed: ${(e as Error).message}`); } };
  const run = async (code: string) => { const r = await apply.mutateAsync(code); setMsg(`${code}: ${r.itemTypesCreated} item types and ${r.rulesCreated} rules installed (${r.skipped} already present).`); };
  const demo = async (code: string) => {
    const r = await seed.mutateAsync(code);
    setMsg(r.skipped ? `${code}: demo data already loaded.` : `${code}: loaded ${r.items} tagged items, ${r.locations} locations, ${r.parties} parties, ${r.devices} readers, ${r.operations} historical operations, ${r.reads} reads → ${r.alerts} alert(s).${r.warnings.length ? ' Warnings: ' + r.warnings.join('; ') : ''}`);
  };
  return (
    <div>
      <div className="topbar"><h1>Solution templates</h1><div className="row"><button onClick={exportCfg} title="Download this tenant's item types and rules as a template JSON">Export configuration</button><button onClick={() => fileRef.current?.click()}>Import template JSON</button><input ref={fileRef} type="file" accept="application/json" hidden onChange={(e) => e.target.files?.[0] && importCfg(e.target.files[0])} /></div></div>
      <p className="muted small">A template installs the item types, lifecycles and rules for a vertical. Everything is editable afterwards, and several templates can be combined in one tenant (e.g. a hospital running linen + medical assets + surgical instruments). <b>Load demo data</b> adds a realistic site for that vertical – locations, parties, tagged items, readers and a few days of history – so you can explore the workflows immediately.</p>
      {msg && <div className="success" style={{ marginBottom: 12 }}>{msg}</div>}
      <ErrorBox error={apply.error ?? seed.error} />
      <div className="grid cols-2">
        {data?.map((t) => <div className="panel" key={t.code}>
          <div className="topbar"><div><h2 style={{ marginBottom: 2 }}>{t.name}</h2><span className="muted small">{t.vertical}</span></div><div className="row">{t.installed ? <Badge tone="ok">Installed</Badge> : <button className="primary sm" disabled={apply.isPending} onClick={() => run(t.code)}>Install</button>}{t.hasDemo && (t.demoSeeded ? <Badge tone="info" >Demo: {t.demoSite}</Badge> : <button className="sm" disabled={seed.isPending} onClick={() => demo(t.code)} title={`Loads "${t.demoSite}" with sample items, readers and history`}>{seed.isPending ? 'Loading…' : 'Load demo data'}</button>)}</div></div>
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

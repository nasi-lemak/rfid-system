import { useState } from 'react';
import { post } from '../api/client';
import { useItemTypes } from '../api/hooks';
import type { ImportResult, ReconcileResult } from '../api/types';
import { Badge, ErrorBox } from '../components/ui';

const sampleCsv = `AssetNumber,Description,AssetClass,Location,Owner,AcquisitionValue,CapitalizationDate,EPC,CostCenter
LT-9001,Dell Latitude 5550,IT-ASSET,IT Store,Jane Lee,1499,2026-03-01,,CC-200
PRJ-9001,Projector,ASSET,Meeting Room 2.1,Finance,1100,2025-11-15,,CC-200`;

export default function Import() {
  const types = useItemTypes();
  const [text, setText] = useState(sampleCsv);
  const [defaultType, setDefaultType] = useState('');
  const [source, setSource] = useState('erp');
  const [opts, setOpts] = useState({ createMissingLocations: true, createMissingParties: true, updateExisting: true });
  const [result, setResult] = useState<ImportResult | null>(null);
  const [rec, setRec] = useState<ReconcileResult | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const body = () => ({ ...(text.trim().startsWith('[') || text.trim().startsWith('{') ? { json: text } : { csv: text }), defaultType: defaultType || null, source, ...opts });
  const run = async (dryRun: boolean) => { setBusy(true); setError(null); setRec(null); try { setResult(await post<ImportResult>('/api/import/items', { ...body(), dryRun })); } catch (e) { setError(e); } finally { setBusy(false); } };
  const reconcile = async () => { setBusy(true); setError(null); setResult(null); try { setRec(await post<ReconcileResult>('/api/import/reconcile', body())); } catch (e) { setError(e); } finally { setBusy(false); } };
  return (
    <div>
      <div className="topbar"><h1>ERP import & reconciliation</h1><div className="row"><button onClick={reconcile} disabled={busy}>Reconcile (read-only)</button><button onClick={() => run(true)} disabled={busy}>Dry run</button><button className="primary" onClick={() => { if (confirm('Import now? Items are created or updated for real. Use Dry run first to preview the result.')) run(false); }} disabled={busy}>Import</button></div></div>
      <p className="muted small">Paste an ERP/EAM/CMDB extract as CSV (header row; columns like AssetNumber/Identifier, Description/Name, AssetClass/Type, Location, Owner/Custodian, AcquisitionValue/Cost, CapitalizationDate/PurchasedAt, EPC, Quantity, Lot, Expiry — unknown columns become attributes) or a JSON array. Items are matched by identifier. Scripted feeds can POST the same CSV to <code>/api/import/items/csv</code>.</p>
      <div className="grid" style={{ gridTemplateColumns: '1fr 300px' }}>
        <div className="panel"><textarea className="mono" rows={14} value={text} onChange={(e) => setText(e.target.value)} /></div>
        <div className="panel form">
          <label>Default item type (when a row has none)<select value={defaultType} onChange={(e) => setDefaultType(e.target.value)}><option value="">—</option>{types.data?.map((t) => <option key={t.id} value={t.code}>{t.name} ({t.code})</option>)}</select></label>
          <label>Source tag<input value={source} onChange={(e) => setSource(e.target.value)} /></label>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={opts.createMissingLocations} onChange={(e) => setOpts({ ...opts, createMissingLocations: e.target.checked })} /> Create missing locations</label>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={opts.createMissingParties} onChange={(e) => setOpts({ ...opts, createMissingParties: e.target.checked })} /> Create missing custodians</label>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={opts.updateExisting} onChange={(e) => setOpts({ ...opts, updateExisting: e.target.checked })} /> Update existing items</label>
        </div>
      </div>
      <ErrorBox error={error} />
      {result && <div className="panel" style={{ marginTop: 12 }}>
        <div className="row" style={{ marginBottom: 8 }}>{result.dryRun && <Badge tone="info">dry run – nothing written</Badge>}<Badge tone="ok">{result.created} created</Badge><Badge tone="info">{result.updated} updated</Badge><Badge>{result.unchanged} unchanged</Badge>{result.errors > 0 && <Badge tone="crit">{result.errors} errors</Badge>}</div>
        <table><thead><tr><th>Identifier</th><th>Action</th><th>Changes</th></tr></thead><tbody>{result.rows.map((r, i) => <tr key={i}><td className="mono">{r.identifier || '(blank)'}</td><td><Badge tone={r.action === 'Error' ? 'crit' : r.action === 'Created' ? 'ok' : r.action === 'Updated' ? 'info' : ''}>{r.action}</Badge></td><td className="small">{r.error ?? r.changes.join(' · ')}</td></tr>)}</tbody></table>
      </div>}
      {rec && <div className="grid cols-3" style={{ marginTop: 12 }}>
        <div className="panel"><h3>Matched: {rec.matched}</h3><h3 style={{ marginTop: 12 }}>Field differences ({rec.differences.length})</h3><table><tbody>{rec.differences.map((d, i) => <tr key={i}><td className="mono small">{d.identifier}</td><td className="small">{d.field}</td><td className="small">ERP: <b>{d.erp}</b><br />platform: {d.platform ?? '—'}</td></tr>)}</tbody></table></div>
        <div className="panel"><h3>In ERP, not in platform ({rec.missingInPlatform.length})</h3>{rec.missingInPlatform.map((x) => <div key={x} className="mono small">{x}</div>)}</div>
        <div className="panel"><h3>In platform, not in ERP ({rec.missingInErp.length})</h3>{rec.missingInErp.map((x) => <div key={x} className="mono small">{x}</div>)}</div>
      </div>}
    </div>
  );
}

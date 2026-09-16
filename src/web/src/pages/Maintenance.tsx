import { useState } from 'react';
import { Link } from 'react-router-dom';
import { post } from '../api/client';
import { useInvalidate, useItemTypes, useMaintenance, useMaintenanceItem } from '../api/hooks';
import { Badge, TrendChart, fmt } from '../components/ui';

const levelTone = (l: string): 'ok' | 'warn' | 'crit' => l === 'High' ? 'crit' : l === 'Medium' ? 'warn' : 'ok';

export default function Maintenance() {
  const [typeId, setTypeId] = useState(''); const [min, setMin] = useState(0);
  const report = useMaintenance(typeId || undefined); const itemTypes = useItemTypes(); const inv = useInvalidate();
  const [sel, setSel] = useState<string | null>(null); const detail = useMaintenanceItem(sel ?? undefined);
  const [msg, setMsg] = useState<string | null>(null);
  const rows = report.data?.rows.filter((r) => r.risk >= min) ?? [];
  return (
    <div>
      <div className="topbar"><h1>Predictive maintenance</h1><div className="row"><select value={typeId} onChange={(e) => setTypeId(e.target.value)} style={{ maxWidth: 220 }}><option value="">All item types</option>{itemTypes.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select><select value={min} onChange={(e) => setMin(Number(e.target.value))} style={{ maxWidth: 150 }}><option value={0}>All risk levels</option><option value={40}>Medium and up</option><option value={70}>High only</option></select><button className="primary" onClick={async () => { const r = await post<{ forecasts: number; alerts: number }>('/api/maintenance/run'); setMsg(`Snapshot: ${r.forecasts} forecasts, ${r.alerts} new alert(s)`); inv('maintenance', 'alerts'); }}>Snapshot & alert now</button></div></div>
      <p className="muted small">Risk 0–100 from cycle wear against the type's limit, recent inspection failures, overdue inspections, drift from the item's usual service interval, usage intensity, age and open alerts. The dominant driver predicts the service date. Items scoring ≥ 70 raise a warning alert once; snapshots are kept daily for trends.{msg ? ` · ${msg}` : ''}</p>
      <div className="grid cols-4" style={{ marginBottom: 16 }}>
        <div className="panel stat"><span className="label">Items scored</span><span className="value">{report.data?.total ?? '–'}</span></div>
        <div className={'panel stat ' + ((report.data?.high ?? 0) > 0 ? 'crit' : '')}><span className="label">High risk</span><span className="value">{report.data?.high ?? '–'}</span></div>
        <div className={'panel stat ' + ((report.data?.medium ?? 0) > 0 ? 'warn' : '')}><span className="label">Medium</span><span className="value">{report.data?.medium ?? '–'}</span></div>
        <div className="panel stat ok"><span className="label">Low</span><span className="value">{report.data?.low ?? '–'}</span></div>
      </div>
      <div className="grid" style={{ gridTemplateColumns: sel ? '1fr 440px' : '1fr' }}>
        <div className="panel table-wrap"><table><thead><tr><th>Item</th><th>Type</th><th style={{ minWidth: 160 }}>Risk</th><th>Predicted service</th><th>Cycles</th><th>Last inspected</th><th>Top driver</th></tr></thead>
          <tbody>{rows.map((r) => { const top = [...r.factors].sort((a, b) => b.weight * b.value - a.weight * a.value)[0]; return <tr key={r.itemId} className="clickable" onClick={() => setSel(r.itemId)} style={sel === r.itemId ? { background: 'color-mix(in srgb, var(--primary) 8%, transparent)' } : undefined}>
            <td><Link to={`/items/${r.itemId}`} onClick={(e) => e.stopPropagation()}>{r.item}</Link><div className="muted small">{r.identifier}{r.state ? ` · ${r.state}` : ''}</div></td><td className="small">{r.itemType}</td>
            <td><div className="bar" style={{ gridTemplateColumns: '1fr 70px' }}><div className="track"><div className="fill" style={{ width: `${r.risk}%`, background: r.level === 'High' ? 'var(--crit)' : r.level === 'Medium' ? 'var(--warn)' : 'var(--ok)' }} /></div><span><Badge tone={levelTone(r.level)}>{r.risk.toFixed(0)}</Badge></span></div></td>
            <td className="small">{r.predictedServiceAt ? <>{fmt.d(r.predictedServiceAt)}<div className="muted">{r.predictedBy}</div></> : <span className="muted">—</span>}</td><td className="small">{r.cycleCount}{r.maxCycles ? ` / ${r.maxCycles}` : ''}</td><td className="small muted">{fmt.ago(r.lastInspectedAt)}</td><td className="small">{top ? `${top.name}: ${top.detail}` : ''}</td></tr>; })}
            {rows.length === 0 && <tr><td colSpan={7} className="empty">No items match. Items are scored when their type has a cycle limit or inspection interval, or they have inspection/maintenance history.</td></tr>}</tbody></table></div>
        {sel && <div className="panel"><div className="topbar"><h3 style={{ margin: 0 }}>{detail.data?.prediction.item ?? '…'}</h3><button className="sm" onClick={() => setSel(null)}>✕</button></div>
          {detail.data && <>
            <div className="row" style={{ marginBottom: 8 }}><Badge tone={levelTone(detail.data.prediction.level)}>{detail.data.prediction.level} · {detail.data.prediction.risk.toFixed(0)}/100</Badge>{detail.data.prediction.predictedServiceAt && <span className="small">service by <b>{fmt.d(detail.data.prediction.predictedServiceAt)}</b> ({detail.data.prediction.predictedBy})</span>}</div>
            <h4 style={{ margin: '8px 0 4px' }}>Factors</h4>
            <div className="bars">{detail.data.prediction.factors.map((f) => <div className="bar" key={f.name} style={{ gridTemplateColumns: '130px 1fr 40px' }}><span title={f.detail}>{f.name}</span><div className="track"><div className="fill" style={{ width: `${f.value * 100}%`, background: f.value > 0.7 ? 'var(--crit)' : f.value > 0.4 ? 'var(--warn)' : 'var(--primary)' }} /></div><span className="right small muted">×{f.weight}</span></div>)}</div>
            <ul className="small" style={{ margin: '8px 0 0 16px', padding: 0 }}>{detail.data.prediction.factors.map((f) => <li key={f.name}><b>{f.name}</b>: {f.detail}</li>)}</ul>
            {detail.data.trend.length > 1 && <><h4 style={{ margin: '12px 0 4px' }}>Risk trend</h4><TrendChart buckets={detail.data.trend.map((t) => ({ start: t.at, count: Math.round(t.risk) }))} height={100} label="risk" /></>}
          </>}</div>}
      </div>
    </div>
  );
}

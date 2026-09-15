import { useState } from 'react';
import { Link } from 'react-router-dom';
import { getToken, post } from '../api/client';
import { useAccuracy, useAlertResponse, useDwell, useInvalidate, useTrend, useUptime, useUtilization, useWarehouse } from '../api/hooks';
import { useAuth } from '../auth';
import { Badge, Bars, ErrorBox, TrendChart, fmt } from '../components/ui';

export default function Analytics() {
  const { user } = useAuth(); const isAdmin = user?.role === 'Admin';
  const [days, setDays] = useState(90); const [metric, setMetric] = useState('events'); const [bucket, setBucket] = useState('day');
  const trend = useTrend(metric, days, bucket); const util = useUtilization(days); const dwell = useDwell(days); const acc = useAccuracy(365); const resp = useAlertResponse(days); const uptime = useUptime(30);
  const wh = useWarehouse(); const inv = useInvalidate();
  const [exp, setExp] = useState({ dataset: 'events', days: 30, format: 'parquet' }); const [busy, setBusy] = useState(false); const [error, setError] = useState<unknown>(null);
  const runExport = async () => { setBusy(true); setError(null); try { await post('/api/warehouse/export', { dataset: exp.dataset, from: new Date(Date.now() - exp.days * 864e5).toISOString(), format: exp.format }); inv('warehouse'); } catch (e) { setError(e); } finally { setBusy(false); } };
  const download = async () => {
    setBusy(true); setError(null);
    try {
      const url = `/api/warehouse/download?dataset=${exp.dataset}&from=${new Date(Date.now() - exp.days * 864e5).toISOString()}&format=${exp.format}`;
      const res = await fetch(url, { headers: { Authorization: `Bearer ${getToken()}` } }); if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const blob = await res.blob(); const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `${exp.dataset}.${exp.format}`; a.click(); URL.revokeObjectURL(a.href);
    } catch (e) { setError(e); } finally { setBusy(false); }
  };
  const bytes = (n: number) => n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(1)} KB` : `${(n / 1048576).toFixed(1)} MB`;
  return (
    <div>
      <div className="topbar"><h1>Analytics</h1><div className="row"><select value={days} onChange={(e) => setDays(Number(e.target.value))} style={{ maxWidth: 130 }}>{[7, 30, 90, 180, 365].map((d) => <option key={d} value={d}>last {d} d</option>)}</select></div></div>
      <div className="grid cols-2" style={{ marginBottom: 16 }}>
        <div className="panel" style={{ gridColumn: 'span 2' }}>
          <div className="topbar"><h3 style={{ margin: 0 }}>Activity trend</h3><div className="row"><select value={metric} onChange={(e) => setMetric(e.target.value)} style={{ maxWidth: 150 }}>{['events', 'reads', 'operations', 'alerts', 'moves', 'gps', 'notifications'].map((m) => <option key={m}>{m}</option>)}</select><select value={bucket} onChange={(e) => setBucket(e.target.value)} style={{ maxWidth: 110 }}><option value="day">per day</option><option value="week">per week</option></select><span className="muted small">{trend.data?.total ?? 0} total</span></div></div>
          {trend.data && <TrendChart buckets={trend.data.buckets} height={180} label={metric} />}
        </div>
        <div className="panel"><h3>Asset utilisation · by item type</h3><p className="muted small">Share of items with any activity in the window, moves and average cycles.</p>
          <table><thead><tr><th>Type</th><th>Items</th><th>Utilised</th><th>Moves</th><th>In custody</th><th>Avg cycles</th></tr></thead>
            <tbody>{util.data?.map((r) => <tr key={r.itemTypeId}><td>{r.itemType}</td><td>{r.items}</td><td><div className="bar" style={{ gridTemplateColumns: '1fr 44px' }}><div className="track"><div className="fill" style={{ width: `${r.utilizationPercent}%` }} /></div><span className="right small">{r.utilizationPercent}%</span></div></td><td>{r.movedInWindow}</td><td>{r.inCustody}</td><td>{r.avgCycles}</td></tr>)}</tbody></table></div>
        <div className="panel"><h3>Dwell time · by zone</h3><p className="muted small">Presence sessions: average, p90 and maximum minutes per zone.</p>
          <table><thead><tr><th>Zone</th><th>Sessions</th><th>Items</th><th>Avg</th><th>p90</th><th>Max</th></tr></thead>
            <tbody>{dwell.data?.slice(0, 12).map((r) => <tr key={r.locationId}><td>{r.location}<div className="muted small">{r.kind}</div></td><td>{r.sessions}</td><td>{r.distinctItems}</td><td>{r.avgMinutes} min</td><td>{r.p90Minutes} min</td><td>{r.maxMinutes} min</td></tr>)}{dwell.data?.length === 0 && <tr><td colSpan={6} className="muted">No presence sessions in the window</td></tr>}</tbody></table></div>
        <div className="panel"><div className="topbar"><h3 style={{ margin: 0 }}>Inventory accuracy · last 12 months</h3>{acc.data?.overallAccuracy != null && <Badge tone={acc.data.overallAccuracy >= 98 ? 'ok' : acc.data.overallAccuracy >= 90 ? 'warn' : 'crit'}>{acc.data.overallAccuracy}% over {acc.data.stocktakes} stocktakes</Badge>}</div>
          {acc.data && acc.data.rows.length > 1 && <TrendChart buckets={acc.data.rows.map((r) => ({ start: r.at, count: r.accuracy ?? 0 }))} height={110} label="% found" />}
          <table><thead><tr><th>Stocktake</th><th>Expected</th><th>Found</th><th>Missing</th><th>Accuracy</th></tr></thead>
            <tbody>{acc.data?.rows.slice(-8).reverse().map((r) => <tr key={r.id}><td><Link to={`/stocktakes/${r.id}`}>{r.name}</Link><div className="muted small">{r.location} · {fmt.ago(r.at)}</div></td><td>{r.expected}</td><td>{r.found}</td><td>{r.missing}</td><td>{r.accuracy != null ? `${r.accuracy}%` : '—'}</td></tr>)}{acc.data?.rows.length === 0 && <tr><td colSpan={5} className="muted">No reconciled stocktakes yet</td></tr>}</tbody></table></div>
        <div className="panel"><h3>Alert response</h3><p className="muted small">Mean time to acknowledge / close, escalations, still open.</p>
          <table><thead><tr><th>Severity</th><th>Raised</th><th>Open</th><th>MTTA</th><th>MTTR</th><th>Escalated</th></tr></thead>
            <tbody>{resp.data?.map((r) => <tr key={r.severity}><td><Badge tone={r.severity === 'Critical' ? 'crit' : r.severity === 'Warning' ? 'warn' : 'info'}>{r.severity}</Badge></td><td>{r.raised}</td><td>{r.openNow}</td><td>{r.avgMinutesToAck != null ? `${r.avgMinutesToAck} min` : '—'}</td><td>{r.avgMinutesToClose != null ? `${r.avgMinutesToClose} min` : '—'}</td><td>{r.escalated}</td></tr>)}</tbody></table>
          <h3 style={{ marginTop: 14 }}>Reader uptime · 30 days</h3>
          <Bars rows={(uptime.data ?? []).slice(0, 10).map((r) => ({ label: r.name, count: Math.round(r.uptimePercent) }))} /></div>
      </div>
      {isAdmin && <div className="panel">
        <div className="topbar"><h3 style={{ margin: 0 }}>Data warehouse export</h3><span className="muted small">{wh.data?.enabled ? <Badge tone="ok">scheduled every {wh.data.intervalMinutes} min</Badge> : <Badge>scheduler off (Warehouse:Enabled)</Badge>} · {wh.data?.format} · landing zone <code>{wh.data?.path}</code></span></div>
        <p className="muted small">Datasets land as Parquet (or CSV) files partitioned by day – point Snowflake, BigQuery, Databricks, Synapse or DuckDB at the folder. Incremental runs continue from the last exported timestamp; <i>items</i> is a daily snapshot.</p>
        <div className="row" style={{ marginBottom: 12 }}>
          <select value={exp.dataset} onChange={(e) => setExp({ ...exp, dataset: e.target.value })} style={{ maxWidth: 200 }}>{wh.data?.datasets.map((d) => <option key={d.name} value={d.name}>{d.name}{d.snapshot ? ' (snapshot)' : ''}</option>)}</select>
          <select value={exp.days} onChange={(e) => setExp({ ...exp, days: Number(e.target.value) })} style={{ maxWidth: 130 }}>{[1, 7, 30, 90, 365].map((d) => <option key={d} value={d}>last {d} d</option>)}</select>
          <select value={exp.format} onChange={(e) => setExp({ ...exp, format: e.target.value })} style={{ maxWidth: 110 }}><option value="parquet">Parquet</option><option value="csv">CSV</option></select>
          <button disabled={busy} onClick={download}>Download</button><button disabled={busy} onClick={runExport}>Export to landing zone</button><button className="primary" disabled={busy} onClick={async () => { setBusy(true); try { await post('/api/warehouse/run'); inv('warehouse'); } finally { setBusy(false); } }}>Run incremental export now</button>
        </div>
        <ErrorBox error={error} />
        <div className="grid cols-2">
          <div><h4 style={{ margin: '0 0 6px' }}>Datasets</h4><table><tbody>{wh.data?.datasets.map((d) => <tr key={d.name}><td className="mono">{d.name}</td><td className="small muted">{d.snapshot ? 'daily snapshot' : 'incremental'}</td><td className="small">{d.lastExportedTo ? `exported to ${fmt.ago(d.lastExportedTo)}` : 'never'}</td></tr>)}</tbody></table></div>
          <div><h4 style={{ margin: '0 0 6px' }}>Recent runs</h4><table><tbody>{wh.data?.runs.slice(0, 10).map((r) => <tr key={r.id}><td className="mono small">{r.dataset}</td><td className="small">{r.rows} rows · {bytes(r.bytes)}{r.manual ? ' · manual' : ''}</td><td className="small muted">{fmt.ago(r.startedAt)}</td><td>{r.error ? <Badge tone="crit" >failed</Badge> : <Badge tone="ok">{r.format}</Badge>}</td></tr>)}{wh.data?.runs.length === 0 && <tr><td className="muted">No runs yet</td></tr>}</tbody></table></div>
        </div>
      </div>}
    </div>
  );
}

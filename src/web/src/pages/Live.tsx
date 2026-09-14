import { Link } from 'react-router-dom';
import { useDevices, useLocations } from '../api/hooks';
import { useLive } from '../live';
import { Badge, fmt, toneForSeverity } from '../components/ui';

export default function Live() {
  const { connected, reads, alerts, clearReads } = useLive();
  const devices = useDevices();
  const locs = useLocations();
  const dn = (id?: string | null) => devices.data?.find((d) => d.id === id)?.name ?? '—';
  const ln = (id?: string | null) => locs.data?.find((l) => l.id === id)?.name ?? '—';
  const byEpc = new Map<string, typeof reads[number] & { count: number }>();
  for (const r of reads) { const e = byEpc.get(r.epc); if (e) e.count++; else byEpc.set(r.epc, { ...r, count: 1 }); }
  return (
    <div>
      <div className="topbar"><h1>{connected && <span className="live-dot" />}Live reads</h1><div className="row"><span className="muted small">{connected ? 'Connected to live hub' : 'Disconnected'} · {reads.length} reads buffered</span><button onClick={clearReads}>Clear</button></div></div>
      <div className="grid" style={{ gridTemplateColumns: '2fr 1fr' }}>
        <div className="panel table-wrap">
          <h3>Tags seen (this session)</h3>
          <table><thead><tr><th>EPC</th><th>Item</th><th>Reader</th><th>Location</th><th>RSSI</th><th>Reads</th><th>Last</th></tr></thead>
            <tbody>{[...byEpc.values()].map((r) => <tr key={r.epc}><td className="mono">{r.epc}</td><td>{r.itemId ? <Link to={`/items/${r.itemId}`}>{r.itemName}</Link> : <Badge tone="info">unknown</Badge>}</td><td>{dn(r.deviceId)}</td><td>{ln(r.locationId)}</td><td>{r.rssi != null ? `${r.rssi} dBm` : ''}</td><td>{r.count}</td><td className="small muted">{fmt.ago(r.readAt)}</td></tr>)}</tbody></table>
          {reads.length === 0 && <div className="empty">Waiting for reads from readers and handhelds… Post to <code>/api/ingest/reads</code> to see them here.</div>}
        </div>
        <div className="panel"><h3>Live alerts</h3>{alerts.map((a) => <div key={a.id} className="row" style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><Badge tone={toneForSeverity(a.severity)}>{a.severity}</Badge><span style={{ flex: 1 }}>{a.message}</span></div>)}{alerts.length === 0 && <span className="muted">None this session</span>}</div>
      </div>
    </div>
  );
}

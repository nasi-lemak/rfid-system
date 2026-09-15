import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useFloorPlan, useFloorPlans, useHeatMap, useHistoryItems, useLocations, useMuster, usePathHistory, useTiming, useZones } from '../api/hooks';
import { Badge, fmt } from '../components/ui';

export default function Presence() {
  const [tab, setTab] = useState<'zones' | 'muster' | 'timing' | 'floor'>('zones');
  const plans = useFloorPlans(); const [planId, setPlanId] = useState(''); const activePlan = planId || plans.data?.[0]?.id; const plan = useFloorPlan(activePlan);
  const [layer, setLayer] = useState<'live' | 'heat' | 'replay'>('live'); const [hours, setHours] = useState(24); const [cellM, setCellM] = useState(1);
  const heat = useHeatMap(activePlan, hours, cellM, layer === 'heat');
  const histItems = useHistoryItems(layer === 'replay' ? activePlan : undefined, hours);
  const [replayItem, setReplayItem] = useState(''); const path = usePathHistory(layer === 'replay' ? (replayItem || histItems.data?.[0]?.itemId) : undefined, hours);
  const [cursor, setCursor] = useState(1); // 0..1 fraction of the path shown
  const locs = useLocations();
  const sites = locs.data?.filter((l) => l.kind === 'Site') ?? [];
  const events = locs.data?.filter((l) => locs.data?.some((c) => c.kind === 'Checkpoint' && c.parentId === l.id)) ?? [];
  const [site, setSite] = useState(''); const [under, setUnder] = useState(''); const [event, setEvent] = useState('');
  const zones = useZones(under || undefined);
  const muster = useMuster(site || (sites[0]?.id));
  const timing = useTiming(event || events[0]?.id);
  const hms = (s?: number | null) => s == null ? '—' : `${Math.floor(s / 3600).toString().padStart(2, '0')}:${Math.floor((s % 3600) / 60).toString().padStart(2, '0')}:${Math.floor(s % 60).toString().padStart(2, '0')}`;
  return (
    <div>
      <div className="topbar"><h1>Presence & location</h1><span className="muted small">Zone presence from fixed readers, BLE/UWB gateways and portals · sessions close after the zone's dwell timeout</span></div>
      <div className="tabs">{(['zones', 'floor', 'muster', 'timing'] as const).map((t) => <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t === 'zones' ? 'Zone occupancy' : t === 'floor' ? 'Floor plan (x/y)' : t === 'muster' ? 'Muster roll-call' : 'Checkpoint timing'}</button>)}</div>
      {tab === 'zones' && <>
        <div className="row" style={{ marginBottom: 12 }}><select value={under} onChange={(e) => setUnder(e.target.value)} style={{ maxWidth: 320 }}><option value="">All sites</option>{sites.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select><span className="muted small">{zones.data?.reduce((a, z) => a + z.present, 0) ?? 0} items present in {zones.data?.length ?? 0} zones</span></div>
        <div className="grid cols-3">{zones.data?.map((z) => <div className="panel" key={z.locationId}><div className="topbar"><h2 style={{ margin: 0 }}>{z.location}</h2><span className="row"><Badge>{z.kind}</Badge><Badge tone="ok">{z.present}</Badge></span></div>
          <table><tbody>{z.items.map((i) => <tr key={i.itemId}><td><Link to={`/items/${i.itemId}`}>{i.person ?? i.name}</Link><div className="muted small">{i.itemType} · {i.identifier}</div></td><td className="right small muted">{i.dwellMinutes.toFixed(0)} min<br />{i.rssi != null ? `${i.rssi.toFixed(0)} dBm` : ''}</td></tr>)}</tbody></table></div>)}
          {zones.data?.length === 0 && <div className="panel empty">No open presence sessions. They appear as soon as readers post sightings (fixed readers, MQTT, vendor webhooks).</div>}</div>
      </>}
      {tab === 'floor' && <>
        <div className="row" style={{ marginBottom: 12 }}><select value={planId || plans.data?.[0]?.id || ''} onChange={(e) => setPlanId(e.target.value)} style={{ maxWidth: 320 }}>{plans.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select><span className="muted small">Antennas with x/y act as anchors; positions are RSSI trilateration (log-distance model). Set <code>widthM</code>/<code>heightM</code> on the location.</span></div>
        {plans.data?.length === 0 && <div className="panel empty">No floor plans yet. Give antennas x/y coordinates (Readers &amp; devices) and a location <code>widthM</code>/<code>heightM</code>.</div>}
        {plan.data && (() => { const w = plan.data.widthM ?? Math.max(10, ...plan.data.anchors.map((a) => a.x + 1)); const h = plan.data.heightM ?? Math.max(10, ...plan.data.anchors.map((a) => a.y + 1)); const sc = Math.min(1100 / w, 600 / h);
          const pts = path.data?.points ?? []; const shown = pts.slice(0, Math.max(1, Math.round(pts.length * cursor))); const head = shown[shown.length - 1]; const maxSec = Math.max(1, ...(heat.data?.cells.map((c) => c.seconds) ?? [1]));
          return (
          <div className="grid" style={{ gridTemplateColumns: '1fr 300px' }}>
            <div className="panel" style={{ overflow: 'auto' }}>
              <div className="row" style={{ marginBottom: 8, flexWrap: 'wrap' }}>
                <div className="tabs" style={{ marginBottom: 0, borderBottom: 'none' }}>{(['live', 'heat', 'replay'] as const).map((l) => <button key={l} className={layer === l ? 'active' : ''} onClick={() => setLayer(l)}>{l === 'live' ? 'Live positions' : l === 'heat' ? 'Heat map' : 'Path replay'}</button>)}</div>
                {layer !== 'live' && <select value={hours} onChange={(e) => setHours(Number(e.target.value))} style={{ maxWidth: 140 }}>{[1, 4, 8, 24, 72, 168].map((hh) => <option key={hh} value={hh}>last {hh < 24 ? `${hh} h` : `${hh / 24} d`}</option>)}</select>}
                {layer === 'heat' && <select value={cellM} onChange={(e) => setCellM(Number(e.target.value))} style={{ maxWidth: 120 }}>{[0.5, 1, 2, 5].map((c) => <option key={c} value={c}>{c} m cells</option>)}</select>}
                {layer === 'replay' && <select value={replayItem || histItems.data?.[0]?.itemId || ''} onChange={(e) => { setReplayItem(e.target.value); setCursor(1); }} style={{ maxWidth: 260 }}>{histItems.data?.map((i) => <option key={i.itemId} value={i.itemId}>{i.name ?? i.identifier} · {i.fixes} fixes</option>)}{histItems.data?.length === 0 && <option value="">No recorded paths</option>}</select>}
              </div>
              <svg width={w * sc + 40} height={h * sc + 40} style={{ background: 'var(--bg)', borderRadius: 8 }}>
                <g transform="translate(20,20)">
                  <rect x={0} y={0} width={w * sc} height={h * sc} fill="none" stroke="var(--border)" strokeDasharray="6 4" />
                  {layer === 'heat' && heat.data?.cells.map((c) => <rect key={`${c.ix}:${c.iy}`} x={c.x * sc} y={c.y * sc} width={heat.data!.cellM * sc} height={heat.data!.cellM * sc} fill="var(--crit)" fillOpacity={0.12 + 0.75 * Math.sqrt(c.seconds / maxSec)}><title>{`${c.x}–${c.x + heat.data!.cellM} m, ${c.y}–${c.y + heat.data!.cellM} m: ${Math.round(c.seconds / 60)} min dwell · ${c.items} item(s) · ${c.samples} samples`}</title></rect>)}
                  {Array.from({ length: Math.floor(w) + 1 }).filter((_, i) => i % 5 === 0).map((_, i) => <line key={'gx' + i} x1={i * 5 * sc} y1={0} x2={i * 5 * sc} y2={h * sc} stroke="var(--border)" strokeOpacity={0.5} />)}
                  {Array.from({ length: Math.floor(h) + 1 }).filter((_, i) => i % 5 === 0).map((_, i) => <line key={'gy' + i} x1={0} y1={i * 5 * sc} x2={w * sc} y2={i * 5 * sc} stroke="var(--border)" strokeOpacity={0.5} />)}
                  {plan.data.anchors.map((a) => <g key={a.antennaId} transform={`translate(${a.x * sc},${a.y * sc})`}><rect x={-7} y={-7} width={14} height={14} fill="var(--warn)" rx={2} /><text x={10} y={4} fontSize={11} fill="var(--muted)">{a.device} #{a.port}</text></g>)}
                  {layer === 'live' && plan.data.items.map((i) => <g key={i.itemId} transform={`translate(${i.x * sc},${i.y * sc})`}><circle r={Math.max(6, (i.accuracyM ?? 1) * sc)} fill="var(--primary)" fillOpacity={0.15} /><circle r={6} fill="var(--primary)" /><text x={9} y={4} fontSize={12} fill="var(--text)">{i.person ?? i.name}</text></g>)}
                  {layer === 'replay' && shown.length > 0 && <>
                    <polyline points={shown.map((p) => `${p.x * sc},${p.y * sc}`).join(' ')} fill="none" stroke="var(--primary)" strokeWidth={2} strokeOpacity={0.8} />
                    {shown.map((p, i) => <circle key={i} cx={p.x * sc} cy={p.y * sc} r={2.5} fill="var(--primary)" fillOpacity={0.5} />)}
                    {head && <g transform={`translate(${head.x * sc},${head.y * sc})`}><circle r={Math.max(6, (head.accuracyM ?? 1) * sc)} fill="var(--primary)" fillOpacity={0.15} /><circle r={7} fill="var(--primary)" stroke="#fff" strokeWidth={2} /><text x={10} y={4} fontSize={12} fill="var(--text)">{path.data?.item?.name} · {new Date(head.at).toLocaleTimeString()}</text></g>}
                  </>}
                </g>
              </svg>
              {layer === 'replay' && pts.length > 0 && <div className="row" style={{ marginTop: 8 }}><input type="range" min={0} max={1} step={0.001} value={cursor} onChange={(e) => setCursor(Number(e.target.value))} style={{ flex: 1 }} /><span className="small muted" style={{ minWidth: 220 }}>{shown.length}/{pts.length} fixes · {new Date(pts[0].at).toLocaleString()} → {head ? new Date(head.at).toLocaleTimeString() : ''}</span></div>}
              <div className="muted small">{w} m × {h} m · {plan.data.anchors.length} anchors · {layer === 'live' ? `${plan.data.items.length} positioned items (last 12 h)` : layer === 'heat' ? `${heat.data?.cells.length ?? 0} cells with dwell in the last ${hours} h (darker = longer)` : `${pts.length} fixes in the last ${hours} h`}</div>
            </div>
            {layer === 'live' && <div className="panel"><h3>Positioned items</h3>{plan.data.items.map((i) => <div key={i.itemId} style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><Link to={`/items/${i.itemId}`}>{i.person ?? i.name}</Link><div className="muted small">({i.x}, {i.y}) m ± {i.accuracyM} · {fmt.ago(i.at)}</div></div>)}{plan.data.items.length === 0 && <span className="muted">Nothing positioned recently</span>}</div>}
            {layer === 'heat' && <div className="panel"><h3>Hot spots</h3>{heat.data?.cells.slice(0, 12).map((c) => <div key={`${c.ix}:${c.iy}`} style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><b>({c.x}, {c.y}) m</b> · {Math.round(c.seconds / 60)} min<div className="muted small">{c.items} item(s) · {c.samples} samples</div></div>)}{heat.data?.cells.length === 0 && <span className="muted">No position history in this window</span>}</div>}
            {layer === 'replay' && <div className="panel"><h3>Recorded paths</h3>{histItems.data?.map((i) => <div key={i.itemId} style={{ padding: '6px 0', borderBottom: '1px solid var(--border)', cursor: 'pointer', fontWeight: i.itemId === (replayItem || histItems.data?.[0]?.itemId) ? 600 : 400 }} onClick={() => { setReplayItem(i.itemId); setCursor(1); }}>{i.name ?? i.identifier}<div className="muted small">{i.fixes} fixes · {fmt.ago(i.first)} → {fmt.ago(i.last)}</div></div>)}{histItems.data?.length === 0 && <span className="muted">No fixes recorded in this window</span>}</div>}
          </div>); })()}
      </>}
      {tab === 'muster' && <>
        <div className="row" style={{ marginBottom: 12 }}><select value={site || sites[0]?.id || ''} onChange={(e) => setSite(e.target.value)} style={{ maxWidth: 320 }}>{sites.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select><span className="muted small">Accounted = latest zone is a Muster Point</span></div>
        {muster.data && <div className="grid cols-3" style={{ marginBottom: 16 }}><div className="panel stat"><span className="label">On site</span><span className="value">{muster.data.onSite}</span></div><div className="panel stat ok"><span className="label">Accounted (at muster point)</span><span className="value">{muster.data.accounted}</span></div><div className="panel stat crit"><span className="label">Unaccounted</span><span className="value">{muster.data.unaccounted}</span></div></div>}
        <div className="grid cols-2">
          <div className="panel"><h3>Unaccounted</h3><table><tbody>{muster.data?.unaccountedItems.map((i) => <tr key={i.itemId}><td>{i.person ?? i.name}<div className="muted small">{i.identifier}</div></td><td className="small muted">last seen {fmt.ago(i.lastSeenAt)}</td></tr>)}</tbody></table>{muster.data?.unaccounted === 0 && <span className="muted">Everyone accounted for</span>}</div>
          <div className="panel"><h3>Accounted</h3><table><tbody>{muster.data?.accountedItems.map((i) => <tr key={i.itemId}><td>{i.person ?? i.name}</td><td className="small muted">{fmt.ago(i.lastSeenAt)}</td></tr>)}</tbody></table></div>
        </div>
      </>}
      {tab === 'timing' && <>
        <div className="row" style={{ marginBottom: 12 }}><select value={event || events[0]?.id || ''} onChange={(e) => setEvent(e.target.value)} style={{ maxWidth: 320 }}>{events.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select><span className="muted small">Checkpoint locations under the event, in "order" attribute order · first read per checkpoint</span></div>
        <div className="panel table-wrap"><table><thead><tr><th>Rank</th><th>Bib</th><th>Athlete</th><th>Category</th>{timing.data?.checkpoints.map((c) => <th key={c}>{c}</th>)}<th>Elapsed</th></tr></thead>
          <tbody>{timing.data?.rows.map((r) => <tr key={r.itemId}><td>{r.rank || '—'}</td><td className="mono">{r.bib}</td><td>{r.athlete}</td><td>{r.category}</td>{timing.data.checkpoints.map((c) => <td key={c} className="small">{r.checkpoints[c] ? new Date(r.checkpoints[c]!).toLocaleTimeString() : '—'}</td>)}<td className="mono">{hms(r.elapsedSeconds)}</td></tr>)}</tbody></table>
          {timing.data?.rows.length === 0 && <div className="empty">No checkpoint reads yet.</div>}</div>
      </>}
    </div>
  );
}

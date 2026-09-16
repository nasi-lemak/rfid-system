import { useState } from 'react';
import { Link } from 'react-router-dom';
import { post } from '../api/client';
import { useAnomalies, useInvalidate, useReadProfile } from '../api/hooks';
import type { Anomaly } from '../api/types';
import { Badge, ErrorBox, fmt } from '../components/ui';

const kindLabel: Record<string, string> = { ReadRateSpike: 'Read-rate spike', ReadRateDrop: 'Read-rate drop', OffHoursActivity: 'Off-hours activity', UnknownTagSurge: 'Unknown-tag surge', ItemFlapping: 'Item flapping', ExcessiveMovement: 'Excessive movement' };
const kindTone = (k: string): 'ok' | 'warn' | 'crit' | 'info' | '' => k === 'OffHoursActivity' || k === 'UnknownTagSurge' ? 'crit' : k === 'ReadRateDrop' ? 'warn' : 'info';

/** Hour-of-day profile: baseline average (bars) vs today (line). Single hue plus a neutral for the baseline; hover for values. */
function Profile({ rows }: { rows: { hour: number; baseline: number; today: number }[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const w = 600, h = 140, padL = 30, padB = 18; const max = Math.max(1, ...rows.map((r) => Math.max(r.baseline, r.today)));
  const x = (i: number) => padL + (i * (w - padL - 8)) / 24; const y = (v: number) => 8 + (h - 8 - padB) * (1 - v / max); const bw = (w - padL - 8) / 24 - 3;
  const line = rows.map((r, i) => `${i === 0 ? 'M' : 'L'}${(x(i) + bw / 2).toFixed(1)},${y(r.today).toFixed(1)}`).join(' ');
  const hv = hover != null ? rows[hover] : null;
  return (
    <div style={{ position: 'relative' }}>
      <svg viewBox={`0 0 ${w} ${h}`} style={{ width: '100%', height: h }} onMouseLeave={() => setHover(null)}>
        {[0, max / 2, max].map((t) => <g key={t}><line x1={padL} x2={w - 8} y1={y(t)} y2={y(t)} stroke="var(--border)" strokeDasharray="2 3" /><text x={padL - 6} y={y(t) + 4} fontSize={10} textAnchor="end" fill="var(--muted)">{Math.round(t)}</text></g>)}
        {rows.map((r, i) => <g key={r.hour} onMouseEnter={() => setHover(i)}><rect x={x(i)} y={y(r.baseline)} width={bw} height={y(0) - y(r.baseline)} fill="var(--muted)" fillOpacity={0.35} rx={2} /><rect x={x(i)} y={0} width={bw + 3} height={h} fill="transparent" /></g>)}
        <path d={line} fill="none" stroke="var(--primary)" strokeWidth={2} strokeLinejoin="round" />
        {rows.map((r, i) => (hover === i || r.today === max) && r.today > 0 ? <circle key={'c' + i} cx={x(i) + bw / 2} cy={y(r.today)} r={hover === i ? 5 : 3.5} fill="var(--primary)" stroke="var(--panel)" strokeWidth={2} /> : null)}
        {[0, 6, 12, 18, 23].map((hh) => <text key={hh} x={x(hh) + bw / 2} y={h - 4} fontSize={10} textAnchor="middle" fill="var(--muted)">{hh}:00</text>)}
      </svg>
      <div className="row small muted" style={{ gap: 12 }}><span><span style={{ display: 'inline-block', width: 10, height: 10, background: 'var(--muted)', opacity: 0.35, borderRadius: 2, marginRight: 4 }} />baseline (avg / day, last 14 d)</span><span><span style={{ display: 'inline-block', width: 10, height: 3, background: 'var(--primary)', marginRight: 4, verticalAlign: 'middle' }} />today</span></div>
      {hv && <div style={{ position: 'absolute', top: 4, right: 8, background: 'var(--panel)', border: '1px solid var(--border)', borderRadius: 6, padding: '4px 8px', fontSize: 12, pointerEvents: 'none' }}>{hv.hour}:00 UTC · today <b>{hv.today}</b> · baseline <b>{hv.baseline}</b></div>}
    </div>
  );
}

export default function Anomalies() {
  const [status, setStatus] = useState('Open'); const { data, error } = useAnomalies(status); const inv = useInvalidate();
  const [sel, setSel] = useState<Anomaly | null>(null); const [running, setRunning] = useState(false); const [runMsg, setRunMsg] = useState<string | null>(null);
  const profile = useReadProfile(sel?.deviceId ?? undefined, sel?.deviceId ? undefined : sel?.locationId ?? undefined);
  const act = async (a: Anomaly, what: 'dismiss' | 'confirm') => { await post(`/api/anomalies/${a.id}/${what}`); inv('anomalies', 'alerts'); if (sel?.id === a.id) setSel(null); };
  const run = async () => { setRunning(true); try { const r = await post<{ created: number; updated: number }>('/api/anomalies/run'); setRunMsg(`Detector run: ${r.created} new, ${r.updated} refreshed`); inv('anomalies'); } finally { setRunning(false); } };
  return (
    <div>
      <div className="topbar"><h1>Anomalies</h1><div className="row"><select value={status} onChange={(e) => setStatus(e.target.value)} style={{ maxWidth: 140 }}><option>Open</option><option>Confirmed</option><option>Dismissed</option></select><button className="primary" disabled={running} onClick={run}>{running ? 'Running…' : 'Run detector now'}</button></div></div>
      <p className="muted small">Read patterns are compared with a 14-day baseline every 15 minutes: read-rate spikes and drops per reader (same hour of day), activity in normally quiet hours, unknown-tag surges, items flapping between two zones, and items moving far more than their type. Scores are standard deviations from the baseline; strong findings also raise a warning alert.{runMsg ? ` · ${runMsg}` : ''}</p>
      <ErrorBox error={error} />
      <div className="grid" style={{ gridTemplateColumns: sel ? '1fr 420px' : '1fr' }}>
        <div className="panel table-wrap"><table><thead><tr><th style={{ width: 150 }}>Kind</th><th style={{ minWidth: 340 }}>Finding</th><th>Score</th><th>Observed / expected</th><th>Window</th><th>Seen</th><th></th></tr></thead>
          <tbody>{data?.map((a) => <tr key={a.id} className="clickable" onClick={() => setSel(a)} style={sel?.id === a.id ? { background: 'color-mix(in srgb, var(--primary) 8%, transparent)' } : undefined}>
            <td><Badge tone={kindTone(a.kind)}>{kindLabel[a.kind] ?? a.kind}</Badge></td>
            <td>{a.message}<div className="muted small">{a.device ?? a.location ?? ''}{a.itemId ? <> · <Link to={`/items/${a.itemId}`}>{a.item}</Link></> : null}{a.alertId ? ' · alert raised' : ''}</div></td>
            <td><b>{a.score}σ</b></td><td className="small">{a.observed} / {a.expected}</td>
            <td className="small muted">{new Date(a.windowStart).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}–{new Date(a.windowEnd).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</td>
            <td className="small muted">{fmt.ago(a.lastSeenAt)}{a.occurrences > 1 ? ` · ×${a.occurrences}` : ''}</td>
            <td className="right" onClick={(e) => e.stopPropagation()}>{a.status === 'Open' && <><button className="sm" onClick={() => act(a, 'confirm')}>Confirm</button> <button className="sm" onClick={() => act(a, 'dismiss')}>Dismiss</button></>}</td>
          </tr>)}
            {data?.length === 0 && <tr><td colSpan={7} className="empty">No {status.toLowerCase()} anomalies. The detector needs a few days of reads to build baselines.</td></tr>}</tbody></table></div>
        {sel && <div className="panel"><div className="topbar"><h3 style={{ margin: 0 }}>{sel.device ?? sel.location ?? sel.item ?? 'Detail'}</h3><button className="sm" onClick={() => setSel(null)}>✕</button></div>
          <p className="small">{sel.message}</p>
          {(sel.deviceId || sel.locationId) && <><h4 style={{ margin: '8px 0 4px' }}>Read profile by hour</h4>{profile.data ? <Profile rows={profile.data} /> : <span className="muted small">Loading…</span>}</>}
          <dl className="kv" style={{ marginTop: 10 }}><dt>Detected</dt><dd>{fmt.dt(sel.detectedAt)}</dd><dt>Last seen</dt><dd>{fmt.dt(sel.lastSeenAt)} (×{sel.occurrences})</dd><dt>Score</dt><dd>{sel.score}σ · observed {sel.observed} vs expected {sel.expected}</dd>{Object.entries(sel.details ?? {}).map(([k, v]) => <><dt key={k}>{k}</dt><dd key={k + 'v'} className="small mono">{JSON.stringify(v)}</dd></>)}</dl>
        </div>}
      </div>
    </div>
  );
}

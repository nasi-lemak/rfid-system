import { useEffect, useState, type ReactNode } from 'react';

export const fmt = {
  dt: (s?: string | null) => (s ? new Date(s).toLocaleString() : '—'),
  d: (s?: string | null) => (s ? new Date(s).toLocaleDateString() : '—'),
  ago: (s?: string | null) => {
    if (!s) return 'never';
    const m = Math.round((Date.now() - new Date(s).getTime()) / 60000);
    if (m < 1) return 'just now'; if (m < 60) return `${m} min ago`; const h = Math.round(m / 60); if (h < 48) return `${h} h ago`; return `${Math.round(h / 24)} d ago`;
  },
};

export function Badge({ children, tone }: { children: ReactNode; tone?: 'ok' | 'warn' | 'crit' | 'info' | '' }) { return <span className={'badge ' + (tone ?? '')}>{children}</span>; }
export const toneForStatus = (s?: string | null): 'ok' | 'warn' | 'crit' | 'info' | '' => s === 'Active' || s === 'Ok' || s === 'Found' || s === 'Completed' || s === 'Applied' ? 'ok' : s === 'Missing' || s === 'Unexpected' || s === 'Rejected' ? 'warn' : s === 'Disposed' || s === 'Critical' ? 'crit' : s === 'Unknown' || s === 'Open' || s === 'Info' ? 'info' : '';
export const toneForSeverity = (s: string): 'ok' | 'warn' | 'crit' | 'info' => (s === 'Critical' ? 'crit' : s === 'Warning' ? 'warn' : 'info');

export function Modal({ title, onClose, children, width }: { title: string; onClose: () => void; children: ReactNode; width?: number }) {
  useEffect(() => { const h = (e: KeyboardEvent) => e.key === 'Escape' && onClose(); window.addEventListener('keydown', h); return () => window.removeEventListener('keydown', h); }, [onClose]);
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" style={width ? { width } : undefined} onClick={(e) => e.stopPropagation()}>
        <div className="topbar"><h2>{title}</h2><button onClick={onClose}>✕</button></div>
        {children}
      </div>
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) { return <div className="empty">{children}</div>; }
export function ErrorBox({ error }: { error: unknown }) { return error ? <div className="error">{(error as Error).message ?? String(error)}</div> : null; }

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return <div className="pager"><span className="muted small">{total} total</span><button className="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>‹</button><span className="small">{page} / {pages}</span><button className="sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>›</button></div>;
}

export function useDebounce<T>(value: T, ms = 300) { const [v, setV] = useState(value); useEffect(() => { const t = setTimeout(() => setV(value), ms); return () => clearTimeout(t); }, [value, ms]); return v; }

export function Bars({ rows }: { rows: { label: string; count: number }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return <div className="bars">{rows.map((r) => <div className="bar" key={r.label}><span title={r.label} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{r.label}</span><div className="track"><div className="fill" style={{ width: `${(r.count / max) * 100}%` }} /></div><span className="right">{r.count}</span></div>)}{rows.length === 0 && <span className="muted">No data</span>}</div>;
}

export function Spark({ rows }: { rows: { day: string; count: number }[] }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  const days: { day: string; count: number }[] = [];
  for (let i = 13; i >= 0; i--) { const d = new Date(); d.setUTCHours(0, 0, 0, 0); d.setUTCDate(d.getUTCDate() - i); const key = d.toISOString().slice(0, 10); days.push({ day: key, count: rows.find((r) => r.day.slice(0, 10) === key)?.count ?? 0 }); }
  return <div className="spark">{days.map((d) => <div key={d.day} title={`${d.day}: ${d.count}`} style={{ height: `${(d.count / max) * 100}%` }} />)}</div>;
}

/** Simple "state machine" visual for a lifecycle definition. */
export function LifecycleView({ lifecycle }: { lifecycle?: { initial?: string | null; states: string[]; transitions: { from: string; to: string; on: string; incrementCycle?: boolean }[] } | null }) {
  if (!lifecycle || lifecycle.states.length === 0) return <span className="muted">No lifecycle (free-form state)</span>;
  return (
    <div>
      <div className="state-machine">{lifecycle.states.map((s) => <span key={s} className={'state' + (s === lifecycle.initial ? ' initial' : '')}>{s}</span>)}</div>
      <div className="tx" style={{ marginTop: 8 }}>{lifecycle.transitions.map((t, i) => <div key={i}>{t.from} → <b>{t.to}</b> on <i>{t.on}</i>{t.incrementCycle ? ' (+cycle)' : ''}</div>)}</div>
    </div>
  );
}

/** Line/area trend over time buckets. One series, one hue; hover a point for its value. */
export function TrendChart({ buckets, height = 120, label }: { buckets: { start: string; count: number }[]; height?: number; label?: string }) {
  const [hover, setHover] = useState<number | null>(null);
  if (buckets.length === 0) return <div className="muted small">No data</div>;
  const w = 600, padL = 34, padB = 18, padT = 8; const max = Math.max(1, ...buckets.map((b) => b.count));
  const x = (i: number) => padL + (i * (w - padL - 8)) / Math.max(1, buckets.length - 1); const y = (v: number) => padT + (height - padT - padB) * (1 - v / max);
  const path = buckets.map((b, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(b.count).toFixed(1)}`).join(' ');
  const area = `${path} L${x(buckets.length - 1).toFixed(1)},${y(0)} L${x(0).toFixed(1)},${y(0)} Z`;
  const ticks = [0, max / 2, max].map((v) => Math.round(v));
  const fmtDay = (s: string) => new Date(s).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  const h = hover != null ? buckets[hover] : null;
  return (
    <div style={{ position: 'relative' }}>
      <svg viewBox={`0 0 ${w} ${height}`} style={{ width: '100%', height }} onMouseLeave={() => setHover(null)}>
        {ticks.map((t) => <g key={t}><line x1={padL} x2={w - 8} y1={y(t)} y2={y(t)} stroke="var(--border)" strokeDasharray="2 3" /><text x={padL - 6} y={y(t) + 4} fontSize={10} textAnchor="end" fill="var(--muted)">{t}</text></g>)}
        <path d={area} fill="var(--primary)" fillOpacity={0.08} />
        <path d={path} fill="none" stroke="var(--primary)" strokeWidth={2} strokeLinejoin="round" />
        {buckets.map((b, i) => <g key={b.start}>
          <rect x={x(i) - (w - padL) / buckets.length / 2} y={0} width={(w - padL) / buckets.length} height={height} fill="transparent" onMouseEnter={() => setHover(i)} />
          {(hover === i || b.count === max) && <circle cx={x(i)} cy={y(b.count)} r={hover === i ? 5 : 3.5} fill="var(--primary)" stroke="var(--panel)" strokeWidth={2} />}
        </g>)}
        {h && <line x1={x(hover!)} x2={x(hover!)} y1={padT} y2={height - padB} stroke="var(--muted)" strokeOpacity={0.5} />}
        {[0, Math.floor(buckets.length / 2), buckets.length - 1].filter((v, i, a) => a.indexOf(v) === i).map((i) => <text key={i} x={x(i)} y={height - 4} fontSize={10} textAnchor={i === 0 ? 'start' : i === buckets.length - 1 ? 'end' : 'middle'} fill="var(--muted)">{fmtDay(buckets[i].start)}</text>)}
      </svg>
      {h && <div style={{ position: 'absolute', top: 4, right: 8, background: 'var(--panel)', border: '1px solid var(--border)', borderRadius: 6, padding: '4px 8px', fontSize: 12, pointerEvents: 'none' }}><b>{h.count}</b> {label ?? ''} · {fmtDay(h.start)}</div>}
    </div>
  );
}

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

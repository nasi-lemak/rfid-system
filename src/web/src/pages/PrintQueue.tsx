import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQueryClient } from '@tanstack/react-query';
import { post } from '../api/client';
import { useDevices, usePrintJobs } from '../api/hooks';
import { Badge, fmt, toneForStatus } from '../components/ui';

export default function PrintQueue() {
  const [status, setStatus] = useState('');
  const { data } = usePrintJobs({ status, take: 300 });
  const devices = useDevices();
  const qc = useQueryClient();
  const [stock, setStock] = useState<Record<string, string>>({});
  const printers = devices.data?.filter((d) => d.kind === 'Printer') ?? [];
  const act = async (id: string, a: 'retry' | 'cancel') => { await post(`/api/labels/jobs/${id}/${a}`); qc.invalidateQueries({ queryKey: ['print-jobs'] }); };
  const restock = async (id: string) => { const n = Number(stock[id]); if (!n) return; await post(`/api/labels/printers/${id}/stock`, { count: n }); setStock({ ...stock, [id]: '' }); qc.invalidateQueries({ queryKey: ['devices'] }); };
  return (
    <div>
      <div className="topbar"><h1>Print queue & label audit</h1><div className="row"><div className="chips">{['', 'Queued', 'Printed', 'Failed', 'Cancelled'].map((s) => <span key={s} className={'chip' + (status === s ? ' active' : '')} onClick={() => setStatus(s)}>{s || 'All'}</span>)}</div><button onClick={() => post('/api/labels/jobs/process').then(() => qc.invalidateQueries({ queryKey: ['print-jobs'] }))}>Process now</button></div></div>
      <div className="grid cols-3" style={{ marginBottom: 12 }}>{printers.map((p) => { const st = Number(p.config.labelStock ?? NaN); const min = Number(p.config.labelStockMin ?? 50); return <div className="panel" key={p.id}><div className="topbar"><b>{p.name}</b><Badge tone={isNaN(st) ? '' : st === 0 ? 'crit' : st <= min ? 'warn' : 'ok'}>{isNaN(st) ? 'stock not tracked' : `${st} labels`}</Badge></div><div className="muted small">{String(p.config.host ?? '?')}:{String(p.config.port ?? 9100)} · min {min}</div><div className="row" style={{ marginTop: 8 }}><input type="number" placeholder="labels loaded" value={stock[p.id] ?? ''} onChange={(e) => setStock({ ...stock, [p.id]: e.target.value })} style={{ width: 140 }} /><button className="sm" onClick={() => restock(p.id)}>Set stock</button></div></div>; })}{printers.length === 0 && <div className="panel muted">No printer devices. Add one under Readers & devices (kind Printer, host/port).</div>}</div>
      <div className="panel table-wrap"><table><thead><tr><th>Requested</th><th>Item</th><th>EPC</th><th>Printer</th><th>Reason</th><th>Copies</th><th>Status</th><th>By</th><th></th></tr></thead>
        <tbody>{data?.map((j) => <tr key={j.id}><td className="small">{fmt.dt(j.requestedAt)}</td><td><Link to={`/items/${j.itemId}`}>{j.itemName}</Link><div className="muted small">{j.itemIdentifier}</div></td><td className="mono small">{j.epc}</td><td>{j.printer}</td><td><Badge>{j.reason}</Badge>{j.note && <div className="muted small">{j.note}</div>}</td><td>{j.copies}</td>
          <td><Badge tone={j.status === 'Printed' ? 'ok' : j.status === 'Failed' ? 'crit' : j.status === 'Queued' ? 'info' : toneForStatus(j.status)}>{j.status}</Badge>{j.error && <div className="small muted">{j.error} (attempt {j.attempts}{j.nextAttemptAt ? `, retry ${fmt.ago(j.nextAttemptAt)}` : ''})</div>}</td><td className="small">{j.requestedBy}</td>
          <td className="right">{(j.status === 'Failed' || j.status === 'Printed' || j.status === 'Cancelled') && <button className="sm" onClick={() => act(j.id, 'retry')}>{j.status === 'Printed' ? 'Reprint' : 'Retry'}</button>} {j.status === 'Queued' && <button className="sm danger" onClick={() => act(j.id, 'cancel')}>Cancel</button>}</td></tr>)}</tbody></table>
        {data?.length === 0 && <div className="empty">No print jobs.</div>}</div>
    </div>
  );
}

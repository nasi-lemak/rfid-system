import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useAlertAction, useAlerts } from '../api/hooks';
import { Badge, fmt, toneForSeverity } from '../components/ui';

export default function Alerts() {
  const [status, setStatus] = useState<string | undefined>('Open');
  const { data } = useAlerts(status);
  const act = useAlertAction();
  return (
    <div>
      <div className="topbar"><h1>Alerts</h1><div className="chips">{[['Open', 'Open'], ['Acknowledged', 'Acknowledged'], ['Closed', 'Closed'], ['', 'All']].map(([k, l]) => <span key={k} className={'chip' + ((status ?? '') === k ? ' active' : '')} onClick={() => setStatus(k || undefined)}>{l}</span>)}</div></div>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>Severity</th><th>Message</th><th>Item</th><th>Raised</th><th>Status</th><th></th></tr></thead>
          <tbody>{data?.map((a) => <tr key={a.id}>
            <td><Badge tone={toneForSeverity(a.severity)}>{a.severity}</Badge></td><td>{a.message}</td><td>{a.itemId ? <Link to={`/items/${a.itemId}`}>{a.itemName ?? a.itemId}</Link> : '—'}</td><td className="small">{fmt.dt(a.raisedAt)}</td><td><Badge>{a.status}</Badge></td>
            <td className="right">{a.status === 'Open' && <button className="sm" onClick={() => act.mutate({ id: a.id, action: 'acknowledge' })}>Acknowledge</button>} {a.status !== 'Closed' && <button className="sm" onClick={() => act.mutate({ id: a.id, action: 'close' })}>Close</button>}</td>
          </tr>)}</tbody>
        </table>
        {data?.length === 0 && <div className="empty">No alerts.</div>}
      </div>
    </div>
  );
}

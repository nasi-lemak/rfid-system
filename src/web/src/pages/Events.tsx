import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useEvents, useLocations, useLookups } from '../api/hooks';
import { Badge, fmt } from '../components/ui';

export default function Events() {
  const [type, setType] = useState('');
  const [locationId, setLocationId] = useState('');
  const { data } = useEvents({ type, locationId, take: 200 });
  const lookups = useLookups();
  const locs = useLocations();
  const ln = (id?: string | null) => locs.data?.find((l) => l.id === id)?.name ?? (id ? '…' : '—');
  return (
    <div>
      <div className="topbar"><h1>Event log</h1><div className="row"><select value={type} onChange={(e) => setType(e.target.value)}><option value="">All event types</option>{lookups.data?.eventTypes.map((t) => <option key={t}>{t}</option>)}</select><select value={locationId} onChange={(e) => setLocationId(e.target.value)}><option value="">All locations</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></div></div>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>When</th><th>Type</th><th>Item</th><th>From</th><th>To</th><th>State</th><th>Details</th></tr></thead>
          <tbody>{data?.map((e) => <tr key={e.id}><td className="small">{fmt.dt(e.occurredAt)}</td><td><Badge>{e.type}</Badge></td><td><Link to={`/items/${e.itemId}`}>{e.itemName ?? e.itemId}</Link><div className="muted small">{e.itemIdentifier}</div></td><td>{ln(e.fromLocationId)}</td><td>{ln(e.toLocationId)}</td><td>{e.toState ? <>{e.fromState ?? '—'} → {e.toState}</> : ''}</td><td className="small muted">{Object.entries(e.data ?? {}).map(([k, v]) => `${k}=${String(v)}`).join(' · ')}</td></tr>)}</tbody>
        </table>
      </div>
    </div>
  );
}

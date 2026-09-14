import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useItemTypes, useItems, useLocations } from '../api/hooks';
import { Badge, Pager, fmt, toneForStatus, useDebounce } from '../components/ui';
import { NewItemModal } from '../components/NewItemModal';

const filters = [['', 'All'], ['inCustody', 'In custody'], ['overdue', 'Overdue'], ['expiring', 'Expiring ≤30d'], ['inspectionDue', 'Inspection due'], ['notSeen7d', 'Not seen 7d'], ['lowStock', 'Low stock']];

export default function Items() {
  const [sp, setSp] = useSearchParams();
  const nav = useNavigate();
  const [q, setQ] = useState(sp.get('q') ?? '');
  const dq = useDebounce(q);
  const [page, setPage] = useState(1);
  const [showNew, setShowNew] = useState(false);
  const params = { q: dq, itemTypeId: sp.get('itemTypeId') ?? '', locationId: sp.get('locationId') ?? '', status: sp.get('status') ?? '', filter: sp.get('filter') ?? '', state: sp.get('state') ?? '', page, pageSize: 50 };
  const { data, isFetching } = useItems(params);
  const types = useItemTypes();
  const locs = useLocations();
  const set = (k: string, v: string) => { const n = new URLSearchParams(sp); v ? n.set(k, v) : n.delete(k); setSp(n); setPage(1); };
  return (
    <div>
      <div className="topbar"><h1>Items</h1><div className="row"><button className="primary" onClick={() => setShowNew(true)}>+ New item</button></div></div>
      <div className="panel" style={{ marginBottom: 12 }}>
        <div className="row">
          <input placeholder="Search name, identifier, EPC, lot…" value={q} onChange={(e) => setQ(e.target.value)} style={{ maxWidth: 320 }} />
          <select value={sp.get('itemTypeId') ?? ''} onChange={(e) => set('itemTypeId', e.target.value)} style={{ maxWidth: 200 }}><option value="">All types</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>
          <select value={sp.get('locationId') ?? ''} onChange={(e) => set('locationId', e.target.value)} style={{ maxWidth: 220 }}><option value="">All locations</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name}</option>)}</select>
          <select value={sp.get('status') ?? ''} onChange={(e) => set('status', e.target.value)} style={{ maxWidth: 140 }}><option value="">Any status</option>{['Active', 'Missing', 'Disposed', 'Retired'].map((s) => <option key={s}>{s}</option>)}</select>
          {isFetching && <span className="muted small">Loading…</span>}
        </div>
        <div className="chips" style={{ marginTop: 10 }}>{filters.map(([k, l]) => <span key={k} className={'chip' + ((sp.get('filter') ?? '') === k ? ' active' : '')} onClick={() => set('filter', k)}>{l}</span>)}</div>
      </div>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>Item</th><th>Type</th><th>State</th><th>Status</th><th>Location</th><th>Custodian</th><th>Qty</th><th>EPC</th><th>Last seen</th></tr></thead>
          <tbody>
            {data?.items.map((i) => (
              <tr key={i.id} className="clickable" onClick={() => nav(`/items/${i.id}`)}>
                <td><b>{i.name}</b><br /><span className="muted small">{i.identifier}</span></td>
                <td>{i.itemType?.name}</td>
                <td>{i.state ? <Badge>{i.state}</Badge> : <span className="muted">—</span>}</td>
                <td><Badge tone={toneForStatus(i.status)}>{i.status}</Badge></td>
                <td>{i.currentLocation?.name ?? <span className="muted">—</span>}{i.parentItem && <div className="muted small">in {i.parentItem.name}</div>}</td>
                <td>{i.custodianParty?.name ?? <span className="muted">—</span>}{i.dueBackAt && <div className={'small ' + (new Date(i.dueBackAt) < new Date() ? 'error' : 'muted')}>due {fmt.d(i.dueBackAt)}</div>}</td>
                <td>{i.itemType?.category === 'Quantity' ? `${i.quantity} ${i.unit ?? ''}` : i.cycleCount > 0 ? <span className="muted small">{i.cycleCount} cycles</span> : ''}</td>
                <td className="mono">{i.tags[0]?.epc ?? <span className="muted">untagged</span>}{i.tags.length > 1 && ` +${i.tags.length - 1}`}</td>
                <td className="small muted">{fmt.ago(i.lastSeenAt)}</td>
              </tr>
            ))}
            {data && data.items.length === 0 && <tr><td colSpan={9} className="empty">No items match. <Link to="/templates">Install a solution template</Link> or create an item.</td></tr>}
          </tbody>
        </table>
        {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />}
      </div>
      {showNew && <NewItemModal onClose={() => setShowNew(false)} />}
    </div>
  );
}

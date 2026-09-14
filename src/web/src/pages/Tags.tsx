import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useTags, useUnknownReads } from '../api/hooks';
import { Badge, Pager, fmt, useDebounce } from '../components/ui';
import { NewItemModal } from '../components/NewItemModal';

export default function Tags() {
  const [q, setQ] = useState('');
  const [page, setPage] = useState(1);
  const [unassigned, setUnassigned] = useState(false);
  const { data } = useTags({ q: useDebounce(q), page, pageSize: 50, unassigned: unassigned || '' });
  const unknown = useUnknownReads();
  const [commission, setCommission] = useState<string | null>(null);
  return (
    <div>
      <div className="topbar"><h1>Tags</h1><div className="row"><input placeholder="Search EPC…" value={q} onChange={(e) => setQ(e.target.value)} style={{ width: 240 }} /><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={unassigned} onChange={(e) => setUnassigned(e.target.checked)} /> Unassigned only</label></div></div>
      <div className="grid" style={{ gridTemplateColumns: '2fr 1fr' }}>
        <div className="panel table-wrap">
          <table><thead><tr><th>EPC</th><th>TID</th><th>Technology</th><th>Status</th><th>Item</th><th>Encoded</th></tr></thead>
            <tbody>{data?.items.map((t) => <tr key={t.id}><td className="mono">{t.epc}</td><td className="mono small">{t.tid ?? ''}</td><td>{t.technology}</td><td><Badge tone={t.status === 'Active' ? 'ok' : t.status === 'Retired' ? '' : 'info'}>{t.status}</Badge></td><td>{t.itemId ? <Link to={`/items/${t.itemId}`}>{t.itemName}</Link> : <button className="sm" onClick={() => setCommission(t.epc)}>Commission</button>}</td><td className="small muted">{fmt.dt(t.encodedAt)}</td></tr>)}</tbody></table>
          {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />}
        </div>
        <div className="panel">
          <h3>Unknown tags seen by readers</h3>
          <p className="muted small">EPCs read by portals/handhelds that are not commissioned yet. Click to create an item for one.</p>
          {unknown.data?.map((u) => <div key={u.epc} className="row" style={{ justifyContent: 'space-between', padding: '4px 0', borderBottom: '1px solid var(--border)' }}><span className="mono small">{u.epc}</span><span className="muted small">{u.count}× · {fmt.ago(u.lastSeen)}</span><button className="sm" onClick={() => setCommission(u.epc)}>Commission</button></div>)}
          {unknown.data?.length === 0 && <span className="muted">None</span>}
        </div>
      </div>
      {commission && <NewItemModal epc={commission} onClose={() => setCommission(null)} />}
    </div>
  );
}

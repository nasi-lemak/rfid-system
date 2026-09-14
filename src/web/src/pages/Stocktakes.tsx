import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useCreateStocktake, useItemTypes, useLocations, useStocktakes } from '../api/hooks';
import { Badge, ErrorBox, Modal, fmt, toneForStatus } from '../components/ui';

export default function Stocktakes() {
  const { data } = useStocktakes();
  const locs = useLocations();
  const types = useItemTypes();
  const create = useCreateStocktake();
  const nav = useNavigate();
  const [show, setShow] = useState(false);
  const [f, setF] = useState({ name: '', locationId: '', itemTypeId: '' });
  const submit = async (e: FormEvent) => { e.preventDefault(); const s = await create.mutateAsync({ name: f.name || `Stocktake ${new Date().toLocaleDateString()}`, locationId: f.locationId, itemTypeId: f.itemTypeId || undefined }); setShow(false); nav(`/stocktakes/${s.id}`); };
  return (
    <div>
      <div className="topbar"><h1>Stocktakes</h1><button className="primary" onClick={() => setShow(true)}>+ New stocktake</button></div>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>Name</th><th>Location</th><th>Status</th><th>Expected</th><th>Found</th><th>Missing</th><th>Unexpected</th><th>Started</th></tr></thead>
          <tbody>{data?.map((s) => <tr key={s.summary.id} className="clickable" onClick={() => nav(`/stocktakes/${s.summary.id}`)}>
            <td><b>{s.summary.name}</b></td><td>{s.location}</td><td><Badge tone={toneForStatus(s.summary.status)}>{s.summary.status}</Badge></td><td>{s.summary.expected}</td><td className="ok">{s.summary.found}</td><td>{s.summary.missing > 0 ? <Badge tone="crit">{s.summary.missing}</Badge> : 0}</td><td>{s.summary.unexpected > 0 ? <Badge tone="warn">{s.summary.unexpected}</Badge> : 0}</td><td className="small">{fmt.dt(s.startedAt)}</td>
          </tr>)}</tbody>
        </table>
        {data?.length === 0 && <div className="empty">No stocktakes yet.</div>}
      </div>
      {show && <Modal title="New stocktake" onClose={() => setShow(false)}>
        <form className="form" onSubmit={submit}>
          <label>Name<input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder="e.g. Ward 3 weekly count" /></label>
          <label>Location (includes sub-locations) *<select required value={f.locationId} onChange={(e) => setF({ ...f, locationId: e.target.value })}><option value="">—</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name} · {l.itemCount ?? 0} items</option>)}</select></label>
          <label>Item type (optional)<select value={f.itemTypeId} onChange={(e) => setF({ ...f, itemTypeId: e.target.value })}><option value="">All types</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
          <ErrorBox error={create.error} />
          <div className="row end"><button className="primary" disabled={create.isPending}>Start</button></div>
        </form>
      </Modal>}
    </div>
  );
}

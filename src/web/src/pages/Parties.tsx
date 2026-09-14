import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useLookups, useParties, useRemove, useSave } from '../api/hooks';
import type { Party } from '../api/types';
import { Badge, ErrorBox, Modal, useDebounce } from '../components/ui';

export default function Parties() {
  const [q, setQ] = useState('');
  const { data } = useParties(useDebounce(q));
  const lookups = useLookups();
  const save = useSave<Party>('/api/parties', ['parties']);
  const remove = useRemove('/api/parties', ['parties']);
  const [edit, setEdit] = useState<Partial<Party> | null>(null);
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: { ...edit, attributes: edit.attributes ?? {} } }); setEdit(null); };
  return (
    <div>
      <div className="topbar"><h1>Parties</h1><div className="row"><input placeholder="Search…" value={q} onChange={(e) => setQ(e.target.value)} style={{ width: 220 }} /><button className="primary" onClick={() => setEdit({ kind: 'Employee', name: '' })}>+ New party</button></div></div>
      <p className="muted small">People, employees, customers, departments, suppliers and vehicles — anything that can hold custody of an item.</p>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>Name</th><th>Kind</th><th>Code</th><th>Email</th><th>Items in custody</th><th></th></tr></thead>
          <tbody>{data?.map((p) => <tr key={p.id}><td><b>{p.name}</b></td><td><Badge>{p.kind}</Badge></td><td>{p.code}</td><td>{p.email}</td><td><Link to={`/items?custodianPartyId=${p.id}`}>{p.itemsInCustody ?? 0}</Link></td><td className="right"><button className="sm" onClick={() => setEdit(p)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete?') && remove.mutate(p.id)}>Delete</button></td></tr>)}</tbody>
        </table>
      </div>
      {edit && <Modal title={edit.id ? 'Edit party' : 'New party'} onClose={() => setEdit(null)}>
        <form className="form cols-2" onSubmit={submit}>
          <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
          <label>Kind<select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value })}>{lookups.data?.partyKinds.map((k) => <option key={k}>{k}</option>)}</select></label>
          <label>Code<input value={edit.code ?? ''} onChange={(e) => setEdit({ ...edit, code: e.target.value })} /></label>
          <label>Email<input value={edit.email ?? ''} onChange={(e) => setEdit({ ...edit, email: e.target.value })} /></label>
          <label>External ref (HR / ERP id)<input value={edit.externalRef ?? ''} onChange={(e) => setEdit({ ...edit, externalRef: e.target.value })} /></label>
          <div style={{ gridColumn: '1 / -1' }}><ErrorBox error={save.error} /></div>
          <div className="row end" style={{ gridColumn: '1 / -1' }}><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

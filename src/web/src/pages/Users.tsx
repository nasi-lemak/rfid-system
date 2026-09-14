import { useState, type FormEvent } from 'react';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api/client';
import { useSave } from '../api/hooks';
import { Badge, ErrorBox, Modal } from '../components/ui';

interface User { id: string; email: string; displayName: string; role: string; isActive: boolean }

export default function Users() {
  const { data } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/api/users') });
  const save = useSave<User>('/api/users', ['users']);
  const [edit, setEdit] = useState<(Partial<User> & { password?: string }) | null>(null);
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: edit }); setEdit(null); };
  return (
    <div>
      <div className="topbar"><h1>Users</h1><button className="primary" onClick={() => setEdit({ role: 'Operator', isActive: true })}>+ New user</button></div>
      <div className="panel table-wrap"><table><thead><tr><th>Email</th><th>Name</th><th>Role</th><th>Active</th><th></th></tr></thead>
        <tbody>{data?.map((u) => <tr key={u.id}><td>{u.email}</td><td>{u.displayName}</td><td><Badge>{u.role}</Badge></td><td>{u.isActive ? '✓' : '—'}</td><td className="right"><button className="sm" onClick={() => setEdit(u)}>Edit</button></td></tr>)}</tbody></table></div>
      {edit && <Modal title={edit.id ? 'Edit user' : 'New user'} onClose={() => setEdit(null)}>
        <form className="form" onSubmit={submit}>
          <label>Email *<input required type="email" value={edit.email ?? ''} onChange={(e) => setEdit({ ...edit, email: e.target.value })} /></label>
          <label>Display name *<input required value={edit.displayName ?? ''} onChange={(e) => setEdit({ ...edit, displayName: e.target.value })} /></label>
          <label>Role<select value={edit.role} onChange={(e) => setEdit({ ...edit, role: e.target.value })}><option>Admin</option><option>Operator</option><option>Viewer</option></select></label>
          <label>{edit.id ? 'New password (leave blank to keep)' : 'Password *'}<input type="password" required={!edit.id} value={edit.password ?? ''} onChange={(e) => setEdit({ ...edit, password: e.target.value })} /></label>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          <ErrorBox error={save.error} />
          <div className="row end"><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

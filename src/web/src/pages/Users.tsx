import { useState, type FormEvent } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { get, put } from '../api/client';
import { useInvalidate, useLocations, useSave, useUserSites } from '../api/hooks';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

interface User { id: string; email: string; displayName: string; role: string; isActive: boolean; restrictToSites?: boolean; externalIssuer?: string | null; lastLoginAt?: string | null; siteCount?: number }

function SiteAccessEditor({ user, onClose }: { user: User; onClose: () => void }) {
  const current = useUserSites(user.id); const locs = useLocations(); const inv = useInvalidate();
  const sites = locs.data?.filter((l) => l.kind === 'Site') ?? [];
  const [restrict, setRestrict] = useState<boolean | null>(null);
  const [roles, setRoles] = useState<Record<string, string> | null>(null);
  const eff = roles ?? Object.fromEntries((current.data?.sites ?? []).map((s) => [s.siteLocationId, s.role]));
  const effRestrict = restrict ?? current.data?.restrictToSites ?? false;
  const save = useMutation({ mutationFn: () => put(`/api/users/${user.id}/sites`, { restrictToSites: effRestrict, sites: Object.entries(eff).filter(([, r]) => r).map(([siteLocationId, role]) => ({ siteLocationId, role })) }), onSuccess: () => { inv('users', 'user-sites'); onClose(); } });
  return (
    <Modal title={`Site access – ${user.displayName}`} onClose={onClose} width={560}>
      <div className="form">
        <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={effRestrict} onChange={(e) => setRestrict(e.target.checked)} /> Restrict this user to the sites below</label>
        <p className="muted small">Restricted users only see items, locations and readers under their sites and can only run operations and stocktakes there, with the role given per site (their global role <Badge>{user.role}</Badge> still applies to locations that belong to no site). Changes take effect at the user's next sign-in.</p>
        <table><thead><tr><th>Site</th><th>Role</th></tr></thead>
          <tbody>{sites.map((s) => <tr key={s.id}><td>{s.name}<div className="muted small">{s.code}</div></td><td><select value={eff[s.id] ?? ''} onChange={(e) => setRoles({ ...eff, [s.id]: e.target.value })} disabled={!effRestrict}><option value="">No access</option><option>Viewer</option><option>Operator</option><option>Admin</option></select></td></tr>)}
            {sites.length === 0 && <tr><td colSpan={2} className="muted">No locations of kind Site yet.</td></tr>}</tbody></table>
        <ErrorBox error={save.error} />
        <div className="row end"><button type="button" onClick={onClose}>Cancel</button><button className="primary" disabled={save.isPending} onClick={() => save.mutate()}>Save</button></div>
      </div>
    </Modal>
  );
}

export default function Users() {
  const { data } = useQuery({ queryKey: ['users'], queryFn: () => get<User[]>('/api/users') });
  const save = useSave<User>('/api/users', ['users']);
  const [edit, setEdit] = useState<(Partial<User> & { password?: string }) | null>(null);
  const [sitesFor, setSitesFor] = useState<User | null>(null);
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: edit }); setEdit(null); };
  return (
    <div>
      <div className="topbar"><h1>Users</h1><button className="primary" onClick={() => setEdit({ role: 'Operator', isActive: true })}>+ New user</button></div>
      <div className="panel table-wrap"><table><thead><tr><th>Email</th><th>Name</th><th>Role</th><th>Sites</th><th>Sign-in</th><th>Active</th><th></th></tr></thead>
        <tbody>{data?.map((u) => <tr key={u.id}><td>{u.email}</td><td>{u.displayName}</td><td><Badge>{u.role}</Badge></td><td>{u.restrictToSites ? <Badge tone="info">{u.siteCount ?? 0} site(s)</Badge> : <span className="muted small">all</span>}</td><td className="small muted">{u.externalIssuer ? 'SSO' : 'password'}{u.lastLoginAt ? ` · ${fmt.ago(u.lastLoginAt)}` : ''}</td><td>{u.isActive ? '✓' : '—'}</td><td className="right"><button className="sm" onClick={() => setSitesFor(u)}>Sites</button> <button className="sm" onClick={() => setEdit(u)}>Edit</button></td></tr>)}</tbody></table></div>
      {sitesFor && <SiteAccessEditor user={sitesFor} onClose={() => setSitesFor(null)} />}
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

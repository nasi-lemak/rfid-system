import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth';
import { useLive } from '../live';
import { useAlerts } from '../api/hooks';

const groups: { title: string; links: { to: string; label: string; admin?: boolean }[] }[] = [
  { title: 'Overview', links: [{ to: '/', label: 'Dashboard' }, { to: '/live', label: 'Live reads' }, { to: '/alerts', label: 'Alerts' }, { to: '/presence', label: 'Presence & location' }, { to: '/events', label: 'Event log' }, { to: '/reports', label: 'Reports' }] },
  { title: 'Track', links: [{ to: '/items', label: 'Items' }, { to: '/tags', label: 'Tags' }, { to: '/operations', label: 'Operations' }, { to: '/stocktakes', label: 'Stocktakes' }, { to: '/print-queue', label: 'Print queue' }] },
  { title: 'Configure', links: [{ to: '/item-types', label: 'Item types' }, { to: '/locations', label: 'Locations' }, { to: '/parties', label: 'Parties' }, { to: '/devices', label: 'Readers & devices' }, { to: '/rules', label: 'Rules' }, { to: '/labels', label: 'Label designer' }, { to: '/templates', label: 'Solution templates' }, { to: '/integrations', label: 'Integrations', admin: true }, { to: '/import', label: 'ERP import', admin: true }, { to: '/users', label: 'Users', admin: true }, { to: '/cluster', label: 'Cluster & system', admin: true }] },
];

export default function Layout() {
  const { user, logout } = useAuth();
  const { connected } = useLive();
  const alerts = useAlerts('Open');
  const open = alerts.data?.length ?? 0;
  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand"><span className={'dot' + (connected ? '' : ' off')} title={connected ? 'Live connection up' : 'Live connection down'} />RFID Platform</div>
        {groups.map((g) => (
          <div key={g.title}>
            <div className="nav-group">{g.title}</div>
            {g.links.filter((l) => !l.admin || user?.role === 'Admin').map((l) => (
              <NavLink key={l.to} to={l.to} end={l.to === '/'}>
                {l.label}
                {l.to === '/alerts' && open > 0 && <span className="badge crit">{open}</span>}
              </NavLink>
            ))}
          </div>
        ))}
        <div className="spacer" />
        <div className="user">{user?.displayName}<br /><span className="muted">{user?.role}{user?.restrictToSites ? ` · ${user.sites?.length ?? 0} site(s)` : ''} · {user?.tenantName ?? 'tenant'}{user?.sso ? ' · SSO' : ''}</span></div>
        <button onClick={logout}>Sign out</button>
      </aside>
      <main className="main"><Outlet /></main>
    </div>
  );
}

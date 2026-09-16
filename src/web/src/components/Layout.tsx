import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth';
import { useLive } from '../live';
import { useAlerts } from '../api/hooks';
import { LANGS, useI18n } from '../i18n';

const groups: { title: string; links: { to: string; label: string; admin?: boolean }[] }[] = [
  { title: 'Overview', links: [{ to: '/', label: 'Dashboard' }, { to: '/live', label: 'Live reads' }, { to: '/alerts', label: 'Alerts' }, { to: '/presence', label: 'Presence & location' }, { to: '/map', label: 'Map & geofences' }, { to: '/events', label: 'Event log' }, { to: '/reports', label: 'Reports' }, { to: '/analytics', label: 'Analytics' }, { to: '/anomalies', label: 'Anomalies' }] },
  { title: 'Track', links: [{ to: '/items', label: 'Items' }, { to: '/tags', label: 'Tags' }, { to: '/operations', label: 'Operations' }, { to: '/stocktakes', label: 'Stocktakes' }, { to: '/print-queue', label: 'Print queue' }, { to: '/encoding', label: 'Encoding' }, { to: '/billing', label: 'Billing' }, { to: '/maintenance', label: 'Maintenance' }] },
  { title: 'Configure', links: [{ to: '/item-types', label: 'Item types' }, { to: '/locations', label: 'Locations' }, { to: '/parties', label: 'Parties' }, { to: '/devices', label: 'Readers & devices' }, { to: '/rules', label: 'Rules' }, { to: '/workflows', label: 'Workflows' }, { to: '/notifications', label: 'Notifications', admin: true }, { to: '/labels', label: 'Label designer' }, { to: '/templates', label: 'Solution templates' }, { to: '/integrations', label: 'Integrations', admin: true }, { to: '/import', label: 'ERP import', admin: true }, { to: '/users', label: 'Users', admin: true }, { to: '/cluster', label: 'Cluster & system', admin: true }, { to: '/audit', label: 'Audit & retention', admin: true }, { to: '/epcis', label: 'EPCIS 2.0', admin: true }] },
];

export default function Layout() {
  const { user, logout } = useAuth();
  const { connected } = useLive();
  const { t, lang, setLang } = useI18n();
  const alerts = useAlerts('Open');
  const open = alerts.data?.length ?? 0;
  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand"><span className={'dot' + (connected ? '' : ' off')} title={connected ? t('Live connection up') : t('Live connection down')} />RFID Platform</div>
        {groups.map((g) => (
          <div key={g.title}>
            <div className="nav-group">{t(g.title)}</div>
            {g.links.filter((l) => !l.admin || user?.role === 'Admin').map((l) => (
              <NavLink key={l.to} to={l.to} end={l.to === '/'}>
                {t(l.label)}
                {l.to === '/alerts' && open > 0 && <span className="badge crit">{open}</span>}
              </NavLink>
            ))}
          </div>
        ))}
        <div className="spacer" />
        <div className="user">{user?.displayName}<br /><span className="muted">{user?.role}{user?.restrictToSites ? ` · ${user.sites?.length ?? 0} site(s)` : ''} · {user?.tenantName ?? 'tenant'}{user?.sso ? ' · SSO' : ''}</span></div>
        <select value={lang} onChange={(e) => setLang(e.target.value as typeof lang)} title={t('Language')} style={{ marginBottom: 8, background: 'var(--sidebar-active)', color: 'var(--sidebar-ink)', border: '1px solid var(--sidebar-active)' }}>{LANGS.map((l) => <option key={l.code} value={l.code}>{l.label}</option>)}</select>
        <button onClick={logout}>{t('Sign out')}</button>
      </aside>
      <main className="main"><Outlet /></main>
    </div>
  );
}

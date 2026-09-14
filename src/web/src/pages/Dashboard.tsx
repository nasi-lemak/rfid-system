import { Link } from 'react-router-dom';
import { useAlerts, useDashboard, useEvents } from '../api/hooks';
import { Badge, Bars, Spark, fmt, toneForSeverity } from '../components/ui';

export default function Dashboard() {
  const { data, error } = useDashboard();
  const alerts = useAlerts('Open');
  const events = useEvents({ take: 12 });
  const t = data?.totals ?? {};
  const stat = (label: string, key: string, tone?: string, to?: string) => (
    <div className={'panel stat ' + (tone && (t[key] ?? 0) > 0 ? tone : '')}><span className="label">{label}</span><span className="value">{to ? <Link to={to} style={{ color: 'inherit' }}>{t[key] ?? '–'}</Link> : t[key] ?? '–'}</span></div>
  );
  return (
    <div>
      <div className="topbar"><h1>Dashboard</h1><span className="muted small">Auto-refreshing</span></div>
      {error && <div className="error">{(error as Error).message}</div>}
      <div className="grid cols-4" style={{ marginBottom: 16 }}>
        {stat('Tracked items', 'items', '', '/items')}
        {stat('Active tags', 'tags', '', '/tags')}
        {stat('Open alerts', 'openAlerts', 'warn', '/alerts')}
        {stat('Critical alerts', 'criticalAlerts', 'crit', '/alerts')}
        {stat('In custody', 'inCustody', '', '/items?filter=inCustody')}
        {stat('Overdue returns', 'overdue', 'warn', '/items?filter=overdue')}
        {stat('Missing', 'missing', 'crit', '/items?status=Missing')}
        {stat('Not seen 7 days', 'notSeen7d', 'warn', '/items?filter=notSeen7d')}
        {stat('Expiring ≤30d', 'expiring30d', 'warn', '/items?filter=expiring')}
        {stat('Inspection due ≤14d', 'inspectionDue14d', 'warn', '/items?filter=inspectionDue')}
        {stat('Reads today', 'readsToday', '', '/live')}
        {stat('Operations today', 'operationsToday', '', '/operations')}
      </div>
      <div className="grid cols-3">
        <div className="panel"><h3>Items by type</h3><Bars rows={(data?.byType ?? []).map((r) => ({ label: r.type, count: r.count }))} /></div>
        <div className="panel"><h3>Items by state</h3><Bars rows={(data?.byState ?? []).map((r) => ({ label: r.state, count: r.count }))} /></div>
        <div className="panel"><h3>Items by location</h3><Bars rows={(data?.byLocation ?? []).map((r) => ({ label: r.location, count: r.count }))} /></div>
        <div className="panel"><h3>Tag reads · last 14 days</h3><Spark rows={data?.readsPerDay ?? []} /></div>
        <div className="panel"><h3>Operations · last 14 days</h3><Spark rows={data?.opsPerDay ?? []} /></div>
        <div className="panel"><h3>Item status</h3><Bars rows={(data?.byStatus ?? []).map((r) => ({ label: r.status, count: r.count }))} /></div>
      </div>
      <div className="grid cols-2" style={{ marginTop: 16 }}>
        <div className="panel">
          <div className="topbar"><h3>Open alerts</h3><Link to="/alerts" className="small">All alerts →</Link></div>
          {(alerts.data ?? []).slice(0, 8).map((a) => <div key={a.id} className="row" style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><Badge tone={toneForSeverity(a.severity)}>{a.severity}</Badge><span style={{ flex: 1 }}>{a.message}</span><span className="muted small">{fmt.ago(a.raisedAt)}</span></div>)}
          {alerts.data?.length === 0 && <div className="muted">No open alerts</div>}
        </div>
        <div className="panel">
          <div className="topbar"><h3>Recent activity</h3><Link to="/events" className="small">Event log →</Link></div>
          <ul className="timeline">{(events.data ?? []).map((e) => <li key={e.id}><span className="when">{fmt.ago(e.occurredAt)}</span><span><Badge>{e.type}</Badge> <Link to={`/items/${e.itemId}`}>{e.itemName ?? e.itemId}</Link>{e.toState ? <span className="muted"> → {e.toState}</span> : null}</span></li>)}</ul>
        </div>
      </div>
    </div>
  );
}

import { useMutation } from '@tanstack/react-query';
import { post } from '../api/client';
import { useCluster, useInvalidate } from '../api/hooks';
import { Badge, fmt } from '../components/ui';

export default function Cluster() {
  const { data, error } = useCluster();
  const inv = useInvalidate();
  const release = useMutation({ mutationFn: (name: string) => post(`/api/cluster/leases/${encodeURIComponent(name)}/release`), onSuccess: () => inv('cluster') });
  const up = (s: number) => s < 3600 ? `${Math.floor(s / 60)} min` : s < 86400 ? `${(s / 3600).toFixed(1)} h` : `${(s / 86400).toFixed(1)} d`;
  return (
    <div>
      <div className="topbar"><h1>Cluster & system</h1><span className="muted small">Any number of API nodes can run against the same database; background jobs and reader connections are coordinated with leases</span></div>
      {error && <div className="error">{(error as Error).message}</div>}
      {data && <>
        <div className="grid cols-4" style={{ marginBottom: 16 }}>
          <div className="panel stat"><span className="label">This node</span><span className="value" style={{ fontSize: 18 }}>{data.node.id}</span><span className="muted small">pid {data.node.pid} · up {up(data.node.uptimeSeconds)}</span></div>
          <div className="panel stat ok"><span className="label">Active nodes</span><span className="value">{data.nodes.length}</span></div>
          <div className="panel stat"><span className="label">Leases held here</span><span className="value">{data.leases.filter((l) => l.active && l.heldByThisNode).length}<span className="muted small"> / {data.leases.filter((l) => l.active).length}</span></span></div>
          <div className="panel stat"><span className="label">Scale-out features</span><span className="row" style={{ flexWrap: 'wrap', gap: 4 }}>
            <Badge tone={data.features.redisBackplane ? 'ok' : ''}>Redis backplane {data.features.redisBackplane ? 'on' : 'off'}</Badge>
            <Badge tone={data.features.sso ? 'ok' : ''}>SSO {data.features.sso ? 'on' : 'off'}</Badge>
            <Badge tone={data.features.mqtt ? 'ok' : ''}>MQTT {data.features.mqtt ? 'on' : 'off'}</Badge>
            <Badge tone={data.features.llrp ? 'ok' : ''}>LLRP {data.features.llrp ? 'on' : 'off'}</Badge></span></div>
        </div>
        <div className="grid cols-2">
          <div className="panel"><h3>Nodes</h3><table><thead><tr><th>Node</th><th>Leases</th><th>Since</th></tr></thead>
            <tbody>{data.nodes.map((n) => <tr key={n.id}><td className="mono">{n.id} {n.isThisNode && <Badge tone="info">this node</Badge>}</td><td>{n.leases}</td><td className="small muted">{fmt.ago(n.since)}</td></tr>)}</tbody></table>
            <p className="muted small" style={{ marginTop: 12 }}>Node ids are <code>machine-xxxxxx</code>; a node that stops renewing its leases is replaced by another within one job interval. Live updates fan out across nodes when <code>Redis:ConnectionString</code> is set; position tracks are stored on the item row so any node can continue them; position history is kept for {data.features.positionRetentionDays} days.</p></div>
          <div className="panel table-wrap"><h3>Leases</h3><table><thead><tr><th>Lease</th><th>Owner</th><th>Expires</th><th></th></tr></thead>
            <tbody>{data.leases.map((l) => <tr key={l.name}><td className="mono">{l.name}</td><td className="mono small">{l.owner}{l.heldByThisNode && ' ★'}</td><td className="small">{l.active ? <Badge tone="ok">{fmt.ago(l.expiresAt)}</Badge> : <Badge>expired</Badge>}</td><td className="right">{l.active && <button className="sm" onClick={() => release.mutate(l.name)}>Release</button>}</td></tr>)}
              {data.leases.length === 0 && <tr><td colSpan={4} className="muted">No leases yet – background jobs acquire them a few seconds after start-up.</td></tr>}</tbody></table></div>
        </div>
      </>}
    </div>
  );
}

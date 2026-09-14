import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useStocktake, useStocktakeAction, useStocktakeScan } from '../api/hooks';
import { Badge, ErrorBox, fmt, toneForStatus } from '../components/ui';

export default function StocktakeDetail() {
  const { id = '' } = useParams();
  const { data } = useStocktake(id);
  const act = useStocktakeAction(id);
  const scan = useStocktakeScan(id);
  const [epcs, setEpcs] = useState('');
  const [filter, setFilter] = useState('');
  if (!data) return <div className="muted">Loading…</div>;
  const s = data.summary;
  const pct = s.expected ? Math.round((s.found / s.expected) * 100) : 0;
  const lines = data.lines.filter((l) => !filter || l.result === filter || (filter === 'Pending' && l.result === 'Pending'));
  return (
    <div>
      <div className="topbar">
        <div><h1 style={{ marginBottom: 2 }}>{s.name}</h1><span className="muted">{data.location} · started {fmt.dt(data.startedAt)}</span></div>
        <div className="row">
          <Badge tone={toneForStatus(s.status)}>{s.status}</Badge>
          {s.status === 'Open' && <><button onClick={() => act.mutate('cancel')}>Cancel</button><button className="primary" onClick={() => act.mutate('reconcile')}>Reconcile</button></>}
          {s.status === 'Reconciled' && <button className="primary" onClick={() => act.mutate('apply')} title="Flag missing items and move unexpected items into this location">Apply result</button>}
        </div>
      </div>
      <div className="grid cols-4" style={{ marginBottom: 16 }}>
        <div className="panel stat"><span className="label">Expected</span><span className="value">{s.expected}</span></div>
        <div className="panel stat ok"><span className="label">Found ({pct}%)</span><span className="value">{s.found}</span></div>
        <div className="panel stat crit"><span className="label">{s.status === 'Open' ? 'Not yet seen' : 'Missing'}</span><span className="value">{s.missing}</span></div>
        <div className="panel stat warn"><span className="label">Unexpected / unknown</span><span className="value">{s.unexpected} / {s.unknown}</span></div>
      </div>
      <ErrorBox error={act.error ?? scan.error} />
      <div className="grid" style={{ gridTemplateColumns: '1fr 320px' }}>
        <div className="panel table-wrap">
          <div className="chips" style={{ marginBottom: 10 }}>{['', 'Found', 'Pending', 'Missing', 'Unexpected', 'Unknown'].map((f) => <span key={f} className={'chip' + (filter === f ? ' active' : '')} onClick={() => setFilter(f)}>{f || 'All'}</span>)}</div>
          <table>
            <thead><tr><th>Result</th><th>Item</th><th>Type</th><th>Expected at</th><th>EPC</th><th>Seen</th></tr></thead>
            <tbody>{lines.map((l) => <tr key={l.id}><td><Badge tone={toneForStatus(l.result === 'Pending' ? 'Info' : l.result)}>{l.result}</Badge></td><td>{l.itemId ? <Link to={`/items/${l.itemId}`}>{l.itemName}</Link> : <span className="muted">unknown tag</span>}<div className="muted small">{l.itemIdentifier}</div></td><td>{l.itemType}</td><td>{l.expectedLocation}</td><td className="mono">{l.epc}</td><td className="small muted">{l.foundAt ? fmt.ago(l.foundAt) : ''}</td></tr>)}</tbody>
          </table>
        </div>
        <div className="panel">
          <h3>Add scans</h3>
          <p className="muted small">Normally the handheld app posts scans here in real time. You can also paste EPCs manually.</p>
          <textarea className="mono" rows={8} value={epcs} onChange={(e) => setEpcs(e.target.value)} disabled={s.status !== 'Open'} />
          <div className="row end" style={{ marginTop: 8 }}><button className="primary" disabled={s.status !== 'Open' || !epcs.trim()} onClick={async () => { await scan.mutateAsync(epcs.split(/[\s,;]+/).filter(Boolean)); setEpcs(''); }}>Submit scans</button></div>
        </div>
      </div>
    </div>
  );
}

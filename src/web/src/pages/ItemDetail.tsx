import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useItem, useItemEvents, useLocations, useParties } from '../api/hooks';
import { post } from '../api/client';
import { Badge, ErrorBox, LifecycleView, Modal, fmt, toneForStatus } from '../components/ui';
import { OperationForm } from '../components/OperationForm';

export default function ItemDetail() {
  const { id } = useParams();
  const { data, refetch, error } = useItem(id);
  const events = useItemEvents(id);
  const locs = useLocations();
  const parties = useParties();
  const [op, setOp] = useState<string | null>(null);
  const [bindEpc, setBindEpc] = useState<string | null>(null);
  const [bindErr, setBindErr] = useState<unknown>(null);
  if (error) return <ErrorBox error={error} />;
  if (!data) return <div className="muted">Loading…</div>;
  const i = data.item;
  const locName = (lid?: string | null) => locs.data?.find((l) => l.id === lid)?.name ?? (lid ? '…' : '—');
  const partyName = (pid?: string | null) => parties.data?.find((p) => p.id === pid)?.name ?? (pid ? '…' : '—');
  const lifecycle = i.itemType?.lifecycle;
  const allowedOps = lifecycle?.transitions.filter((t) => t.from === '*' || t.from.toLowerCase() === (i.state ?? '').toLowerCase()).map((t) => t.on) ?? [];
  const quick = ['Transfer', 'Issue', 'Return', 'Count', 'Inspect', 'Maintain', 'ProcessStage', 'Dispatch', 'Pack', 'Unpack', 'Adjust', 'Dispose'];
  const bind = async () => { setBindErr(null); try { await post(`/api/items/${id}/tags`, { epc: bindEpc }); setBindEpc(null); refetch(); } catch (e) { setBindErr(e); } };
  return (
    <div>
      <div className="topbar">
        <div><h1 style={{ marginBottom: 2 }}>{i.name}</h1><span className="muted">{i.itemType?.name} · {i.identifier}</span></div>
        <div className="row">{quick.map((q) => <button key={q} className="sm" style={allowedOps.includes(q) ? { borderColor: 'var(--primary)', color: 'var(--primary)' } : undefined} onClick={() => setOp(q)}>{q}</button>)}</div>
      </div>
      <div className="grid cols-3">
        <div className="panel">
          <h3>Status</h3>
          <dl className="kv">
            <dt>State</dt><dd>{i.state ? <Badge>{i.state}</Badge> : '—'}</dd>
            <dt>Status</dt><dd><Badge tone={toneForStatus(i.status)}>{i.status}</Badge></dd>
            <dt>Location</dt><dd>{i.currentLocation ? <Link to={`/items?locationId=${i.currentLocation.id}`}>{i.currentLocation.name}</Link> : '—'}</dd>
            <dt>Custodian</dt><dd>{i.custodianParty?.name ?? '—'}</dd>
            {i.dueBackAt && <><dt>Due back</dt><dd className={new Date(i.dueBackAt) < new Date() ? 'error' : ''}>{fmt.dt(i.dueBackAt)}</dd></>}
            <dt>Container</dt><dd>{i.parentItem ? <Link to={`/items/${i.parentItem.id}`}>{i.parentItem.name}</Link> : '—'}</dd>
            <dt>Last seen</dt><dd>{fmt.ago(i.lastSeenAt)} · {locName(i.lastSeenLocationId)}</dd>
            {i.itemType?.category === 'Quantity' && <><dt>Quantity</dt><dd>{i.quantity} {i.unit}</dd><dt>Lot</dt><dd>{i.lotNumber ?? '—'}</dd></>}
            {i.expiryDate && <><dt>Expiry</dt><dd>{fmt.d(i.expiryDate)}</dd></>}
            {(i.cycleCount > 0 || i.itemType?.maxCycles) && <><dt>Cycles</dt><dd>{i.cycleCount}{i.itemType?.maxCycles ? ` / ${i.itemType.maxCycles}` : ''}</dd></>}
            {i.nextInspectionDue && <><dt>Next inspection</dt><dd className={new Date(i.nextInspectionDue) < new Date() ? 'error' : ''}>{fmt.d(i.nextInspectionDue)}</dd></>}
            {i.cost != null && <><dt>Cost</dt><dd>{i.cost}</dd></>}
            <dt>Created</dt><dd>{fmt.dt(i.createdAt)}</dd>
          </dl>
        </div>
        <div className="panel">
          <h3>Tags</h3>
          {i.tags.map((t) => <div key={t.id} className="row" style={{ justifyContent: 'space-between', padding: '4px 0' }}><span className="mono">{t.epc}</span><span><Badge tone={t.status === 'Active' ? 'ok' : ''}>{t.status}</Badge> <span className="muted small">{t.technology}</span></span></div>)}
          {i.tags.length === 0 && <div className="muted">No tag bound. Commission from a handheld or bind an EPC below.</div>}
          <div className="row" style={{ marginTop: 8 }}><input className="mono" placeholder="EPC hex" value={bindEpc ?? ''} onChange={(e) => setBindEpc(e.target.value)} /><button className="sm" disabled={!bindEpc} onClick={bind}>Bind</button></div>
          <ErrorBox error={bindErr} />
          <h3 style={{ marginTop: 16 }}>Attributes</h3>
          <dl className="kv">{Object.entries(i.attributes ?? {}).map(([k, v]) => <><dt key={k + 'k'}>{k}</dt><dd key={k + 'v'}>{String(v ?? '')}</dd></>)}</dl>
          {Object.keys(i.attributes ?? {}).length === 0 && <span className="muted">None</span>}
          {i.itemType?.isContainer && <><h3 style={{ marginTop: 16 }}>Contents ({data.children.length})</h3>{data.children.map((c) => <div key={c.id}><Link to={`/items/${c.id}`}>{c.name}</Link> <span className="muted small">{c.itemType} {c.state ? `· ${c.state}` : ''}</span></div>)}</>}
        </div>
        <div className="panel"><h3>Lifecycle</h3><LifecycleView lifecycle={lifecycle} /></div>
      </div>
      <div className="panel" style={{ marginTop: 16 }}>
        <h3>History (chain of custody)</h3>
        <ul className="timeline">
          {events.data?.map((e) => (
            <li key={e.id}><span className="when">{fmt.dt(e.occurredAt)}</span>
              <span><Badge>{e.type}</Badge>{' '}
                {e.type === 'Moved' && <>{locName(e.fromLocationId)} → <b>{locName(e.toLocationId)}</b> {e.data?.direction ? <span className="muted">({String(e.data.direction)})</span> : null}{e.data?.viaContainer ? <span className="muted"> via {String(e.data.viaContainer)}</span> : null}</>}
                {e.type === 'CustodyChanged' && <>{partyName(e.fromPartyId)} → <b>{partyName(e.toPartyId)}</b></>}
                {e.type === 'StateChanged' && <>{e.fromState ?? '—'} → <b>{e.toState}</b></>}
                {e.type === 'Counted' && <>{String(e.data?.result ?? 'counted')}{e.data?.stocktake ? ` in ${String(e.data.stocktake)}` : ''}</>}
                {e.type === 'QuantityChanged' && <>{String(e.data?.before)} {e.data?.delta && Number(e.data.delta) >= 0 ? '+' : ''}{String(e.data?.delta)}</>}
                {e.type === 'Inspected' && <>Result: <b>{String(e.data?.result ?? '')}</b></>}
                {(e.type === 'Packed' || e.type === 'Unpacked') && <>container {String(e.data?.container ?? '')}</>}
                {e.type === 'Commissioned' && <span className="mono">{String(e.data?.epc ?? '')}</span>}
                {e.type === 'Seen' && <>at {locName(e.toLocationId)}{e.data?.rssi ? <span className="muted"> · {String(e.data.rssi)} dBm</span> : null}</>}
                {e.operationId && <Link className="small" to={`/operations?id=${e.operationId}`} style={{ marginLeft: 8 }}>op</Link>}
              </span>
            </li>
          ))}
        </ul>
        {events.data?.length === 0 && <span className="muted">No history yet</span>}
      </div>
      {op && <Modal title={`${op} · ${i.name}`} onClose={() => setOp(null)}><OperationForm initialType={op} initialEpcs={i.tags.map((t) => t.epc)} initialItemIds={i.tags.length ? [] : [i.id]} onDone={() => { setOp(null); refetch(); events.refetch(); }} /></Modal>}
    </div>
  );
}

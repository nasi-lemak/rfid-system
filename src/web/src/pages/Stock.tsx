import { useState } from 'react';
import { post } from '../api/client';
import { useItemTypes, useLocations, useStockBalances, useStockMovements, useStockSummary } from '../api/hooks';
import type { StockAllocation } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';
import { OperationForm } from '../components/OperationForm';

/** Warehouse view over quantity items: replenishment summary, balances per location/lot, movements, FEFO pick allocation. */
export default function Stock() {
  const summary = useStockSummary();
  const types = useItemTypes(); const locs = useLocations();
  const [typeId, setTypeId] = useState(''); const [locId, setLocId] = useState('');
  const balances = useStockBalances({ itemTypeId: typeId || undefined, locationId: locId || undefined });
  const movements = useStockMovements({ days: 30, itemTypeId: typeId || undefined, take: 200 });
  const [tab, setTab] = useState<'summary' | 'balances' | 'movements' | 'allocate'>('summary');
  const [alloc, setAlloc] = useState<{ itemTypeCode: string; quantity: string; lotNumber: string }[]>([{ itemTypeCode: '', quantity: '', lotNumber: '' }]);
  const [from, setFrom] = useState(''); const [result, setResult] = useState<StockAllocation[] | null>(null); const [err, setErr] = useState<unknown>(null);
  const [move, setMove] = useState<{ itemIds: string[]; epcs: string[] } | null>(null);
  const qtyTypes = types.data?.filter((t) => t.category === 'Quantity') ?? [];
  const allocate = async () => {
    setErr(null);
    try { setResult(await post<StockAllocation[]>('/api/stock/allocate', { lines: alloc.filter((l) => l.itemTypeCode && l.quantity).map((l) => ({ itemTypeCode: l.itemTypeCode, quantity: Number(l.quantity), lotNumber: l.lotNumber || null })), fromLocationId: from || null, fefo: true })); }
    catch (e) { setErr(e); }
  };
  return (
    <div>
      <div className="topbar"><h1>Stock</h1><div className="row">{(['summary', 'balances', 'movements', 'allocate'] as const).map((t) => <button key={t} className={tab === t ? 'primary' : ''} onClick={() => setTab(t)}>{t[0].toUpperCase() + t.slice(1)}</button>)}</div></div>
      <p className="muted small">Quantity items (lots, SKUs, consumables) as a warehouse would see them. A balance is the sum of lot rows of a type at a location; movements are the quantity events; a pick list is an allocation over the lots (earliest expiry first). Execute picks with <b>Move quantity</b> (split/merge lots between bins), <b>Adjust</b> (consumption) or <b>Dispatch</b>.</p>
      {tab !== 'allocate' && <div className="row" style={{ marginBottom: 10 }}><select value={typeId} onChange={(e) => setTypeId(e.target.value)}><option value="">All quantity types</option>{qtyTypes.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>{tab === 'balances' && <select value={locId} onChange={(e) => setLocId(e.target.value)}><option value="">All locations</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name} ({l.kind})</option>)}</select>}</div>}
      {tab === 'summary' && <div className="panel table-wrap"><table><thead><tr><th>Item type</th><th>On hand</th><th>Locations</th><th>Lots</th><th>Reorder point</th><th>Shortfall</th><th>Consumed 30d</th><th>Days of cover</th><th>Expiry</th></tr></thead>
        <tbody>{summary.data?.filter((s) => !typeId || s.itemTypeId === typeId).map((s) => <tr key={s.itemTypeId}><td><b>{s.itemType}</b> <span className="muted small">{s.itemTypeCode}</span></td><td>{s.onHand} {s.unit}</td><td>{s.locations}</td><td>{s.lots}</td><td>{s.reorderPoint ?? '—'}</td><td>{s.shortfall > 0 ? <Badge tone="warn">{s.shortfall} {s.unit}</Badge> : <Badge tone="ok">ok</Badge>}</td><td>{s.consumed30d}</td><td>{s.daysOfCover ?? '—'}</td><td className="small">{s.earliestExpiry ?? '—'}{s.expiringSoon > 0 && <Badge tone="warn">{s.expiringSoon} lot(s) ≤ 30 d</Badge>}</td></tr>)}</tbody></table>{summary.data?.length === 0 && <div className="empty">No quantity items yet.</div>}</div>}
      {tab === 'balances' && <div className="panel table-wrap"><table><thead><tr><th>Item type</th><th>Location</th><th>Lot</th><th>Expiry</th><th>Quantity</th><th>Rows</th><th>Last seen</th></tr></thead>
        <tbody>{balances.data?.map((b, i) => <tr key={i}><td>{b.itemType}</td><td>{b.location ?? '—'}</td><td className="mono small">{b.lotNumber ?? '—'}</td><td className="small">{b.expiry ?? '—'}</td><td><b>{b.quantity}</b> {b.unit}</td><td>{b.rows}</td><td className="small">{fmt.ago(b.lastSeenAt)}</td></tr>)}</tbody></table></div>}
      {tab === 'movements' && <div className="panel table-wrap"><table><thead><tr><th>When</th><th>Item</th><th>Type</th><th>Lot</th><th>Kind</th><th>Delta</th><th>Location</th></tr></thead>
        <tbody>{movements.data?.map((m, i) => <tr key={i}><td className="small">{fmt.dt(m.at)}</td><td className="mono small">{m.identifier}</td><td>{m.itemType}</td><td className="mono small">{m.lotNumber ?? ''}</td><td><Badge tone={m.kind.startsWith('transfer') ? 'info' : m.delta < 0 ? 'warn' : 'ok'}>{m.kind}</Badge></td><td style={{ color: m.delta < 0 ? 'var(--warn, #e39b2b)' : undefined }}>{m.delta > 0 ? '+' : ''}{m.delta}</td><td>{m.location ?? '—'}</td></tr>)}</tbody></table></div>}
      {tab === 'allocate' && <div className="panel">
        <h3>Pick list (FEFO)</h3>
        <div className="form">
          {alloc.map((l, i) => <div className="row" key={i}><select style={{ flex: 2 }} value={l.itemTypeCode} onChange={(e) => setAlloc(alloc.map((x, j) => (j === i ? { ...x, itemTypeCode: e.target.value } : x)))}><option value="">— item type —</option>{qtyTypes.map((t) => <option key={t.code} value={t.code}>{t.name}</option>)}</select><input style={{ flex: 1 }} type="number" step="any" placeholder="quantity" value={l.quantity} onChange={(e) => setAlloc(alloc.map((x, j) => (j === i ? { ...x, quantity: e.target.value } : x)))} /><input style={{ flex: 1 }} placeholder="lot (optional)" value={l.lotNumber} onChange={(e) => setAlloc(alloc.map((x, j) => (j === i ? { ...x, lotNumber: e.target.value } : x)))} /><button type="button" className="sm danger" onClick={() => setAlloc(alloc.filter((_, j) => j !== i))}>✕</button></div>)}
          <div className="row"><button type="button" className="sm" onClick={() => setAlloc([...alloc, { itemTypeCode: '', quantity: '', lotNumber: '' }])}>+ Line</button><select value={from} onChange={(e) => setFrom(e.target.value)}><option value="">Pick from anywhere</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select><button className="primary" onClick={allocate}>Allocate</button></div>
        </div>
        <ErrorBox error={err} />
        {result && result.map((r, i) => <div key={i} style={{ marginTop: 12 }}>
          <div className="row"><b>{r.itemType}</b> <Badge tone={r.shortfall > 0 ? 'warn' : 'ok'}>{r.allocated} / {r.requested}{r.shortfall > 0 ? ` · short ${r.shortfall}` : ''}</Badge>{r.picks.length > 0 && <button className="sm" onClick={() => setMove({ itemIds: r.picks.map((p) => p.itemId), epcs: [] })}>Move picked lots…</button>}</div>
          <table><thead><tr><th>Lot row</th><th>Location</th><th>Lot</th><th>Expiry</th><th>Available</th><th>Take</th></tr></thead><tbody>{r.picks.map((p) => <tr key={p.itemId}><td className="mono small">{p.identifier}</td><td>{p.location ?? '—'}</td><td className="mono small">{p.lotNumber ?? '—'}</td><td className="small">{p.expiry ?? '—'}</td><td>{p.available}</td><td><b>{p.take}</b></td></tr>)}</tbody></table>
        </div>)}
      </div>}
      {move && <Modal title="Move quantity" onClose={() => setMove(null)}><p className="muted small">Runs <b>Move quantity</b> for the picked lot rows: the entered quantity leaves each row and lands on the matching lot at the destination. Adjust the quantity per run if the picks differ.</p><OperationForm initialType="MoveQuantity" initialItemIds={move.itemIds} onDone={() => { setMove(null); balances.refetch(); summary.refetch(); }} /></Modal>}
    </div>
  );
}

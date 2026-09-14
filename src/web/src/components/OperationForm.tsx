import { useState, type FormEvent } from 'react';
import { useItemTypes, useItems, useLocations, useLookups, useParties, useRunOperation } from '../api/hooks';
import type { OperationRequest, OperationResult } from '../api/types';
import { Badge, ErrorBox, toneForStatus } from './ui';

const help: Record<string, string> = {
  Receive: 'Items arrive at a location (goods-in, returns from customer, sample intake).', Transfer: 'Move items to another location; containers carry their contents.',
  Issue: 'Hand items to a person / department / customer (checkout, loan, assignment).', Return: 'Take items back into stock, clearing custody.',
  Count: 'Confirm presence without moving (cycle count).', Dispatch: 'Ship to a customer or external location (sale, rental, keg delivery).',
  Inspect: 'Record an inspection; Target state = result (e.g. Passed / Failed).', Maintain: 'Send for maintenance / repair.', Dispose: 'Retire permanently; tags are released.',
  Pack: 'Put items inside a container item (pallet, tray, kit, cage).', Unpack: 'Remove items from their container.',
  ProcessStage: 'Advance items through their lifecycle (wash stage, sterilisation, production step).', Commission: 'Bind a new EPC to a new or existing item.', Adjust: 'Change quantity by a delta (consumption, receipt of bulk stock).',
};

export function OperationForm({ initialType = 'Transfer', initialEpcs = [], initialItemIds = [], onDone }: { initialType?: string; initialEpcs?: string[]; initialItemIds?: string[]; onDone?: (r: OperationResult) => void }) {
  const lookups = useLookups();
  const locs = useLocations();
  const parties = useParties();
  const types = useItemTypes();
  const containers = useItems({ pageSize: 200 });
  const run = useRunOperation();
  const [type, setType] = useState(initialType);
  const [epcText, setEpcText] = useState(initialEpcs.join('\n'));
  const [f, setF] = useState({ fromLocationId: '', toLocationId: '', partyId: '', containerItemId: '', targetState: '', reference: '', notes: '', dueBackAt: '', quantity: '' });
  const [newItem, setNewItem] = useState({ itemTypeId: '', identifier: '', name: '' });
  const [result, setResult] = useState<OperationResult | null>(null);
  const needsTo = ['Receive', 'Transfer', 'Dispatch'].includes(type);
  const showTo = needsTo || ['Issue', 'Return', 'Maintain', 'Unpack', 'ProcessStage', 'Count'].includes(type);
  const showParty = ['Issue', 'Transfer', 'Dispatch'].includes(type);
  const showState = ['ProcessStage', 'Inspect', 'Maintain'].includes(type);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const epcs = epcText.split(/[\s,;]+/).map((s) => s.trim()).filter(Boolean);
    const lines: OperationRequest['lines'] = epcs.map((epc) => ({ epc, quantity: f.quantity ? Number(f.quantity) : undefined, newItem: type === 'Commission' ? { itemTypeId: newItem.itemTypeId, identifier: newItem.identifier || epc, name: newItem.name || newItem.identifier || epc } : undefined }));
    for (const id of initialItemIds) lines.push({ itemId: id, quantity: f.quantity ? Number(f.quantity) : undefined });
    const req: OperationRequest = { type, lines, fromLocationId: f.fromLocationId || undefined, toLocationId: f.toLocationId || undefined, partyId: f.partyId || undefined, containerItemId: f.containerItemId || undefined, targetState: f.targetState || undefined, reference: f.reference || undefined, notes: f.notes || undefined, dueBackAt: f.dueBackAt ? new Date(f.dueBackAt).toISOString() : undefined };
    const r = await run.mutateAsync(req);
    setResult(r);
    if (r.rejected === 0 && r.unknown === 0) onDone?.(r);
  };
  const states = (() => { const s = new Set<string>(); types.data?.forEach((t) => t.lifecycle?.states.forEach((x) => s.add(x))); return [...s]; })();
  return (
    <form className="form" onSubmit={submit}>
      <div className="row">
        <label style={{ flex: 1 }}>Operation<select value={type} onChange={(e) => { setType(e.target.value); setResult(null); }}>{(lookups.data?.operationTypes ?? [type]).map((t) => <option key={t}>{t}</option>)}</select></label>
        <label style={{ flex: 1 }}>Reference<input value={f.reference} onChange={(e) => setF({ ...f, reference: e.target.value })} placeholder="PO / work order / case no." /></label>
      </div>
      <div className="muted small">{help[type]}</div>
      <div className="form cols-2">
        {showTo && <label>{type === 'Return' || type === 'Unpack' ? 'Return to location' : 'Destination'}{needsTo ? ' *' : ''}<select required={needsTo} value={f.toLocationId} onChange={(e) => setF({ ...f, toLocationId: e.target.value })}><option value="">—</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name} ({l.kind})</option>)}</select></label>}
        {showParty && <label>Party{type === 'Issue' ? ' *' : ''}<select required={type === 'Issue'} value={f.partyId} onChange={(e) => setF({ ...f, partyId: e.target.value })}><option value="">—</option>{parties.data?.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.kind})</option>)}</select></label>}
        {(type === 'Issue' || type === 'Dispatch') && <label>Due back<input type="datetime-local" value={f.dueBackAt} onChange={(e) => setF({ ...f, dueBackAt: e.target.value })} /></label>}
        {type === 'Pack' && <label>Container *<select required value={f.containerItemId} onChange={(e) => setF({ ...f, containerItemId: e.target.value })}><option value="">—</option>{containers.data?.items.filter((i) => i.itemType?.isContainer).map((i) => <option key={i.id} value={i.id}>{i.name} ({i.identifier})</option>)}</select></label>}
        {showState && <label>Target state<input list="states" value={f.targetState} onChange={(e) => setF({ ...f, targetState: e.target.value })} placeholder="e.g. InWash, Passed" /><datalist id="states">{states.map((s) => <option key={s} value={s} />)}</datalist></label>}
        {type === 'Adjust' && <label>Quantity delta *<input type="number" step="any" required value={f.quantity} onChange={(e) => setF({ ...f, quantity: e.target.value })} placeholder="-5 or +20" /></label>}
        {type === 'Commission' && <><label>Item type *<select required value={newItem.itemTypeId} onChange={(e) => setNewItem({ ...newItem, itemTypeId: e.target.value })}><option value="">—</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label><label>Identifier<input value={newItem.identifier} onChange={(e) => setNewItem({ ...newItem, identifier: e.target.value })} placeholder="defaults to EPC" /></label><label>Name<input value={newItem.name} onChange={(e) => setNewItem({ ...newItem, name: e.target.value })} /></label></>}
      </div>
      <label>EPCs (one per line){initialItemIds.length ? <span className="muted"> · plus {initialItemIds.length} selected item(s)</span> : null}<textarea className="mono" rows={4} value={epcText} onChange={(e) => setEpcText(e.target.value)} placeholder="Paste or scan EPCs…" /></label>
      <label>Notes<input value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
      <ErrorBox error={run.error} />
      {result && <div className="panel"><div className="row"><Badge tone="ok">{result.ok} ok</Badge>{result.unknown > 0 && <Badge tone="info">{result.unknown} unknown</Badge>}{result.rejected > 0 && <Badge tone="warn">{result.rejected} rejected</Badge>}</div><table><tbody>{result.lines.map((l, i) => <tr key={i}><td className="mono">{l.epc}</td><td>{l.itemName}</td><td><Badge tone={toneForStatus(l.result)}>{l.result}</Badge></td><td className="small">{l.newState ?? ''} {l.message ?? ''}</td></tr>)}</tbody></table></div>}
      <div className="row end"><button className="primary" disabled={run.isPending}>{run.isPending ? 'Processing…' : `Run ${type}`}</button></div>
    </form>
  );
}

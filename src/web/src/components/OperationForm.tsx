import { useState, type FormEvent } from 'react';
import { useItemTypes, useItems, useLocations, useOperationDefinitions, useParties, useRunOperation } from '../api/hooks';
import type { OperationRequest, OperationResult } from '../api/types';
import { builtInCodes, fallbackDefinition, uiFor } from './operationUi';
import { Badge, ErrorBox, toneForStatus } from './ui';

/**
 * One form for every operation. What it asks for is derived from the operation definition (requirements +
 * effects), so template- and tenant-defined operations (e.g. "Sterilise") get the same UI as the built-ins.
 */
export function OperationForm({ initialType = 'Transfer', initialEpcs = [], initialItemIds = [], onDone }: { initialType?: string; initialEpcs?: string[]; initialItemIds?: string[]; onDone?: (r: OperationResult) => void }) {
  const defs = useOperationDefinitions();
  const locs = useLocations();
  const parties = useParties();
  const types = useItemTypes();
  const containers = useItems({ pageSize: 200 });
  const run = useRunOperation();
  const [code, setCode] = useState(initialType);
  const [epcText, setEpcText] = useState(initialEpcs.join('\n'));
  const [f, setF] = useState({ fromLocationId: '', toLocationId: '', partyId: '', containerItemId: '', targetState: '', reference: '', notes: '', dueBackAt: '', quantity: '' });
  const [newItem, setNewItem] = useState({ itemTypeId: '', identifier: '', name: '' });
  const [result, setResult] = useState<OperationResult | null>(null);
  const available = defs.data && defs.data.length > 0 ? defs.data : builtInCodes.map(fallbackDefinition);
  const def = available.find((d) => d.code === code) ?? fallbackDefinition(code);
  const ui = uiFor(def);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const epcs = epcText.split(/[\s,;]+/).map((s) => s.trim()).filter(Boolean);
    const lines: OperationRequest['lines'] = epcs.map((epc) => ({ epc, quantity: f.quantity ? Number(f.quantity) : undefined, newItem: ui.isCommission ? { itemTypeId: newItem.itemTypeId, identifier: newItem.identifier || epc, name: newItem.name || newItem.identifier || epc } : undefined }));
    for (const id of initialItemIds) lines.push({ itemId: id, quantity: f.quantity ? Number(f.quantity) : undefined });
    const req: OperationRequest = { type: def.baseType, operation: def.code, lines, fromLocationId: f.fromLocationId || undefined, toLocationId: f.toLocationId || undefined, partyId: f.partyId || undefined, containerItemId: f.containerItemId || undefined, targetState: f.targetState || undefined, reference: f.reference || undefined, notes: f.notes || undefined, dueBackAt: f.dueBackAt ? new Date(f.dueBackAt).toISOString() : undefined };
    const r = await run.mutateAsync(req);
    setResult(r);
    if (r.rejected === 0 && r.unknown === 0) onDone?.(r);
  };
  const states = (() => { const s = new Set<string>(); types.data?.filter((t) => def.itemTypeCodes.length === 0 || def.itemTypeCodes.includes(t.code)).forEach((t) => t.lifecycle?.states.forEach((x) => s.add(x))); return [...s]; })();
  const returnLabel = def.baseType === 'Return' || def.baseType === 'Unpack';
  return (
    <form className="form" onSubmit={submit}>
      <div className="row">
        <label style={{ flex: 1 }}>Operation<select value={code} onChange={(e) => { setCode(e.target.value); setResult(null); }}>
          {available.filter((d) => d.isBuiltIn).map((d) => <option key={d.code} value={d.code}>{d.name}</option>)}
          {available.some((d) => !d.isBuiltIn) && <optgroup label="Vertical / custom">{available.filter((d) => !d.isBuiltIn).map((d) => <option key={d.code} value={d.code}>{d.icon ? `${d.icon} ` : ''}{d.name}{d.vertical ? ` · ${d.vertical}` : ''}</option>)}</optgroup>}
        </select></label>
        <label style={{ flex: 1 }}>Reference<input value={f.reference} onChange={(e) => setF({ ...f, reference: e.target.value })} placeholder="PO / work order / case no." /></label>
      </div>
      <div className="muted small">{def.description}{!def.isBuiltIn && <> · <span className="mono">{def.effects.map((e) => e.kind).join(' → ')}</span>{def.itemTypeCodes.length > 0 && <> · applies to {def.itemTypeCodes.join(', ')}</>}</>}</div>
      <div className="form cols-2">
        {ui.showTo && <label>{returnLabel ? 'Return to location' : 'Destination'}{ui.needsTo ? ' *' : ''}<select required={ui.needsTo} value={f.toLocationId} onChange={(e) => setF({ ...f, toLocationId: e.target.value })}><option value="">—</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name} ({l.kind})</option>)}</select></label>}
        {ui.showParty && <label>Party{ui.needsParty ? ' *' : ''}<select required={ui.needsParty} value={f.partyId} onChange={(e) => setF({ ...f, partyId: e.target.value })}><option value="">—</option>{parties.data?.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.kind})</option>)}</select></label>}
        {ui.showDueBack && <label>Due back<input type="datetime-local" value={f.dueBackAt} onChange={(e) => setF({ ...f, dueBackAt: e.target.value })} /></label>}
        {ui.needsContainer && <label>Container *<select required value={f.containerItemId} onChange={(e) => setF({ ...f, containerItemId: e.target.value })}><option value="">—</option>{containers.data?.items.filter((i) => i.itemType?.isContainer).map((i) => <option key={i.id} value={i.id}>{i.name} ({i.identifier})</option>)}</select></label>}
        {ui.showState && <label>Target state{ui.needsState ? ' *' : ''}<input list="states" required={ui.needsState} value={f.targetState} onChange={(e) => setF({ ...f, targetState: e.target.value })} placeholder={def.isBuiltIn ? 'e.g. InWash, Passed' : 'leave empty to follow the lifecycle'} /><datalist id="states">{states.map((s) => <option key={s} value={s} />)}</datalist></label>}
        {ui.showQuantity && <label>Quantity delta{ui.needsQuantity ? ' *' : ''}<input type="number" step="any" required={ui.needsQuantity} value={f.quantity} onChange={(e) => setF({ ...f, quantity: e.target.value })} placeholder="-5 or +20" /></label>}
        {ui.isCommission && <><label>Item type *<select required value={newItem.itemTypeId} onChange={(e) => setNewItem({ ...newItem, itemTypeId: e.target.value })}><option value="">—</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label><label>Identifier<input value={newItem.identifier} onChange={(e) => setNewItem({ ...newItem, identifier: e.target.value })} placeholder="defaults to EPC" /></label><label>Name<input value={newItem.name} onChange={(e) => setNewItem({ ...newItem, name: e.target.value })} /></label></>}
      </div>
      <label>EPCs (one per line){initialItemIds.length ? <span className="muted"> · plus {initialItemIds.length} selected item(s)</span> : null}<textarea className="mono" rows={4} value={epcText} onChange={(e) => setEpcText(e.target.value)} placeholder="Paste or scan EPCs…" /></label>
      <label>Notes<input value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
      <ErrorBox error={run.error} />
      {result && <div className="panel"><div className="row"><Badge tone="ok">{result.ok} ok</Badge>{result.unknown > 0 && <Badge tone="info">{result.unknown} unknown</Badge>}{result.rejected > 0 && <Badge tone="warn">{result.rejected} rejected</Badge>}</div><table><tbody>{result.lines.map((l, i) => <tr key={i}><td className="mono">{l.epc}</td><td>{l.itemName}</td><td><Badge tone={toneForStatus(l.result)}>{l.result}</Badge></td><td className="small">{l.newState ?? ''} {l.message ?? ''}</td></tr>)}</tbody></table></div>}
      <div className="row end"><button className="primary" disabled={run.isPending}>{run.isPending ? 'Processing…' : `Run ${def.name}`}</button></div>
    </form>
  );
}

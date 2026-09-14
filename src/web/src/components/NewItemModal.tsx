import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { useInvalidate, useItemTypes, useLocations } from '../api/hooks';
import { post } from '../api/client';
import type { Item, ItemType } from '../api/types';
import { ErrorBox, Modal } from './ui';

/** Renders inputs for an item type's custom attribute schema. */
export function AttributeFields({ type, values, onChange }: { type?: ItemType | null; values: Record<string, unknown>; onChange: (v: Record<string, unknown>) => void }) {
  if (!type?.attributeSchema?.length) return null;
  return <>{type.attributeSchema.map((a) => (
    <label key={a.name}>{a.name}{a.required ? ' *' : ''}
      {a.type === 'select' ? <select value={String(values[a.name] ?? '')} onChange={(e) => onChange({ ...values, [a.name]: e.target.value })}><option value="">—</option>{a.options?.map((o) => <option key={o}>{o}</option>)}</select>
        : a.type === 'bool' ? <select value={String(values[a.name] ?? '')} onChange={(e) => onChange({ ...values, [a.name]: e.target.value === 'true' })}><option value="">—</option><option value="true">Yes</option><option value="false">No</option></select>
        : <input type={a.type === 'number' ? 'number' : a.type === 'date' ? 'date' : 'text'} required={a.required} value={String(values[a.name] ?? '')} onChange={(e) => onChange({ ...values, [a.name]: a.type === 'number' ? Number(e.target.value) : e.target.value })} />}
    </label>
  ))}</>;
}

export function NewItemModal({ onClose, epc: initialEpc }: { onClose: () => void; epc?: string }) {
  const types = useItemTypes();
  const locs = useLocations();
  const inv = useInvalidate();
  const nav = useNavigate();
  const [form, setForm] = useState({ itemTypeId: '', identifier: '', name: '', currentLocationId: '', epc: initialEpc ?? '', quantity: 1, lotNumber: '', expiryDate: '', state: '' });
  const [attrs, setAttrs] = useState<Record<string, unknown>>({});
  const [error, setError] = useState<unknown>(null);
  const type = types.data?.find((t) => t.id === form.itemTypeId);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); setError(null);
    try {
      const item = await post<Item>('/api/items', { ...form, currentLocationId: form.currentLocationId || null, epc: form.epc || null, lotNumber: form.lotNumber || null, expiryDate: form.expiryDate || null, state: form.state || null, attributes: attrs });
      inv('items', 'dashboard', 'tags', 'item-types'); onClose(); nav(`/items/${item.id}`);
    } catch (err) { setError(err); }
  };
  return (
    <Modal title="New item" onClose={onClose}>
      <form className="form cols-2" onSubmit={submit}>
        <label>Item type *<select required value={form.itemTypeId} onChange={(e) => setForm({ ...form, itemTypeId: e.target.value, state: '' })}><option value="">Select…</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
        <label>Identifier / serial *<input required value={form.identifier} onChange={(e) => setForm({ ...form, identifier: e.target.value })} /></label>
        <label>Name<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder={form.identifier} /></label>
        <label>Location<select value={form.currentLocationId} onChange={(e) => setForm({ ...form, currentLocationId: e.target.value })}><option value="">—</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        <label>EPC (tag)<input className="mono" value={form.epc} onChange={(e) => setForm({ ...form, epc: e.target.value })} placeholder="optional – bind later from a handheld" /></label>
        {type?.lifecycle?.states.length ? <label>Initial state<select value={form.state} onChange={(e) => setForm({ ...form, state: e.target.value })}><option value="">{type.lifecycle.initial ?? '—'} (default)</option>{type.lifecycle.states.map((s) => <option key={s}>{s}</option>)}</select></label> : <div />}
        {type?.category === 'Quantity' && <><label>Quantity ({type.unit})<input type="number" step="any" value={form.quantity} onChange={(e) => setForm({ ...form, quantity: Number(e.target.value) })} /></label><label>Lot number<input value={form.lotNumber} onChange={(e) => setForm({ ...form, lotNumber: e.target.value })} /></label></>}
        {type?.tracksExpiry && <label>Expiry date<input type="date" value={form.expiryDate} onChange={(e) => setForm({ ...form, expiryDate: e.target.value })} /></label>}
        <AttributeFields type={type} values={attrs} onChange={setAttrs} />
        <div style={{ gridColumn: '1 / -1' }}><ErrorBox error={error} /></div>
        <div className="row end" style={{ gridColumn: '1 / -1' }}><button type="button" onClick={onClose}>Cancel</button><button className="primary">Create</button></div>
      </form>
    </Modal>
  );
}

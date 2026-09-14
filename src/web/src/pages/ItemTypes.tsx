import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useItemTypes, useRemove, useSave } from '../api/hooks';
import type { ItemType } from '../api/types';
import { Badge, ErrorBox, LifecycleView, Modal } from '../components/ui';

const blank: Omit<ItemType, 'id'> = { name: '', code: '', category: 'Serialized', isContainer: false, tracksExpiry: false, tracksCycles: false, maxCycles: null, requiresInspection: false, inspectionIntervalDays: null, reorderPoint: null, unit: '', attributeSchema: [], lifecycle: { initial: '', states: [], transitions: [] } };

export default function ItemTypes() {
  const { data } = useItemTypes();
  const save = useSave<ItemType>('/api/item-types', ['item-types', 'templates']);
  const remove = useRemove('/api/item-types', ['item-types']);
  const [edit, setEdit] = useState<(Partial<ItemType> & { id?: string }) | null>(null);
  const [lcText, setLcText] = useState('');
  const [attrText, setAttrText] = useState('');
  const open = (t?: ItemType) => { const v = t ?? { ...blank }; setEdit(v); setLcText(JSON.stringify(v.lifecycle ?? blank.lifecycle, null, 2)); setAttrText(JSON.stringify(v.attributeSchema ?? [], null, 2)); };
  const submit = async (e: FormEvent) => {
    e.preventDefault(); if (!edit) return;
    let lifecycle: ItemType['lifecycle'] = null; let attributeSchema: ItemType['attributeSchema'] = [];
    try { lifecycle = lcText.trim() ? JSON.parse(lcText) : null; attributeSchema = attrText.trim() ? JSON.parse(attrText) : []; } catch { alert('Lifecycle / attribute JSON is invalid'); return; }
    if (lifecycle && lifecycle.states.length === 0) lifecycle = null;
    await save.mutateAsync({ id: edit.id, body: { ...edit, lifecycle, attributeSchema, maxCycles: edit.maxCycles || null, inspectionIntervalDays: edit.inspectionIntervalDays || null, reorderPoint: edit.reorderPoint || null, unit: edit.unit || null } });
    setEdit(null);
  };
  return (
    <div>
      <div className="topbar"><h1>Item types</h1><div className="row"><Link className="btn" to="/templates">Install from template</Link><button className="primary" onClick={() => open()}>+ New type</button></div></div>
      <div className="grid cols-2">
        {data?.map((t) => (
          <div className="panel" key={t.id}>
            <div className="topbar"><div><h2 style={{ marginBottom: 2 }}>{t.name} <span className="muted small">{t.code}</span></h2><span className="muted small">{t.vertical ?? ''} · <Link to={`/items?itemTypeId=${t.id}`}>{t.itemCount ?? 0} items</Link></span></div><div className="row"><Link className="btn sm" to={`/labels?itemTypeId=${t.id}`} style={{ padding: '4px 8px', fontSize: 12 }}>Label</Link><button className="sm" onClick={() => open(t)}>Edit</button><button className="sm danger" onClick={() => confirm('Delete type?') && remove.mutate(t.id)}>Delete</button></div></div>
            <div className="chips" style={{ marginBottom: 8 }}><Badge>{t.category}</Badge>{t.isContainer && <Badge tone="info">container</Badge>}{t.tracksCycles && <Badge>cycles{t.maxCycles ? ` ≤ ${t.maxCycles}` : ''}</Badge>}{t.tracksExpiry && <Badge>expiry</Badge>}{t.requiresInspection && <Badge>inspect every {t.inspectionIntervalDays}d</Badge>}{t.reorderPoint != null && <Badge>reorder &lt; {t.reorderPoint}</Badge>}</div>
            <LifecycleView lifecycle={t.lifecycle} />
            {t.attributeSchema?.length > 0 && <div className="small muted" style={{ marginTop: 8 }}>Attributes: {t.attributeSchema.map((a) => a.name + (a.required ? '*' : '')).join(', ')}</div>}
          </div>
        ))}
      </div>
      {data?.length === 0 && <div className="panel empty">No item types. <Link to="/templates">Install a solution template</Link> to get started.</div>}
      {edit && <Modal title={edit.id ? 'Edit item type' : 'New item type'} onClose={() => setEdit(null)} width={760}>
        <form className="form" onSubmit={submit}>
          <div className="form cols-2">
            <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Code *<input required value={edit.code ?? ''} onChange={(e) => setEdit({ ...edit, code: e.target.value.toUpperCase() })} /></label>
            <label>Category<select value={edit.category} onChange={(e) => setEdit({ ...edit, category: e.target.value as ItemType['category'] })}><option>Serialized</option><option>Quantity</option></select></label>
            <label>Unit (quantity types)<input value={edit.unit ?? ''} onChange={(e) => setEdit({ ...edit, unit: e.target.value })} /></label>
            <label>Max cycles<input type="number" value={edit.maxCycles ?? ''} onChange={(e) => setEdit({ ...edit, maxCycles: e.target.value ? Number(e.target.value) : null, tracksCycles: !!e.target.value })} /></label>
            <label>Inspection interval (days)<input type="number" value={edit.inspectionIntervalDays ?? ''} onChange={(e) => setEdit({ ...edit, inspectionIntervalDays: e.target.value ? Number(e.target.value) : null, requiresInspection: !!e.target.value })} /></label>
            <label>Reorder point<input type="number" step="any" value={edit.reorderPoint ?? ''} onChange={(e) => setEdit({ ...edit, reorderPoint: e.target.value ? Number(e.target.value) : null })} /></label>
            <div className="row" style={{ alignItems: 'end', paddingBottom: 8 }}><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.isContainer} onChange={(e) => setEdit({ ...edit, isContainer: e.target.checked })} /> Container</label><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.tracksExpiry} onChange={(e) => setEdit({ ...edit, tracksExpiry: e.target.checked })} /> Tracks expiry</label><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.tracksCycles} onChange={(e) => setEdit({ ...edit, tracksCycles: e.target.checked })} /> Tracks cycles</label></div>
          </div>
          <label>Lifecycle (JSON: initial, states, transitions[from,to,on,incrementCycle])<textarea className="mono" rows={8} value={lcText} onChange={(e) => setLcText(e.target.value)} /></label>
          <label>Attribute schema (JSON: [{'{'}name, type: string|number|date|bool|select, required, options{'}'}])<textarea className="mono" rows={4} value={attrText} onChange={(e) => setAttrText(e.target.value)} /></label>
          <ErrorBox error={save.error} />
          <div className="row end"><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary" disabled={save.isPending}>Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

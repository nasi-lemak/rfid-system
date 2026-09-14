import { useMemo, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useLocations, useLookups, useRemove, useSave } from '../api/hooks';
import type { Location } from '../api/types';
import { Badge, ErrorBox, Modal } from '../components/ui';

export default function Locations() {
  const { data } = useLocations();
  const lookups = useLookups();
  const save = useSave<Location>('/api/locations', ['locations']);
  const remove = useRemove('/api/locations', ['locations']);
  const [sel, setSel] = useState<Location | null>(null);
  const [edit, setEdit] = useState<Partial<Location> | null>(null);
  const [attrText, setAttrText] = useState('{}');
  const tree = useMemo(() => {
    const byParent = new Map<string | null, Location[]>();
    (data ?? []).forEach((l) => { const k = l.parentId ?? null; byParent.set(k, [...(byParent.get(k) ?? []), l]); });
    return byParent;
  }, [data]);
  const open = (l?: Partial<Location>) => { setEdit(l ?? { kind: 'Zone', name: '', parentId: sel?.id ?? null, isMobile: false }); setAttrText(JSON.stringify(l?.attributes ?? {}, null, 2)); };
  const submit = async (e: FormEvent) => {
    e.preventDefault(); if (!edit) return;
    let attributes = {}; try { attributes = JSON.parse(attrText || '{}'); } catch { alert('Attributes JSON invalid'); return; }
    await save.mutateAsync({ id: edit.id, body: { ...edit, parentId: edit.parentId || null, attributes } }); setEdit(null);
  };
  const Node = ({ l }: { l: Location }) => (
    <li>
      <div className={'node' + (sel?.id === l.id ? ' active' : '')} onClick={() => setSel(l)}><Badge>{l.kind}</Badge><span>{l.name}</span>{l.code && <span className="muted small">{l.code}</span>}{(l.itemCount ?? 0) > 0 && <span className="muted small">· {l.itemCount}</span>}{l.isMobile && <span title="Mobile location">🚚</span>}</div>
      {tree.get(l.id) && <ul>{tree.get(l.id)!.map((c) => <Node key={c.id} l={c} />)}</ul>}
    </li>
  );
  return (
    <div>
      <div className="topbar"><h1>Locations</h1><button className="primary" onClick={() => open()}>+ New location{sel ? ` under ${sel.name}` : ''}</button></div>
      <div className="grid" style={{ gridTemplateColumns: '1fr 360px' }}>
        <div className="panel tree"><ul>{(tree.get(null) ?? []).map((l) => <Node key={l.id} l={l} />)}</ul>{data?.length === 0 && <div className="empty">No locations yet. Create a Site first, then buildings, rooms, zones, racks, bins…</div>}</div>
        <div className="panel">
          {sel ? <>
            <h2>{sel.name}</h2>
            <dl className="kv"><dt>Kind</dt><dd>{sel.kind}</dd><dt>Code</dt><dd>{sel.code ?? '—'}</dd><dt>Items here</dt><dd><Link to={`/items?locationId=${sel.id}`}>{sel.itemCount ?? 0} (direct) · view subtree</Link></dd><dt>Mobile</dt><dd>{sel.isMobile ? 'Yes' : 'No'}</dd>{sel.latitude != null && <><dt>GPS</dt><dd>{sel.latitude}, {sel.longitude}</dd></>}<dt>Attributes</dt><dd className="mono small">{JSON.stringify(sel.attributes)}</dd></dl>
            <p className="muted small">Attributes such as <code>clean: true</code>, <code>restricted: true</code>, <code>quarantine: true</code> or <code>kanban: true</code> are used by rule conditions (<code>toLocation.clean</code> …).</p>
            <div className="row"><button className="sm" onClick={() => open(sel)}>Edit</button><button className="sm danger" onClick={() => confirm('Delete location?') && remove.mutate(sel.id, { onSuccess: () => setSel(null) })}>Delete</button></div>
            <ErrorBox error={remove.error} />
          </> : <div className="muted">Select a location</div>}
        </div>
      </div>
      {edit && <Modal title={edit.id ? 'Edit location' : 'New location'} onClose={() => setEdit(null)}>
        <form className="form cols-2" onSubmit={submit}>
          <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
          <label>Code<input value={edit.code ?? ''} onChange={(e) => setEdit({ ...edit, code: e.target.value })} /></label>
          <label>Kind<select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value })}>{lookups.data?.locationKinds.map((k) => <option key={k}>{k}</option>)}</select></label>
          <label>Parent<select value={edit.parentId ?? ''} onChange={(e) => setEdit({ ...edit, parentId: e.target.value || null })}><option value="">(root)</option>{data?.filter((l) => l.id !== edit.id).map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name}</option>)}</select></label>
          <label>Latitude<input type="number" step="any" value={edit.latitude ?? ''} onChange={(e) => setEdit({ ...edit, latitude: e.target.value ? Number(e.target.value) : null })} /></label>
          <label>Longitude<input type="number" step="any" value={edit.longitude ?? ''} onChange={(e) => setEdit({ ...edit, longitude: e.target.value ? Number(e.target.value) : null })} /></label>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.isMobile} onChange={(e) => setEdit({ ...edit, isMobile: e.target.checked })} /> Mobile (vehicle / vessel / trailer)</label>
          <label style={{ gridColumn: '1 / -1' }}>Attributes (JSON)<textarea className="mono" rows={3} value={attrText} onChange={(e) => setAttrText(e.target.value)} /></label>
          <div style={{ gridColumn: '1 / -1' }}><ErrorBox error={save.error} /></div>
          <div className="row end" style={{ gridColumn: '1 / -1' }}><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

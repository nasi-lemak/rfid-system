import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { del, get, post, put } from '../api/client';
import { useItemTypes } from '../api/hooks';
import type { LabelDesign, LabelElement } from '../api/types';
import { ErrorBox } from '../components/ui';

const sample: Record<string, string> = { name: 'Makita 18V drill', identifier: 'TL-0001', epc: '3034F8B2000100000003E8', type: 'Tool', location: 'Tool Crib', state: 'InCrib', lot: '2409-A', expiry: '2027-01-31', quantity: '1', date: new Date().toISOString().slice(0, 10) };
const fill = (t?: string | null) => (t ?? '').replace(/\{([a-zA-Z0-9_.]+)\}/g, (_, k: string) => sample[k] ?? (k.startsWith('attributes.') ? k.split('.')[1].toUpperCase() : `{${k}}`));

export default function LabelDesigner() {
  const [sp, setSp] = useSearchParams();
  const types = useItemTypes();
  const typeId = sp.get('itemTypeId') ?? '';
  const [design, setDesign] = useState<LabelDesign | null>(null);
  const [sel, setSel] = useState<number | null>(null);
  const [zpl, setZpl] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => { (typeId ? get<LabelDesign>(`/api/labels/designs/item-types/${typeId}`) : get<LabelDesign>('/api/labels/designs/default')).then(setDesign).catch(setError); }, [typeId]);
  useEffect(() => { if (!design) return; const t = setTimeout(() => post<{ rendered: string }>('/api/labels/designs/compile', design).then((r) => setZpl(r.rendered)).catch(() => {}), 400); return () => clearTimeout(t); }, [design]);
  if (!design) return <div className="muted">Loading…</div>;
  const sc = 6; // px per mm on screen
  const upd = (i: number, patch: Partial<LabelElement>) => setDesign({ ...design, elements: design.elements.map((e, j) => (j === i ? { ...e, ...patch } : e)) });
  const add = (type: LabelElement['type']) => { setDesign({ ...design, elements: [...design.elements, { type, x: 5, y: 5, width: type === 'box' || type === 'line' ? 30 : null, height: type === 'barcode' ? 12 : type === 'box' ? 15 : type === 'line' ? 0.5 : null, text: type === 'text' ? 'Text {name}' : type === 'barcode' || type === 'qr' ? '{identifier}' : null, fontSizeMm: 3, bold: false, rotation: 0, moduleWidth: 2, magnification: 4, thickness: 0.5 }] }); setSel(design.elements.length); };
  const save = async () => { if (!typeId) { setMsg('Choose an item type to save the design to.'); return; } try { await put(`/api/labels/designs/item-types/${typeId}`, design); setMsg('Saved — labels for this item type now use this design.'); } catch (e) { setError(e); } };
  const reset = async () => { if (typeId && confirm('Remove the custom design for this item type?')) { await del(`/api/labels/designs/item-types/${typeId}`); setDesign(await get<LabelDesign>('/api/labels/designs/default')); } };
  const e = sel != null ? design.elements[sel] : null;
  return (
    <div>
      <div className="topbar"><h1>Label designer</h1><div className="row"><select value={typeId} onChange={(ev) => setSp(ev.target.value ? { itemTypeId: ev.target.value } : {})}><option value="">(default design – not saved)</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}{t.hasLabelDesign ? ' ●' : ''}</option>)}</select><button onClick={reset} disabled={!typeId}>Reset</button><button className="primary" onClick={save} disabled={!typeId}>Save to item type</button></div></div>
      <p className="muted small">Design in millimetres; the compiler emits ZPL for the printer density (8 dpmm = 203 dpi, 12 = 300 dpi). Placeholders: {'{name} {identifier} {epc} {type} {location} {state} {lot} {expiry} {quantity} {date} {attributes.x}'}. RFID encoding writes the EPC to the tag when printed.</p>
      {msg && <div className="success" style={{ marginBottom: 12 }}>{msg}</div>}<ErrorBox error={error} />
      <div className="grid" style={{ gridTemplateColumns: '1fr 320px' }}>
        <div>
          <div className="panel" style={{ overflow: 'auto' }}>
            <div className="row" style={{ marginBottom: 8 }}><span className="muted small">Add:</span>{(['text', 'barcode', 'qr', 'box', 'line'] as const).map((t) => <button key={t} className="sm" onClick={() => add(t)}>+ {t}</button>)}</div>
            <svg width={design.widthMm * sc} height={design.heightMm * sc} style={{ background: '#fff', border: '1px solid var(--border)', borderRadius: 4 }} onClick={() => setSel(null)}>
              {design.elements.map((el, i) => {
                const x = el.x * sc, y = el.y * sc; const active = i === sel; const stroke = active ? 'var(--primary)' : 'transparent';
                const content = el.type === 'text' ? <text x={x} y={y + el.fontSizeMm * sc} fontSize={el.fontSizeMm * sc} fontWeight={el.bold ? 700 : 400} fontFamily="Helvetica, Arial, sans-serif" fill="#111" transform={el.rotation ? `rotate(${el.rotation} ${x} ${y})` : undefined}>{fill(el.text)}</text>
                  : el.type === 'barcode' ? <g><rect x={x} y={y} width={Math.min(design.widthMm - el.x, fill(el.text).length * 2.2 + 8) * sc} height={(el.height ?? 12) * sc} fill="url(#bars)" /><text x={x} y={y + ((el.height ?? 12) + 3) * sc} fontSize={2.5 * sc} fontFamily="monospace" fill="#111">{fill(el.text)}</text></g>
                  : el.type === 'qr' ? <g><rect x={x} y={y} width={el.magnification * 5 * sc} height={el.magnification * 5 * sc} fill="#111" /><rect x={x + 3} y={y + 3} width={el.magnification * 5 * sc - 6} height={el.magnification * 5 * sc - 6} fill="url(#qr)" /></g>
                  : el.type === 'box' ? <rect x={x} y={y} width={(el.width ?? 10) * sc} height={(el.height ?? 10) * sc} fill="none" stroke="#111" strokeWidth={el.thickness * sc} />
                  : <rect x={x} y={y} width={(el.width ?? 10) * sc} height={Math.max(1, (el.height ?? el.thickness) * sc)} fill="#111" />;
                return <g key={i} style={{ cursor: 'pointer' }} onClick={(ev) => { ev.stopPropagation(); setSel(i); }}>{content}<rect x={x - 2} y={y - 2} width={el.type === 'text' ? fill(el.text).length * el.fontSizeMm * 0.55 * sc + 4 : ((el.width ?? (el.type === 'qr' ? el.magnification * 5 : 30)) * sc) + 4} height={el.type === 'text' ? el.fontSizeMm * sc * 1.3 : ((el.height ?? (el.type === 'qr' ? el.magnification * 5 : 12)) * sc) + 4} fill="none" stroke={stroke} strokeDasharray="4 2" /></g>;
              })}
              <defs><pattern id="bars" width="6" height="10" patternUnits="userSpaceOnUse"><rect width="2" height="10" fill="#111" /><rect x="3" width="1" height="10" fill="#111" /></pattern><pattern id="qr" width="8" height="8" patternUnits="userSpaceOnUse"><rect width="4" height="4" fill="#fff" /><rect x="4" y="4" width="4" height="4" fill="#fff" /><rect x="4" width="4" height="4" fill="#111" /><rect y="4" width="4" height="4" fill="#111" /></pattern></defs>
            </svg>
            <div className="muted small" style={{ marginTop: 6 }}>{design.widthMm} × {design.heightMm} mm · approximate preview with sample data · click an element to edit</div>
          </div>
          <div className="panel" style={{ marginTop: 12 }}><h3>Compiled ZPL (sample item)</h3><pre className="mono small" style={{ whiteSpace: 'pre-wrap', maxHeight: 220, overflow: 'auto' }}>{zpl}</pre></div>
        </div>
        <div className="panel">
          <h3>Label</h3>
          <div className="form cols-2"><label>Width mm<input type="number" value={design.widthMm} onChange={(ev) => setDesign({ ...design, widthMm: Number(ev.target.value) })} /></label><label>Height mm<input type="number" value={design.heightMm} onChange={(ev) => setDesign({ ...design, heightMm: Number(ev.target.value) })} /></label>
            <label>Printer dpmm<select value={design.dpmm} onChange={(ev) => setDesign({ ...design, dpmm: Number(ev.target.value) })}><option value={6}>6 (152 dpi)</option><option value={8}>8 (203 dpi)</option><option value={12}>12 (300 dpi)</option><option value={24}>24 (600 dpi)</option></select></label>
            <label className="row" style={{ alignSelf: 'end' }}><input type="checkbox" style={{ width: 'auto' }} checked={design.encodeRfid} onChange={(ev) => setDesign({ ...design, encodeRfid: ev.target.checked })} /> Encode RFID</label></div>
          {e && sel != null && <>
            <h3 style={{ marginTop: 16 }}>Element · {e.type}</h3>
            <div className="form cols-2">
              <label>X mm<input type="number" step="0.5" value={e.x} onChange={(ev) => upd(sel, { x: Number(ev.target.value) })} /></label><label>Y mm<input type="number" step="0.5" value={e.y} onChange={(ev) => upd(sel, { y: Number(ev.target.value) })} /></label>
              {(e.type === 'text' || e.type === 'barcode' || e.type === 'qr') && <label style={{ gridColumn: '1 / -1' }}>Text / placeholder<input value={e.text ?? ''} onChange={(ev) => upd(sel, { text: ev.target.value })} /></label>}
              {e.type === 'text' && <><label>Font mm<input type="number" step="0.5" value={e.fontSizeMm} onChange={(ev) => upd(sel, { fontSizeMm: Number(ev.target.value) })} /></label><label className="row" style={{ alignSelf: 'end' }}><input type="checkbox" style={{ width: 'auto' }} checked={e.bold} onChange={(ev) => upd(sel, { bold: ev.target.checked })} /> Bold</label></>}
              {(e.type === 'box' || e.type === 'line') && <><label>Width mm<input type="number" value={e.width ?? 0} onChange={(ev) => upd(sel, { width: Number(ev.target.value) })} /></label><label>Thickness mm<input type="number" step="0.1" value={e.thickness} onChange={(ev) => upd(sel, { thickness: Number(ev.target.value) })} /></label></>}
              {(e.type === 'box' || e.type === 'barcode') && <label>Height mm<input type="number" value={e.height ?? 0} onChange={(ev) => upd(sel, { height: Number(ev.target.value) })} /></label>}
              {e.type === 'barcode' && <label>Module width (dots)<input type="number" min={1} max={10} value={e.moduleWidth} onChange={(ev) => upd(sel, { moduleWidth: Number(ev.target.value) })} /></label>}
              {e.type === 'qr' && <label>Magnification<input type="number" min={1} max={10} value={e.magnification} onChange={(ev) => upd(sel, { magnification: Number(ev.target.value) })} /></label>}
              <label>Rotation<select value={e.rotation} onChange={(ev) => upd(sel, { rotation: Number(ev.target.value) })}><option value={0}>0°</option><option value={90}>90°</option><option value={180}>180°</option><option value={270}>270°</option></select></label>
            </div>
            <div className="row" style={{ marginTop: 10 }}><button className="sm danger" onClick={() => { setDesign({ ...design, elements: design.elements.filter((_, j) => j !== sel) }); setSel(null); }}>Remove element</button></div>
          </>}
          {!e && <p className="muted small" style={{ marginTop: 12 }}>Select an element on the label to edit it.</p>}
        </div>
      </div>
    </div>
  );
}

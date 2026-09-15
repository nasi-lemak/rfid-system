import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { Circle, CircleMarker, MapContainer, Polygon, Polyline, Popup, TileLayer, Tooltip, useMap, useMapEvents } from 'react-leaflet';
import 'leaflet/dist/leaflet.css';
import { del, post, put } from '../api/client';
import { useGeoMap, useGpsTrack, useInvalidate, useItemTypes, useLocations, useLookups } from '../api/hooks';
import type { GeoFence, GeoPoint } from '../api/types';
import { useAuth } from '../auth';
import { Badge, ErrorBox, Modal, fmt, toneForSeverity } from '../components/ui';

const TILES = (import.meta.env.VITE_MAP_TILES as string | undefined) ?? 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
const ATTR = (import.meta.env.VITE_MAP_ATTRIBUTION as string | undefined) ?? '&copy; OpenStreetMap contributors';
type Draft = Partial<GeoFence> & { points: GeoPoint[] };
const blank = (): Draft => ({ name: '', kind: 'Circle', radiusM: 250, points: [], trigger: 'Both', severity: 'Warning', enabled: true });

function ClickCatcher({ onClick }: { onClick: (p: GeoPoint) => void }) { useMapEvents({ click: (e) => onClick({ lat: +e.latlng.lat.toFixed(6), lng: +e.latlng.lng.toFixed(6) }) }); return null; }
/** Leaflet only reads `center` at mount, so fit the view to the data once it arrives (and again when the user picks a track). */
function FitOnce({ points, focus }: { points: [number, number][]; focus?: [number, number][] }) {
  const map = useMap(); const [done, setDone] = useState(false);
  useEffect(() => { if (!done && points.length > 0) { map.fitBounds(points, { padding: [40, 40], maxZoom: 14 }); setDone(true); } }, [points, done, map]);
  useEffect(() => { if (focus && focus.length > 1) map.fitBounds(focus, { padding: [40, 40], maxZoom: 15 }); }, [focus, map]);
  return null;
}

export default function MapPage() {
  const { user } = useAuth(); const isAdmin = user?.role === 'Admin';
  const [hours, setHours] = useState(168);
  const map = useGeoMap(hours); const inv = useInvalidate();
  const locs = useLocations(); const itemTypes = useItemTypes(); const lookups = useLookups();
  const [selected, setSelected] = useState<string | null>(null); const [trackHours, setTrackHours] = useState(24);
  const track = useGpsTrack(selected ?? undefined, trackHours);
  const [draft, setDraft] = useState<Draft | null>(null); const [showForm, setShowForm] = useState(false); const [error, setError] = useState<unknown>(null);
  const items = map.data?.items ?? []; const fences = map.data?.fences ?? [];
  const allPoints = useMemo<[number, number][]>(() => [...items.map((i) => [i.lat, i.lng] as [number, number]), ...fences.filter((f) => f.centerLat != null).map((f) => [f.centerLat!, f.centerLng!] as [number, number]), ...fences.flatMap((f) => f.points.map((p) => [p.lat, p.lng] as [number, number]))], [items, fences]);
  const trackPoints = useMemo<[number, number][] | undefined>(() => track.data?.points.map((p) => [p.lat, p.lng] as [number, number]), [track.data]);
  const onMapClick = (p: GeoPoint) => { if (!draft) return; if (draft.kind === 'Circle') setDraft({ ...draft, centerLat: p.lat, centerLng: p.lng }); else setDraft({ ...draft, points: [...draft.points, p] }); };
  const save = async () => {
    if (!draft) return; setError(null);
    try { const body = { ...draft, points: draft.kind === 'Polygon' ? draft.points : [], locationId: draft.locationId || null, itemTypeId: draft.itemTypeId || null }; if (draft.id) await put(`/api/geo/fences/${draft.id}`, body); else await post('/api/geo/fences', body); inv('geo-map'); setDraft(null); setShowForm(false); } catch (e) { setError(e); }
  };
  const remove = async (f: GeoFence) => { if (!confirm(`Delete fence ${f.name}?`)) return; await del(`/api/geo/fences/${f.id}`); inv('geo-map'); };
  const sel = items.find((i) => i.itemId === selected);
  const colorFor = (f: GeoFence) => f.color || (f.severity === 'Critical' ? '#d13a2e' : f.severity === 'Warning' ? '#d97a06' : '#1d6fd1');
  return (
    <div>
      <div className="topbar"><h1>Map & geofences</h1><span className="muted small">GPS-tracked assets (vehicles, trailers, containers, tool crates) from telematics units and handhelds · fences raise enter/exit/dwell alerts and can move items to a location</span></div>
      <div className="row" style={{ marginBottom: 12 }}>
        <select value={hours} onChange={(e) => setHours(Number(e.target.value))} style={{ maxWidth: 160 }}>{[1, 24, 168, 720].map((h) => <option key={h} value={h}>seen in last {h < 24 ? `${h} h` : `${h / 24} d`}</option>)}</select>
        {isAdmin && !draft && <button className="primary" onClick={() => { setDraft(blank()); setShowForm(true); }}>+ New geofence</button>}
        {draft && <><Badge tone="info">{draft.kind === 'Circle' ? 'Click the map to set the centre' : `Click the map to add corners (${draft.points.length})`}</Badge><button onClick={() => setShowForm(true)}>Details…</button><button onClick={() => { setDraft(null); setShowForm(false); }}>Cancel</button><button className="primary" onClick={save}>Save fence</button></>}
        <span className="muted small">{items.length} tracked · {fences.length} fences</span>
      </div>
      <ErrorBox error={error ?? map.error} />
      <div className="grid" style={{ gridTemplateColumns: '1fr 320px' }}>
        <div className="panel" style={{ padding: 0, overflow: 'hidden' }}>
          <MapContainer center={[51.5, -0.12]} zoom={6} style={{ height: 620, width: '100%' }} scrollWheelZoom>
            <FitOnce points={allPoints} focus={trackPoints} />
            <TileLayer url={TILES} attribution={ATTR} />
            <ClickCatcher onClick={onMapClick} />
            {fences.map((f) => f.kind === 'Circle' && f.centerLat != null ? <Circle key={f.id} center={[f.centerLat, f.centerLng!]} radius={f.radiusM ?? 100} pathOptions={{ color: colorFor(f), weight: 2, fillOpacity: f.enabled ? 0.12 : 0.03, dashArray: f.enabled ? undefined : '6 4' }}><Tooltip>{f.name} · {f.inside} inside</Tooltip></Circle>
              : f.points.length >= 3 ? <Polygon key={f.id} positions={f.points.map((p) => [p.lat, p.lng] as [number, number])} pathOptions={{ color: colorFor(f), weight: 2, fillOpacity: f.enabled ? 0.12 : 0.03 }}><Tooltip>{f.name} · {f.inside} inside</Tooltip></Polygon> : null)}
            {draft?.kind === 'Circle' && draft.centerLat != null && <Circle center={[draft.centerLat, draft.centerLng!]} radius={draft.radiusM ?? 100} pathOptions={{ color: '#1d6fd1', dashArray: '4 4' }} />}
            {draft?.kind === 'Polygon' && draft.points.length >= 2 && (draft.points.length >= 3 ? <Polygon positions={draft.points.map((p) => [p.lat, p.lng] as [number, number])} pathOptions={{ color: '#1d6fd1', dashArray: '4 4' }} /> : <Polyline positions={draft.points.map((p) => [p.lat, p.lng] as [number, number])} pathOptions={{ color: '#1d6fd1', dashArray: '4 4' }} />)}
            {track.data && track.data.points.length > 1 && <Polyline positions={track.data.points.map((p) => [p.lat, p.lng] as [number, number])} pathOptions={{ color: '#1d6fd1', weight: 3, opacity: 0.8 }} />}
            {items.map((i) => <CircleMarker key={i.itemId} center={[i.lat, i.lng]} radius={i.itemId === selected ? 9 : 6} pathOptions={{ color: '#fff', weight: 2, fillColor: i.itemId === selected ? '#1e8e5a' : '#1d6fd1', fillOpacity: 1 }} eventHandlers={{ click: () => setSelected(i.itemId) }}>
              <Popup><b>{i.name}</b><br />{i.identifier} · {i.itemType}<br />{i.location ? `at ${i.location} · ` : ''}{i.speedKph != null ? `${i.speedKph.toFixed(0)} km/h · ` : ''}{fmt.ago(i.at)}<br />{i.fences.length > 0 ? `inside: ${i.fences.join(', ')}` : 'outside all fences'}<br /><Link to={`/items/${i.itemId}`}>Open item</Link></Popup>
            </CircleMarker>)}
          </MapContainer>
        </div>
        <div>
          <div className="panel" style={{ marginBottom: 16 }}>
            <h3>Tracked assets</h3>
            <div style={{ maxHeight: 260, overflow: 'auto' }}>{items.map((i) => <div key={i.itemId} onClick={() => setSelected(i.itemId)} style={{ padding: '6px 0', borderBottom: '1px solid var(--border)', cursor: 'pointer', fontWeight: i.itemId === selected ? 600 : 400 }}>{i.name}<div className="muted small">{i.itemType} · {fmt.ago(i.at)}{i.fences.length ? ` · ${i.fences.join(', ')}` : ''}</div></div>)}{items.length === 0 && <span className="muted small">No GPS positions yet. Post to <code>POST /api/ingest/gps</code> from a telematics device or the handheld.</span>}</div>
            {sel && <div style={{ marginTop: 8 }}><div className="row"><span className="small">Track</span><select value={trackHours} onChange={(e) => setTrackHours(Number(e.target.value))} style={{ maxWidth: 110 }}>{[1, 6, 24, 72, 168].map((h) => <option key={h} value={h}>{h < 24 ? `${h} h` : `${h / 24} d`}</option>)}</select><span className="muted small">{track.data?.points.length ?? 0} fixes</span><button className="sm" onClick={() => setSelected(null)}>Clear</button></div></div>}
          </div>
          <div className="panel">
            <h3>Geofences</h3>
            {fences.map((f) => <div key={f.id} style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><div className="row" style={{ justifyContent: 'space-between' }}><span><span style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 2, background: colorFor(f), marginRight: 6 }} />{f.name} {!f.enabled && <Badge>off</Badge>}</span><span className="row" style={{ gap: 4 }}><Badge tone={toneForSeverity(f.severity)}>{f.trigger}</Badge><Badge>{f.inside} in</Badge>{isAdmin && <><button className="sm" onClick={() => { setDraft({ ...f, points: f.points ?? [] }); setShowForm(true); }}>Edit</button><button className="sm danger" onClick={() => remove(f)}>✕</button></>}</span></div><div className="muted small">{f.kind === 'Circle' ? `${f.radiusM} m radius` : `${f.points.length} corners`}{f.location ? ` · → ${f.location}` : ''}{f.maxDwellMinutes ? ` · dwell ≤ ${f.maxDwellMinutes} min` : ''}</div></div>)}
            {fences.length === 0 && <span className="muted small">No fences yet.</span>}
          </div>
        </div>
      </div>
      {draft && showForm && <Modal title={draft.id ? 'Edit geofence' : 'New geofence'} onClose={() => setShowForm(false)}>
        <form className="form" onSubmit={(e) => { e.preventDefault(); setShowForm(false); }}>
          <div className="form cols-2">
            <label>Name *<input required value={draft.name ?? ''} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></label>
            <label>Shape<select value={draft.kind} onChange={(e) => setDraft({ ...draft, kind: e.target.value as GeoFence['kind'] })}><option>Circle</option><option>Polygon</option></select></label>
            {draft.kind === 'Circle' && <><label>Centre lat<input type="number" step="0.000001" value={draft.centerLat ?? ''} onChange={(e) => setDraft({ ...draft, centerLat: Number(e.target.value) })} /></label><label>Centre lng<input type="number" step="0.000001" value={draft.centerLng ?? ''} onChange={(e) => setDraft({ ...draft, centerLng: Number(e.target.value) })} /></label><label>Radius (m)<input type="number" min={5} value={draft.radiusM ?? 250} onChange={(e) => setDraft({ ...draft, radiusM: Number(e.target.value) })} /></label></>}
            {draft.kind === 'Polygon' && <label style={{ gridColumn: '1 / -1' }}>Corners (click the map; last corner removed with the button)<div className="row"><span className="small mono">{draft.points.map((p) => `${p.lat},${p.lng}`).join(' · ') || 'none yet'}</span><button type="button" className="sm" onClick={() => setDraft({ ...draft, points: draft.points.slice(0, -1) })}>Undo corner</button></div></label>}
            <label>Trigger alerts on<select value={draft.trigger} onChange={(e) => setDraft({ ...draft, trigger: e.target.value as GeoFence['trigger'] })}><option>Enter</option><option>Exit</option><option>Both</option></select></label>
            <label>Severity<select value={draft.severity} onChange={(e) => setDraft({ ...draft, severity: e.target.value })}>{lookups.data?.severities.map((s) => <option key={s}>{s}</option>)}</select></label>
            <label>Move items inside to location<select value={draft.locationId ?? ''} onChange={(e) => setDraft({ ...draft, locationId: e.target.value || null })}><option value="">— none —</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name} ({l.kind})</option>)}</select></label>
            <label>Only item type<select value={draft.itemTypeId ?? ''} onChange={(e) => setDraft({ ...draft, itemTypeId: e.target.value || null })}><option value="">any</option>{itemTypes.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
            <label>Max dwell (minutes, blank = none)<input type="number" min={1} value={draft.maxDwellMinutes ?? ''} onChange={(e) => setDraft({ ...draft, maxDwellMinutes: e.target.value ? Number(e.target.value) : null })} /></label>
            <label>Colour<input type="color" value={draft.color ?? '#d97a06'} onChange={(e) => setDraft({ ...draft, color: e.target.value })} /></label>
          </div>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={draft.enabled !== false} onChange={(e) => setDraft({ ...draft, enabled: e.target.checked })} /> Enabled</label>
          <div className="row end"><button type="button" onClick={() => setShowForm(false)}>Back to map</button><button type="button" className="primary" onClick={save}>Save fence</button></div>
        </form>
      </Modal>}
    </div>
  );
}

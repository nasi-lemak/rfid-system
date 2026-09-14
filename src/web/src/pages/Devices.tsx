import { useState, type FormEvent } from 'react';
import { post } from '../api/client';
import { useDevices, useLocations, useLookups, useRemove, useSave } from '../api/hooks';
import type { Antenna, Device } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

export default function Devices() {
  const { data, refetch } = useDevices();
  const locs = useLocations();
  const lookups = useLookups();
  const save = useSave<Device>('/api/devices', ['devices']);
  const remove = useRemove('/api/devices', ['devices']);
  const [edit, setEdit] = useState<Partial<Device> | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const ants = edit?.antennas ?? [];
  const setAnt = (i: number, a: Partial<Antenna>) => setEdit({ ...edit!, antennas: ants.map((x, j) => (j === i ? { ...x, ...a } : x)) });
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: { ...edit, config: edit.config ?? {}, antennas: ants.map((a) => ({ ...a, locationId: a.locationId || null })) } }); setEdit(null); };
  const rotate = async (id: string) => { const r = await post<{ token: string }>(`/api/devices/${id}/token`); setToken(r.token); refetch(); };
  return (
    <div>
      <div className="topbar"><h1>Readers & devices</h1><button className="primary" onClick={() => setEdit({ name: '', kind: 'Fixed', antennas: [{ port: 1, direction: 'None' }] })}>+ New device</button></div>
      <p className="muted small">Handhelds, fixed readers, portals, gates, smart cabinets/shelves/lockers. Map each antenna to a location and (for portals) a direction so reads become zone in/out movements. Devices authenticate with a provisioning token.</p>
      <div className="grid cols-2">
        {data?.map((d) => <div className="panel" key={d.id}>
          <div className="topbar"><div><h2 style={{ marginBottom: 2 }}>{d.name}</h2><span className="muted small">{d.kind} · {d.model ?? ''} {d.serialNumber ?? ''} · last seen {fmt.ago(d.lastSeenAt)}</span></div><div className="row"><button className="sm" onClick={() => setEdit(d)}>Edit</button><button className="sm" onClick={() => rotate(d.id)}>{d.hasToken ? 'Rotate token' : 'Issue token'}</button><button className="sm danger" onClick={() => confirm('Delete device?') && remove.mutate(d.id)}>Delete</button></div></div>
          {d.llrp && <div className="small" style={{ marginBottom: 6 }}><Badge tone="info">LLRP</Badge> {d.llrp.host}:{d.llrp.port}</div>}{typeof d.config.mqttTopic === 'string' && <div className="small" style={{ marginBottom: 6 }}><Badge tone="info">MQTT</Badge> {String(d.config.mqttTopic)}</div>}{d.kind === 'Printer' && <div className="small" style={{ marginBottom: 6 }}><Badge tone="info">Printer</Badge> {String(d.config.host ?? '?')}:{String(d.config.port ?? 9100)}</div>}
          <table><thead><tr><th>Port</th><th>Location</th><th>Direction</th><th>Power</th><th>Anchor x,y</th></tr></thead><tbody>{d.antennas.map((a) => <tr key={a.port}><td>{a.port}</td><td>{a.locationName ?? <span className="muted">unmapped</span>}</td><td>{a.direction !== 'None' ? <Badge tone={a.direction === 'In' ? 'ok' : 'warn'}>{a.direction}</Badge> : <span className="muted">presence</span>}</td><td>{a.powerDbm != null ? `${a.powerDbm} dBm` : ''}</td><td className="small">{a.x != null ? `${a.x}, ${a.y} m` : ''}</td></tr>)}</tbody></table>
          {d.antennas.length === 0 && <span className="muted small">No antennas (handheld)</span>}
        </div>)}
      </div>
      {token && <Modal title="Device token (shown once)" onClose={() => setToken(null)}><p>Configure the reader / handheld with this token. It exchanges it for a JWT at <code>POST /api/auth/device</code>.</p><input className="mono" readOnly value={token} onFocus={(e) => e.target.select()} /></Modal>}
      {edit && <Modal title={edit.id ? 'Edit device' : 'New device'} onClose={() => setEdit(null)} width={720}>
        <form className="form" onSubmit={submit}>
          <div className="form cols-2">
            <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Kind<select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value })}>{lookups.data?.deviceKinds.map((k) => <option key={k}>{k}</option>)}</select></label>
            <label>Model<input value={edit.model ?? ''} onChange={(e) => setEdit({ ...edit, model: e.target.value })} placeholder="Zebra FX9600, Impinj R700, Chainway C72…" /></label>
            <label>Serial number<input value={edit.serialNumber ?? ''} onChange={(e) => setEdit({ ...edit, serialNumber: e.target.value })} /></label>
            <label>Site<select value={edit.siteLocationId ?? ''} onChange={(e) => setEdit({ ...edit, siteLocationId: e.target.value || null })}><option value="">—</option>{locs.data?.filter((l) => l.kind === 'Site').map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
            {edit.kind !== 'Handheld' && edit.kind !== 'Printer' && <><label>LLRP host (native LLRP client)<input value={String(edit.config?.llrpHost ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpHost: e.target.value || undefined } })} placeholder="192.168.1.50 (port 5084)" /></label><label>MQTT topic (broker ingestion)<input value={String(edit.config?.mqttTopic ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, mqttTopic: e.target.value || undefined } })} placeholder="rfid/site1/dock1 or impinj/+" /></label></>}
            {edit.kind === 'Printer' && <><label>Printer host<input value={String(edit.config?.host ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, host: e.target.value } })} placeholder="192.168.1.80" /></label><label>Port<input type="number" value={Number(edit.config?.port ?? 9100)} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, port: Number(e.target.value) } })} /></label></>}
          </div>
          <div><h3>Antennas</h3>
            {ants.map((a, i) => <div className="row" key={i} style={{ marginBottom: 6 }}>
              <input type="number" style={{ width: 70 }} value={a.port} onChange={(e) => setAnt(i, { port: Number(e.target.value) })} />
              <select style={{ flex: 2 }} value={a.locationId ?? ''} onChange={(e) => setAnt(i, { locationId: e.target.value || null })}><option value="">unmapped</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name}</option>)}</select>
              <select style={{ flex: 1 }} value={a.direction} onChange={(e) => setAnt(i, { direction: e.target.value as Antenna['direction'] })}><option value="None">Presence</option><option value="In">In</option><option value="Out">Out</option></select>
              <input type="number" step="0.1" style={{ width: 80 }} placeholder="dBm" value={a.powerDbm ?? ''} onChange={(e) => setAnt(i, { powerDbm: e.target.value ? Number(e.target.value) : null })} />
              <input type="number" step="0.1" style={{ width: 70 }} placeholder="x m" title="Anchor x (m) for trilateration" value={a.x ?? ''} onChange={(e) => setAnt(i, { x: e.target.value ? Number(e.target.value) : null })} />
              <input type="number" step="0.1" style={{ width: 70 }} placeholder="y m" title="Anchor y (m)" value={a.y ?? ''} onChange={(e) => setAnt(i, { y: e.target.value ? Number(e.target.value) : null })} />
              <button type="button" className="sm danger" onClick={() => setEdit({ ...edit, antennas: ants.filter((_, j) => j !== i) })}>✕</button>
            </div>)}
            <button type="button" className="sm" onClick={() => setEdit({ ...edit, antennas: [...ants, { port: (ants.at(-1)?.port ?? 0) + 1, direction: 'None' }] })}>+ Add antenna</button>
          </div>
          <ErrorBox error={save.error} />
          <div className="row end"><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

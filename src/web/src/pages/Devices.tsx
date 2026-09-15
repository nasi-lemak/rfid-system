import { useState, type FormEvent } from 'react';
import { post } from '../api/client';
import { useDeviceHealth, useDevices, useFirmwareReleases, useFirmwareRollouts, useInvalidate, useLlrpStatus, useLocations, useLookups, useRemove, useSave } from '../api/hooks';
import { put } from '../api/client';
import type { FirmwareRelease } from '../api/types';
import type { Antenna, Device } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

const healthTone = (h?: string): 'ok' | 'warn' | 'crit' | 'info' | '' => h === 'Online' ? 'ok' : h === 'Degraded' ? 'warn' : h === 'Offline' ? 'crit' : h === 'Updating' ? 'info' : '';

export default function Devices() {
  const { data, refetch } = useDevices();
  const locs = useLocations();
  const lookups = useLookups();
  const save = useSave<Device>('/api/devices', ['devices']);
  const remove = useRemove('/api/devices', ['devices']);
  const [edit, setEdit] = useState<Partial<Device> | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [llrpId, setLlrpId] = useState<string | null>(null);
  const llrp = useLlrpStatus(llrpId ?? undefined);
  const [gpo, setGpo] = useState({ port: 1, state: true });
  const [tab, setTab] = useState<'devices' | 'health' | 'firmware'>('devices');
  const [days, setDays] = useState(7); const health = useDeviceHealth(days);
  const releases = useFirmwareReleases(); const rollouts = useFirmwareRollouts(); const inv = useInvalidate();
  const [rel, setRel] = useState<Partial<FirmwareRelease> | null>(null); const [rollout, setRollout] = useState<{ release: FirmwareRelease; deviceIds: string[] } | null>(null);
  const [sla, setSla] = useState<{ id: string; name: string; heartbeatSlaMinutes: number | ''; trackedItemId: string } | null>(null);
  const saveRel = useSave<FirmwareRelease>('/api/firmware/releases', ['firmware-releases']); const removeRel = useRemove('/api/firmware/releases', ['firmware-releases']);
  const ants = edit?.antennas ?? [];
  const setAnt = (i: number, a: Partial<Antenna>) => setEdit({ ...edit!, antennas: ants.map((x, j) => (j === i ? { ...x, ...a } : x)) });
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: { ...edit, config: edit.config ?? {}, antennas: ants.map((a) => ({ ...a, locationId: a.locationId || null })) } }); setEdit(null); };
  const rotate = async (id: string) => { const r = await post<{ token: string }>(`/api/devices/${id}/token`); setToken(r.token); refetch(); };
  return (
    <div>
      <div className="topbar"><h1>Readers & devices</h1><button className="primary" onClick={() => setEdit({ name: '', kind: 'Fixed', antennas: [{ port: 1, direction: 'None' }] })}>+ New device</button></div>
      <p className="muted small">Handhelds, fixed readers, portals, gates, smart cabinets/shelves/lockers. Map each antenna to a location and (for portals) a direction so reads become zone in/out movements. Devices authenticate with a provisioning token.</p>
      <div className="tabs">{(['devices', 'health', 'firmware'] as const).map((t) => <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t === 'devices' ? 'Devices' : t === 'health' ? 'Health & SLAs' : 'Firmware'}</button>)}</div>
      {tab === 'health' && <>
        <div className="row" style={{ marginBottom: 12 }}><select value={days} onChange={(e) => setDays(Number(e.target.value))} style={{ maxWidth: 140 }}>{[1, 7, 30, 90].map((d) => <option key={d} value={d}>last {d} d</option>)}</select><span className="muted small">Health = time since the last heartbeat or read vs. the device's SLA: Online ≤ SLA · Degraded ≤ 2×SLA · Offline beyond (raises a critical alert; cleared when it returns). Uptime ≈ heartbeats received ÷ expected.</span></div>
        <div className="grid cols-4" style={{ marginBottom: 16 }}>{(['Online', 'Degraded', 'Offline', 'Unknown'] as const).map((k) => <div key={k} className={'panel stat ' + (k === 'Offline' && (health.data?.summary[k] ?? 0) > 0 ? 'crit' : k === 'Degraded' && (health.data?.summary[k] ?? 0) > 0 ? 'warn' : k === 'Online' ? 'ok' : '')}><span className="label">{k}</span><span className="value">{health.data?.summary[k] ?? 0}</span></div>)}</div>
        <div className="panel table-wrap"><table><thead><tr><th>Device</th><th>Health</th><th>Uptime</th><th>Heartbeats</th><th>SLA</th><th>Firmware</th><th>Last heartbeat</th><th></th></tr></thead>
          <tbody>{health.data?.rows.map((r) => <tr key={r.deviceId}><td>{r.name}</td><td><Badge tone={healthTone(r.health)}>{r.health}</Badge></td><td><div className="bar" style={{ gridTemplateColumns: '1fr 48px' }}><div className="track"><div className="fill" style={{ width: `${r.uptimePercent}%`, background: r.uptimePercent < 90 ? 'var(--crit)' : r.uptimePercent < 99 ? 'var(--warn)' : 'var(--ok)' }} /></div><span className="right small">{r.uptimePercent}%</span></div></td><td className="small">{r.heartbeats} / {r.expected}</td><td className="small">{r.slaMinutes} min</td><td className="mono small">{r.firmwareVersion ?? '—'}</td><td className="small muted">{fmt.ago(r.lastHeartbeatAt)}</td><td className="right"><button className="sm" onClick={() => { const d = data?.find((x) => x.id === r.deviceId); setSla({ id: r.deviceId, name: r.name, heartbeatSlaMinutes: d?.heartbeatSlaMinutes ?? '', trackedItemId: d?.trackedItemId ?? '' }); }}>SLA</button></td></tr>)}</tbody></table></div>
      </>}
      {tab === 'firmware' && <>
        <div className="topbar"><span className="muted small">Publish a firmware image per vendor/model, then roll it out. Devices poll <code>GET /api/devices/{'{id}'}/firmware/pending</code> and report progress to <code>POST /api/firmware/rollouts/{'{id}'}/report</code>; LLRP readers report their running version automatically.</span><button className="primary" onClick={() => setRel({ vendor: '', model: '', version: '', isActive: true })}>+ New release</button></div>
        <div className="grid cols-2">
          <div className="panel table-wrap"><h3>Releases</h3><table><thead><tr><th>Vendor / model</th><th>Version</th><th>On version</th><th>Rollouts</th><th></th></tr></thead>
            <tbody>{releases.data?.map((r) => <tr key={r.id}><td>{r.vendor} {r.model}<div className="muted small">{r.notes}</div></td><td className="mono">{r.version}{!r.isActive && <Badge>inactive</Badge>}</td><td>{r.devicesOnVersion}</td><td className="small">{Object.entries(r.rollouts).map(([k, v]) => <Badge key={k} tone={k === 'Done' ? 'ok' : k === 'Failed' ? 'crit' : k === 'Pending' || k === 'Sent' ? 'info' : ''}>{k} {v}</Badge>)}</td><td className="right"><button className="sm primary" onClick={() => setRollout({ release: r, deviceIds: [] })}>Roll out</button> <button className="sm" onClick={() => setRel(r)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete release?') && removeRel.mutate(r.id)}>✕</button></td></tr>)}
              {releases.data?.length === 0 && <tr><td colSpan={5} className="muted">No firmware releases yet.</td></tr>}</tbody></table></div>
          <div className="panel table-wrap"><h3>Rollouts</h3><table><thead><tr><th>Device</th><th>Version</th><th>Status</th><th>When</th><th></th></tr></thead>
            <tbody>{rollouts.data?.map((r) => <tr key={r.id}><td>{r.device}</td><td className="mono small">{r.fromVersion ?? '?'} → {r.version}</td><td><Badge tone={r.status === 'Done' ? 'ok' : r.status === 'Failed' ? 'crit' : r.status === 'Cancelled' ? '' : 'info'}>{r.status}</Badge>{r.error && <div className="small" style={{ color: 'var(--crit)' }}>{r.error}</div>}</td><td className="small muted">{fmt.ago(r.completedAt ?? r.startedAt ?? r.scheduledAt)}</td><td className="right">{(r.status === 'Pending' || r.status === 'Sent') && <button className="sm" onClick={() => post(`/api/firmware/rollouts/${r.id}/cancel`).then(() => inv('firmware-rollouts', 'firmware-releases'))}>Cancel</button>}{r.status === 'Sent' && <> <button className="sm" title="Simulate the device finishing the update" onClick={() => post(`/api/firmware/rollouts/${r.id}/report`, { status: 'Done' }).then(() => inv('firmware-rollouts', 'firmware-releases', 'devices', 'device-health'))}>Mark done</button></>}</td></tr>)}
              {rollouts.data?.length === 0 && <tr><td colSpan={5} className="muted">No rollouts yet.</td></tr>}</tbody></table></div>
        </div>
      </>}
      {rel && <Modal title={rel.id ? 'Edit release' : 'New firmware release'} onClose={() => setRel(null)}>
        <form className="form" onSubmit={async (e) => { e.preventDefault(); await saveRel.mutateAsync({ id: rel.id, body: rel }); setRel(null); }}>
          <div className="form cols-2"><label>Vendor *<input required value={rel.vendor ?? ''} onChange={(e) => setRel({ ...rel, vendor: e.target.value })} placeholder="Impinj" /></label><label>Model *<input required value={rel.model ?? ''} onChange={(e) => setRel({ ...rel, model: e.target.value })} placeholder="R700" /></label>
            <label>Version *<input required value={rel.version ?? ''} onChange={(e) => setRel({ ...rel, version: e.target.value })} placeholder="8.1.0" /></label><label>Checksum (sha256)<input value={rel.checksum ?? ''} onChange={(e) => setRel({ ...rel, checksum: e.target.value })} /></label></div>
          <label>Image URL<input value={rel.url ?? ''} onChange={(e) => setRel({ ...rel, url: e.target.value })} placeholder="https://files.example.com/fw/r700-8.1.0.upg" /></label>
          <label>Release notes<textarea rows={3} value={rel.notes ?? ''} onChange={(e) => setRel({ ...rel, notes: e.target.value })} /></label>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={rel.isActive !== false} onChange={(e) => setRel({ ...rel, isActive: e.target.checked })} /> Active</label>
          <ErrorBox error={saveRel.error} /><div className="row end"><button type="button" onClick={() => setRel(null)}>Cancel</button><button className="primary">Save</button></div>
        </form></Modal>}
      {rollout && <Modal title={`Roll out ${rollout.release.vendor} ${rollout.release.model} ${rollout.release.version}`} onClose={() => setRollout(null)}>
        <p className="muted small">Pick the devices. Devices already on this version are skipped; pending rollouts for the same device are replaced.</p>
        <div className="form">{data?.filter((d) => d.kind !== 'Handheld' && d.kind !== 'Printer').map((d) => <label key={d.id} className="row"><input type="checkbox" style={{ width: 'auto' }} checked={rollout.deviceIds.includes(d.id)} onChange={(e) => setRollout({ ...rollout, deviceIds: e.target.checked ? [...rollout.deviceIds, d.id] : rollout.deviceIds.filter((x) => x !== d.id) })} />{d.name} <span className="muted small">{d.model ?? ''} · fw {d.firmwareVersion ?? '?'}</span></label>)}
          <div className="row end"><button type="button" onClick={() => setRollout(null)}>Cancel</button><button className="primary" disabled={rollout.deviceIds.length === 0} onClick={async () => { await post(`/api/firmware/releases/${rollout.release.id}/rollout`, { deviceIds: rollout.deviceIds }); inv('firmware-rollouts', 'firmware-releases'); setRollout(null); setTab('firmware'); }}>Start rollout</button></div></div></Modal>}
      {sla && <Modal title={`Health SLA · ${sla.name}`} onClose={() => setSla(null)}>
        <form className="form" onSubmit={async (e) => { e.preventDefault(); await put(`/api/devices/${sla.id}/sla`, { heartbeatSlaMinutes: sla.heartbeatSlaMinutes === '' ? null : sla.heartbeatSlaMinutes, trackedItemId: sla.trackedItemId || null }); inv('device-health', 'devices'); setSla(null); }}>
          <label>Expected heartbeat / read interval (minutes; blank = default 15, handhelds & printers 1440)<input type="number" min={1} value={sla.heartbeatSlaMinutes} onChange={(e) => setSla({ ...sla, heartbeatSlaMinutes: e.target.value ? Number(e.target.value) : '' })} /></label>
          <label>Tracked item id (telematics units: the vehicle/container this device reports GPS for)<input className="mono" value={sla.trackedItemId} onChange={(e) => setSla({ ...sla, trackedItemId: e.target.value })} placeholder="item GUID" /></label>
          <div className="row end"><button type="button" onClick={() => setSla(null)}>Cancel</button><button className="primary">Save</button></div></form></Modal>}
      {tab === 'devices' && <div className="grid cols-2">
        {data?.map((d) => <div className="panel" key={d.id}>
          <div className="topbar"><div><h2 style={{ marginBottom: 2 }}>{d.name}</h2><span className="muted small">{d.kind} · {d.model ?? ''} {d.serialNumber ?? ''} · last seen {fmt.ago(d.lastSeenAt)}{d.firmwareVersion ? ` · fw ${d.firmwareVersion}` : ''}</span> <Badge tone={healthTone(d.health)}>{d.health ?? 'Unknown'}</Badge></div><div className="row"><button className="sm" onClick={() => setEdit(d)}>Edit</button><button className="sm" onClick={() => rotate(d.id)}>{d.hasToken ? 'Rotate token' : 'Issue token'}</button><button className="sm danger" onClick={() => confirm('Delete device?') && remove.mutate(d.id)}>Delete</button></div></div>
          {d.llrp && <div className="small" style={{ marginBottom: 6 }}><Badge tone="info">LLRP</Badge> {d.llrp.host}:{d.llrp.port} <button className="sm" onClick={() => setLlrpId(d.id)}>Status / GPIO</button></div>}{typeof d.config.mqttTopic === 'string' && <div className="small" style={{ marginBottom: 6 }}><Badge tone="info">MQTT</Badge> {String(d.config.mqttTopic)}</div>}{d.kind === 'Printer' && <div className="small" style={{ marginBottom: 6 }}><Badge tone="info">Printer</Badge> {String(d.config.host ?? '?')}:{String(d.config.port ?? 9100)}</div>}
          <table><thead><tr><th>Port</th><th>Location</th><th>Direction</th><th>Power</th><th>Anchor x,y</th></tr></thead><tbody>{d.antennas.map((a) => <tr key={a.port}><td>{a.port}</td><td>{a.locationName ?? <span className="muted">unmapped</span>}</td><td>{a.direction !== 'None' ? <Badge tone={a.direction === 'In' ? 'ok' : 'warn'}>{a.direction}</Badge> : <span className="muted">presence</span>}</td><td>{a.powerDbm != null ? `${a.powerDbm} dBm` : ''}</td><td className="small">{a.x != null ? `${a.x}, ${a.y} m` : ''}</td></tr>)}</tbody></table>
          {d.antennas.length === 0 && <span className="muted small">No antennas (handheld)</span>}
        </div>)}
      </div>}
      {llrpId && <Modal title={`LLRP · ${llrp.data?.device ?? ''}`} onClose={() => setLlrpId(null)}>
        {llrp.data ? <>
          <dl className="kv"><dt>Endpoint</dt><dd>{llrp.data.endpoint ? `${llrp.data.endpoint.host}:${llrp.data.endpoint.port}` : '—'}</dd><dt>Connected</dt><dd>{llrp.data.connected ? <Badge tone="ok">yes · since {fmt.ago(llrp.data.connectedAt)}</Badge> : <Badge tone="warn">no (retrying)</Badge>}</dd><dt>Tags received</dt><dd>{llrp.data.tagsReceived ?? 0}</dd>
            <dt>Options</dt><dd className="small">power {llrp.data.options.transmitPowerDbm ?? 'max'} dBm · session S{llrp.data.options.session} · population {llrp.data.options.tagPopulation} · antennas {llrp.data.options.antennaIds?.join(',') || 'all'} · {llrp.data.options.gpiStartPort ? `GPI ${llrp.data.options.gpiStartPort} triggered` : 'continuous'}</dd>
            {llrp.data.capabilities && <><dt>Reader</dt><dd>{llrp.data.capabilities.manufacturer} model {llrp.data.capabilities.modelId} · fw {llrp.data.capabilities.firmware} · {llrp.data.capabilities.maxAntennas} antennas · {llrp.data.capabilities.gpis} GPI / {llrp.data.capabilities.gpos} GPO</dd><dt>Power table</dt><dd className="small">{llrp.data.capabilities.powerTable.map((p) => p.dbm).slice(0, 12).join(', ')}{llrp.data.capabilities.powerTable.length > 12 ? '…' : ''} dBm</dd></>}</dl>
          <h3 style={{ marginTop: 12 }}>GPO (stack light / buzzer)</h3>
          <div className="row"><input type="number" style={{ width: 70 }} value={gpo.port} onChange={(e) => setGpo({ ...gpo, port: Number(e.target.value) })} /><select value={String(gpo.state)} onChange={(e) => setGpo({ ...gpo, state: e.target.value === 'true' })} style={{ width: 100 }}><option value="true">On</option><option value="false">Off</option></select><button className="sm" disabled={!llrp.data.connected} onClick={() => post(`/api/devices/${llrpId}/llrp/gpo`, gpo)}>Set</button><button className="sm" onClick={() => post(`/api/devices/${llrpId}/llrp/reconnect`).then(() => llrp.refetch())}>Reconnect</button></div>
        </> : <span className="muted">Loading…</span>}
      </Modal>}
      {token && <Modal title="Device token (shown once)" onClose={() => setToken(null)}><p>Configure the reader / handheld with this token. It exchanges it for a JWT at <code>POST /api/auth/device</code>.</p><input className="mono" readOnly value={token} onFocus={(e) => e.target.select()} /></Modal>}
      {edit && <Modal title={edit.id ? 'Edit device' : 'New device'} onClose={() => setEdit(null)} width={720}>
        <form className="form" onSubmit={submit}>
          <div className="form cols-2">
            <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Kind<select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value })}>{lookups.data?.deviceKinds.map((k) => <option key={k}>{k}</option>)}</select></label>
            <label>Model<input value={edit.model ?? ''} onChange={(e) => setEdit({ ...edit, model: e.target.value })} placeholder="Zebra FX9600, Impinj R700, Chainway C72…" /></label>
            <label>Serial number<input value={edit.serialNumber ?? ''} onChange={(e) => setEdit({ ...edit, serialNumber: e.target.value })} /></label>
            <label>Site<select value={edit.siteLocationId ?? ''} onChange={(e) => setEdit({ ...edit, siteLocationId: e.target.value || null })}><option value="">—</option>{locs.data?.filter((l) => l.kind === 'Site').map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
            {edit.kind !== 'Handheld' && edit.kind !== 'Printer' && <><label>LLRP host (native LLRP client)<input value={String(edit.config?.llrpHost ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpHost: e.target.value || undefined } })} placeholder="192.168.1.50 (port 5084)" /></label>
            {!!edit.config?.llrpHost && <><label>Transmit power dBm<input type="number" step="0.25" value={String(edit.config?.llrpPower ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpPower: e.target.value ? Number(e.target.value) : undefined } })} placeholder="reader max" /></label><label>Gen2 session<select value={String(edit.config?.llrpSession ?? 1)} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpSession: Number(e.target.value) } })}><option value="0">S0 (fast re-read)</option><option value="1">S1</option><option value="2">S2 (dense population)</option><option value="3">S3</option></select></label><label>Tag population<input type="number" value={String(edit.config?.llrpTagPopulation ?? 32)} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpTagPopulation: Number(e.target.value) } })} /></label><label>Antennas (blank = all)<input value={String(edit.config?.llrpAntennas ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpAntennas: e.target.value || undefined } })} placeholder="1,2" /></label><label>Start on GPI port (photo-eye)<input type="number" value={String(edit.config?.llrpGpiStart ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, llrpGpiStart: e.target.value ? Number(e.target.value) : undefined } })} placeholder="continuous" /></label></>}<label>MQTT topic (broker ingestion)<input value={String(edit.config?.mqttTopic ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, mqttTopic: e.target.value || undefined } })} placeholder="rfid/site1/dock1 or impinj/+" /></label></>}
            {edit.kind === 'Printer' && <><label>Printer host<input value={String(edit.config?.host ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, host: e.target.value } })} placeholder="192.168.1.80" /></label><label>Port<input type="number" value={Number(edit.config?.port ?? 9100)} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, port: Number(e.target.value) } })} /></label><label>Label stock (labels on roll)<input type="number" value={String(edit.config?.labelStock ?? '')} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, labelStock: e.target.value ? Number(e.target.value) : undefined } })} /></label><label>Low-stock minimum<input type="number" value={String(edit.config?.labelStockMin ?? 50)} onChange={(e) => setEdit({ ...edit, config: { ...edit.config, labelStockMin: Number(e.target.value) } })} /></label></>}
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

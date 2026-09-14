import { useState, type FormEvent } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { post } from '../api/client';
import { useIntegrations, useLookups, useRemove, useSave } from '../api/hooks';
import type { IntegrationEndpoint } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

export default function Integrations() {
  const { data } = useIntegrations();
  const lookups = useLookups();
  const save = useSave<IntegrationEndpoint>('/api/integrations', ['integrations']);
  const remove = useRemove('/api/integrations', ['integrations']);
  const qc = useQueryClient();
  const [edit, setEdit] = useState<(Partial<IntegrationEndpoint> & { secret?: string }) | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: { ...edit, eventTypes: edit.eventTypes ?? [], headers: edit.headers ?? {} } }); setEdit(null); };
  const deliver = async (id: string) => { const r = await post<{ delivered: number; lastError?: string }>(`/api/integrations/${id}/deliver`); setMsg(r.lastError ? `Delivery failed: ${r.lastError}` : `Delivered ${r.delivered} record(s)`); qc.invalidateQueries({ queryKey: ['integrations'] }); };
  return (
    <div>
      <div className="topbar"><h1>Integrations</h1><button className="primary" onClick={() => setEdit({ name: '', url: '', enabled: true, includeAlerts: true, eventTypes: [], batchSize: 100 })}>+ New endpoint</button></div>
      <p className="muted small">Outbound ERP / EAM / BI / webhook delivery. Every item event and alert after the endpoint's cursor is POSTed as a JSON batch, signed with HMAC-SHA256 (<code>X-Rfid-Signature</code>), with exponential back-off on failure — nothing is lost while the target is down. Inbound: readers post to <code>/api/ingest/impinj</code>, <code>/api/ingest/zebra</code>, <code>/api/ingest/generic</code> or publish via MQTT (device config <code>mqttTopic</code>).</p>
      {msg && <div className="success" style={{ marginBottom: 12 }}>{msg}</div>}
      <div className="panel table-wrap"><table><thead><tr><th>Name</th><th>URL</th><th>Filter</th><th>Delivered</th><th>Cursor</th><th>Last delivery</th><th>Status</th><th></th></tr></thead>
        <tbody>{data?.map((e) => <tr key={e.id}><td><b>{e.name}</b>{!e.enabled && <Badge>disabled</Badge>}</td><td className="mono small">{e.url}</td><td className="small">{e.eventTypes.length ? e.eventTypes.join(', ') : 'all events'}{e.includeAlerts ? ' + alerts' : ''}</td><td>{e.deliveredCount}</td><td className="small">{fmt.dt(e.eventCursor)}</td><td className="small">{fmt.ago(e.lastDeliveryAt)}</td>
          <td>{e.lastError ? <Badge tone="crit" >failing ×{e.failureCount}</Badge> : <Badge tone="ok">ok</Badge>}{e.lastError && <div className="small muted">{e.lastError} · retry {fmt.ago(e.nextAttemptAt)}</div>}</td>
          <td className="right"><button className="sm" onClick={() => deliver(e.id)}>Deliver now</button> <button className="sm" onClick={() => setEdit(e)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete endpoint?') && remove.mutate(e.id)}>Delete</button></td></tr>)}</tbody></table>
        {data?.length === 0 && <div className="empty">No endpoints yet.</div>}</div>
      {edit && <Modal title={edit.id ? 'Edit endpoint' : 'New endpoint'} onClose={() => setEdit(null)} width={720}>
        <form className="form" onSubmit={submit}>
          <div className="form cols-2">
            <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>URL *<input required value={edit.url ?? ''} onChange={(e) => setEdit({ ...edit, url: e.target.value })} placeholder="https://erp.example.com/rfid/events" /></label>
            <label>Shared secret (HMAC){edit.hasSecret ? ' · set' : ''}<input value={edit.secret ?? ''} onChange={(e) => setEdit({ ...edit, secret: e.target.value })} placeholder={edit.hasSecret ? 'leave blank to keep' : ''} /></label>
            <label>Batch size<input type="number" value={edit.batchSize ?? 100} onChange={(e) => setEdit({ ...edit, batchSize: Number(e.target.value) })} /></label>
            <label>Start from (cursor)<input type="datetime-local" onChange={(e) => setEdit({ ...edit, eventCursor: e.target.value ? new Date(e.target.value).toISOString() : edit.eventCursor })} /></label>
            <div className="row" style={{ alignItems: 'end', paddingBottom: 8 }}><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.enabled} onChange={(e) => setEdit({ ...edit, enabled: e.target.checked })} /> Enabled</label><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.includeAlerts} onChange={(e) => setEdit({ ...edit, includeAlerts: e.target.checked })} /> Include alerts</label></div>
          </div>
          <div><h3>Event types (none = all)</h3><div className="chips">{lookups.data?.eventTypes.map((t) => <span key={t} className={'chip' + (edit.eventTypes?.includes(t) ? ' active' : '')} onClick={() => setEdit({ ...edit, eventTypes: edit.eventTypes?.includes(t) ? edit.eventTypes.filter((x) => x !== t) : [...(edit.eventTypes ?? []), t] })}>{t}</span>)}</div></div>
          <label>Extra headers (JSON)<textarea className="mono" rows={2} value={JSON.stringify(edit.headers ?? {})} onChange={(e) => { try { setEdit({ ...edit, headers: JSON.parse(e.target.value) }); } catch { /* keep typing */ } }} /></label>
          <ErrorBox error={save.error} />
          <div className="row end"><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

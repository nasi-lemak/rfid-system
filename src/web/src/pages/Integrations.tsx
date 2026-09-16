import { useState, type FormEvent } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { post } from '../api/client';
import { useIntegrations, useItemTypes, useLocations, useLookups, useRemove, useSave } from '../api/hooks';
import type { IntegrationEndpoint } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

export default function Integrations() {
  const { data } = useIntegrations();
  const lookups = useLookups(); const types = useItemTypes(); const locs = useLocations();
  const save = useSave<IntegrationEndpoint>('/api/integrations', ['integrations']);
  const remove = useRemove('/api/integrations', ['integrations']);
  const qc = useQueryClient();
  const [edit, setEdit] = useState<(Partial<IntegrationEndpoint> & { secret?: string; apiToken?: string; password?: string; clientSecret?: string }) | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: { ...edit, eventTypes: edit.eventTypes ?? [], itemTypeCodes: edit.itemTypeCodes ?? [], headers: edit.headers ?? {} } }); setEdit(null); };
  const deliver = async (id: string) => { const r = await post<{ delivered: number; lastError?: string }>(`/api/integrations/${id}/deliver`); setMsg(r.lastError ? `Delivery failed: ${r.lastError}` : `Delivered ${r.delivered} record(s)`); qc.invalidateQueries({ queryKey: ['integrations'] }); };
  return (
    <div>
      <div className="topbar"><h1>Integrations</h1><button className="primary" onClick={() => setEdit({ name: '', url: '', enabled: true, includeAlerts: true, eventTypes: [], batchSize: 100 })}>+ New endpoint</button></div>
      <p className="muted small">Outbound ERP / EAM / BI / webhook delivery. Every item event and alert after the endpoint's cursor is POSTed as a JSON batch, signed with HMAC-SHA256 (<code>X-Rfid-Signature</code>). Batches travel through the transactional outbox (one in flight per endpoint, exponential back-off, dead-letter after 8 attempts) and the cursor only advances on success — nothing is lost while the target is down. Filter by event type, item type and site. Inbound: readers post to <code>/api/ingest/impinj</code>, <code>/api/ingest/zebra</code>, <code>/api/ingest/generic</code> or publish via MQTT (device config <code>mqttTopic</code>).</p>
      {msg && <div className="success" style={{ marginBottom: 12 }}>{msg}</div>}
      <div className="panel table-wrap"><table><thead><tr><th>Name</th><th>URL</th><th>Filter</th><th>Delivered</th><th>Cursor</th><th>Last delivery</th><th>Status</th><th></th></tr></thead>
        <tbody>{data?.map((e) => <tr key={e.id}><td><b>{e.name}</b>{!e.enabled && <Badge>disabled</Badge>}</td><td className="mono small">{e.url}</td><td className="small"><Badge>{e.format ?? 'Generic'}</Badge> <Badge>{e.authType ?? 'None'}</Badge><div>{e.eventTypes.length ? e.eventTypes.join(', ') : 'all events'}{e.includeAlerts ? ' + alerts' : ''}{e.itemTypeCodes?.length ? ` · types ${e.itemTypeCodes.join(', ')}` : ''}{e.siteLocationId ? ` · site ${locs.data?.find((l) => l.id === e.siteLocationId)?.name ?? '…'}` : ''}</div></td><td>{e.deliveredCount}</td><td className="small">{fmt.dt(e.eventCursor)}</td><td className="small">{fmt.ago(e.lastDeliveryAt)}</td>
          <td>{e.lastError ? <Badge tone="crit" >failing ×{e.failureCount}</Badge> : e.inFlightMessageId ? <Badge tone="info">in flight</Badge> : <Badge tone="ok">ok</Badge>}{e.lastError && <div className="small muted">{e.lastError} · retry {fmt.ago(e.nextAttemptAt)}</div>}</td>
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
          <div className="form cols-2">
            <label>Payload format<select value={edit.format ?? 'Generic'} onChange={(e) => setEdit({ ...edit, format: e.target.value })}><option value="Generic">Generic envelope (events + alerts)</option><option value="SapAssetManagement">SAP S/4 asset master + transfers</option><option value="Dynamics365">Dynamics 365 customer asset</option><option value="Maximo">IBM Maximo MXASSET</option></select></label>
            <label>Authentication<select value={edit.authType ?? 'None'} onChange={(e) => setEdit({ ...edit, authType: e.target.value })}><option value="None">None (HMAC signature only)</option><option value="Bearer">Bearer / API token</option><option value="Basic">Basic (username/password)</option><option value="OAuth2ClientCredentials">OAuth2 client credentials</option></select></label>
            {edit.authType === 'Bearer' && <label style={{ gridColumn: '1 / -1' }}>API token{edit.hasCredentials ? ' · set' : ''}<input value={edit.apiToken ?? ''} onChange={(e) => setEdit({ ...edit, apiToken: e.target.value })} placeholder={edit.hasCredentials ? 'leave blank to keep' : ''} /></label>}
            {edit.authType === 'Basic' && <><label>Username<input value={edit.username ?? ''} onChange={(e) => setEdit({ ...edit, username: e.target.value })} /></label><label>Password<input type="password" value={edit.password ?? ''} onChange={(e) => setEdit({ ...edit, password: e.target.value })} placeholder={edit.hasCredentials ? 'leave blank to keep' : ''} /></label></>}
            {edit.authType === 'OAuth2ClientCredentials' && <><label>Token URL<input value={edit.tokenUrl ?? ''} onChange={(e) => setEdit({ ...edit, tokenUrl: e.target.value })} placeholder="https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token" /></label><label>Scope<input value={edit.scope ?? ''} onChange={(e) => setEdit({ ...edit, scope: e.target.value })} /></label><label>Client ID<input value={edit.clientId ?? ''} onChange={(e) => setEdit({ ...edit, clientId: e.target.value })} /></label><label>Client secret<input type="password" value={edit.clientSecret ?? ''} onChange={(e) => setEdit({ ...edit, clientSecret: e.target.value })} placeholder={edit.hasCredentials ? 'leave blank to keep' : ''} /></label></>}
            {edit.format && edit.format !== 'Generic' && <label style={{ gridColumn: '1 / -1' }}>Vendor mapping (JSON) · SAP: companyCode, plant, costCenterAttribute · Dynamics: accountId, siteId · Maximo: siteId, orgId<textarea className="mono" rows={2} defaultValue={JSON.stringify(edit.mapping ?? {})} onBlur={(e) => { try { setEdit({ ...edit, mapping: JSON.parse(e.target.value || '{}') }); } catch { /* ignore */ } }} /></label>}
          </div>
          <div className="form cols-2">
            <label>Site (optional)<select value={edit.siteLocationId ?? ''} onChange={(e) => setEdit({ ...edit, siteLocationId: e.target.value || null })}><option value="">— whole tenant —</option>{locs.data?.filter((l) => l.kind === 'Site').map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
            <div><span className="muted small">Item types (none = all)</span><div className="chips" style={{ marginTop: 4 }}>{types.data?.map((t) => <span key={t.code} className={'chip' + (edit.itemTypeCodes?.includes(t.code) ? ' active' : '')} onClick={() => setEdit({ ...edit, itemTypeCodes: edit.itemTypeCodes?.includes(t.code) ? edit.itemTypeCodes.filter((x) => x !== t.code) : [...(edit.itemTypeCodes ?? []), t.code] })}>{t.name}</span>)}</div></div>
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

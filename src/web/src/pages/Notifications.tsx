import { useState } from 'react';
import { del, post, put } from '../api/client';
import { useChannels, useInvalidate, useLookups, useNotificationLog, usePolicies } from '../api/hooks';
import type { EscalationPolicy, NotificationChannel } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

const fields: Record<NotificationChannel['kind'], { key: string; label: string; secret?: boolean; placeholder?: string }[]> = {
  Email: [{ key: 'host', label: 'SMTP host', placeholder: 'smtp.office365.com' }, { key: 'port', label: 'Port', placeholder: '587' }, { key: 'useTls', label: 'TLS (true/false)', placeholder: 'true' }, { key: 'username', label: 'Username' }, { key: 'password', label: 'Password', secret: true }, { key: 'from', label: 'From', placeholder: 'rfid@example.com' }, { key: 'to', label: 'To (comma list)', placeholder: 'ops@example.com, security@example.com' }],
  Sms: [{ key: 'accountSid', label: 'Account SID' }, { key: 'authToken', label: 'Auth token', secret: true }, { key: 'from', label: 'From number', placeholder: '+15551234567' }, { key: 'to', label: 'To numbers (comma list)', placeholder: '+447700900123' }, { key: 'apiBase', label: 'API base (Twilio-compatible)', placeholder: 'https://api.twilio.com/2010-04-01' }],
  Teams: [{ key: 'url', label: 'Incoming webhook URL', placeholder: 'https://….webhook.office.com/webhookb2/…' }],
  Slack: [{ key: 'url', label: 'Incoming webhook URL', placeholder: 'https://hooks.slack.com/services/…' }],
  Webhook: [{ key: 'url', label: 'URL (receives JSON: subject, body, severity, alertId…)' }],
};

export default function Notifications() {
  const channels = useChannels(); const policies = usePolicies(); const log = useNotificationLog(); const lookups = useLookups(); const inv = useInvalidate();
  const [tab, setTab] = useState<'channels' | 'policies' | 'log'>('channels');
  const [ch, setCh] = useState<Partial<NotificationChannel> | null>(null); const [pol, setPol] = useState<Partial<EscalationPolicy> | null>(null);
  const [error, setError] = useState<unknown>(null); const [testResult, setTestResult] = useState<string | null>(null);
  const saveCh = async () => { if (!ch) return; setError(null); try { const body = { ...ch, config: ch.config ?? {} }; if (ch.id) await put(`/api/notifications/channels/${ch.id}`, body); else await post('/api/notifications/channels', body); inv('channels'); setCh(null); } catch (e) { setError(e); } };
  const savePol = async () => { if (!pol) return; setError(null); try { const body = { ...pol, steps: (pol.steps ?? []).map((s) => ({ ...s, channelIds: s.channelIds ?? [] })) }; if (pol.id) await put(`/api/notifications/policies/${pol.id}`, body); else await post('/api/notifications/policies', body); inv('policies'); setPol(null); } catch (e) { setError(e); } };
  const test = async (id: string) => { const r = await post<{ ok: boolean; recipient?: string; error?: string }>(`/api/notifications/channels/${id}/test`); setTestResult(r.ok ? `Test sent to ${r.recipient}` : `Failed: ${r.error}`); inv('channels', 'notification-log'); };
  const steps = pol?.steps ?? [];
  const setStep = (i: number, s: Partial<EscalationPolicy['steps'][number]>) => setPol({ ...pol!, steps: steps.map((x, j) => j === i ? { ...x, ...s } : x) });
  const chName = (id: string) => channels.data?.find((c) => c.channel.id === id)?.channel.name ?? '?';
  return (
    <div>
      <div className="topbar"><h1>Notifications & escalation</h1><span className="muted small">Where alerts go (e-mail, SMS, Teams, Slack, webhooks) and what happens when nobody acknowledges them</span></div>
      <div className="tabs">{(['channels', 'policies', 'log'] as const).map((t) => <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t === 'channels' ? 'Channels' : t === 'policies' ? 'Escalation policies' : 'Delivery log'}</button>)}</div>
      {testResult && <div className="panel" style={{ marginBottom: 12 }}>{testResult} <button className="sm" onClick={() => setTestResult(null)}>✕</button></div>}
      {tab === 'channels' && <>
        <div className="topbar"><p className="muted small" style={{ margin: 0 }}>Rules name channels directly (Rules → Notify channels). <b>Catch-all</b> channels receive every alert at or above their severity, including reader-offline and geofence alerts.</p><button className="primary" onClick={() => setCh({ name: '', kind: 'Email', enabled: true, catchAll: false, minSeverity: 'Critical', config: {} })}>+ New channel</button></div>
        <div className="panel table-wrap"><table><thead><tr><th>Channel</th><th>Kind</th><th>Catch-all</th><th>Last 7 days</th><th>Enabled</th><th></th></tr></thead>
          <tbody>{channels.data?.map(({ channel: c, sent7d, failed7d }) => <tr key={c.id}><td><b>{c.name}</b><div className="muted small">{String(c.config.to ?? c.config.url ?? '')}</div></td><td><Badge>{c.kind}</Badge></td><td>{c.catchAll ? <Badge tone="info">≥ {c.minSeverity}</Badge> : <span className="muted">—</span>}</td><td className="small"><Badge tone="ok">{sent7d} sent</Badge> {failed7d > 0 && <Badge tone="crit">{failed7d} failed</Badge>}</td><td>{c.enabled ? '✓' : '—'}</td><td className="right"><button className="sm" onClick={() => test(c.id)}>Send test</button> <button className="sm" onClick={() => setCh(c)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete channel?') && del(`/api/notifications/channels/${c.id}`).then(() => inv('channels'))}>✕</button></td></tr>)}
            {channels.data?.length === 0 && <tr><td colSpan={6} className="muted">No channels yet.</td></tr>}</tbody></table></div>
      </>}
      {tab === 'policies' && <>
        <div className="topbar"><p className="muted small" style={{ margin: 0 }}>Steps fire while an alert stays <b>open and unacknowledged</b>: step 1 after N minutes (0 = immediately), each later step N minutes after the previous. A <b>default</b> policy applies to alerts raised without a rule (device health, geofences) at or above its severity.</p><button className="primary" onClick={() => setPol({ name: '', steps: [{ afterMinutes: 0, channelIds: [] }, { afterMinutes: 15, channelIds: [] }], repeatLastStep: false, isDefault: false, minSeverity: 'Warning' })}>+ New policy</button></div>
        <div className="grid cols-2">{policies.data?.map((p) => <div className="panel" key={p.id}><div className="topbar"><h3 style={{ margin: 0 }}>{p.name} {p.isDefault && <Badge tone="info">default ≥ {p.minSeverity}</Badge>}</h3><span className="row"><button className="sm" onClick={() => setPol(p)}>Edit</button><button className="sm danger" onClick={() => confirm('Delete policy?') && del(`/api/notifications/policies/${p.id}`).then(() => inv('policies'))}>✕</button></span></div>
          <ol style={{ margin: '8px 0 0 18px', padding: 0 }}>{p.steps.map((s, i) => <li key={i} className="small" style={{ marginBottom: 4 }}>{s.afterMinutes === 0 ? 'Immediately' : `After ${s.afterMinutes} min`}: {s.channelIds.map(chName).join(', ') || <span className="muted">no channels</span>}{s.message ? <span className="muted"> · "{s.message}"</span> : null}</li>)}</ol>
          {p.repeatLastStep && <div className="muted small">Repeats the last step until acknowledged</div>}</div>)}
          {policies.data?.length === 0 && <div className="panel empty">No escalation policies yet.</div>}</div>
      </>}
      {tab === 'log' && <div className="panel table-wrap"><table><thead><tr><th>When</th><th>Channel</th><th>Recipient</th><th>Subject</th><th>Level</th><th>Status</th></tr></thead>
        <tbody>{log.data?.map((l) => <tr key={l.id}><td className="small muted">{fmt.ago(l.sentAt)}</td><td>{l.channel}</td><td className="small">{l.recipient}</td><td className="small">{l.subject}</td><td>{l.escalationLevel > 0 ? <Badge tone="info">#{l.escalationLevel}</Badge> : ''}</td><td><Badge tone={l.status === 'Sent' ? 'ok' : 'crit'}>{l.status}</Badge>{l.error && <div className="small" style={{ color: 'var(--crit)' }}>{l.error}</div>}</td></tr>)}
          {log.data?.length === 0 && <tr><td colSpan={6} className="muted">Nothing sent yet.</td></tr>}</tbody></table></div>}
      {ch && <Modal title={ch.id ? 'Edit channel' : 'New channel'} onClose={() => setCh(null)}>
        <form className="form" onSubmit={(e) => { e.preventDefault(); saveCh(); }}>
          <div className="form cols-2">
            <label>Name *<input required value={ch.name ?? ''} onChange={(e) => setCh({ ...ch, name: e.target.value })} /></label>
            <label>Kind<select value={ch.kind} onChange={(e) => setCh({ ...ch, kind: e.target.value as NotificationChannel['kind'], config: {} })}>{Object.keys(fields).map((k) => <option key={k}>{k}</option>)}</select></label>
            {fields[ch.kind ?? 'Email'].map((f) => <label key={f.key} style={f.key === 'url' || f.key === 'to' ? { gridColumn: '1 / -1' } : undefined}>{f.label}<input type={f.secret ? 'password' : 'text'} value={String(ch.config?.[f.key] ?? '')} onChange={(e) => setCh({ ...ch, config: { ...ch.config, [f.key]: e.target.value } })} placeholder={f.placeholder} /></label>)}
            <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!ch.catchAll} onChange={(e) => setCh({ ...ch, catchAll: e.target.checked })} /> Catch-all (every alert at or above…)</label>
            <label>Minimum severity<select value={ch.minSeverity} onChange={(e) => setCh({ ...ch, minSeverity: e.target.value })}>{lookups.data?.severities.map((s) => <option key={s}>{s}</option>)}</select></label>
          </div>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={ch.enabled !== false} onChange={(e) => setCh({ ...ch, enabled: e.target.checked })} /> Enabled</label>
          <ErrorBox error={error} /><div className="row end"><button type="button" onClick={() => setCh(null)}>Cancel</button><button className="primary">Save</button></div>
        </form></Modal>}
      {pol && <Modal title={pol.id ? 'Edit policy' : 'New escalation policy'} onClose={() => setPol(null)} width={640}>
        <form className="form" onSubmit={(e) => { e.preventDefault(); savePol(); }}>
          <div className="form cols-2"><label>Name *<input required value={pol.name ?? ''} onChange={(e) => setPol({ ...pol, name: e.target.value })} /></label><label>Default for severity ≥<select value={pol.minSeverity} onChange={(e) => setPol({ ...pol, minSeverity: e.target.value })}>{lookups.data?.severities.map((s) => <option key={s}>{s}</option>)}</select></label></div>
          <div className="row"><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!pol.isDefault} onChange={(e) => setPol({ ...pol, isDefault: e.target.checked })} /> Default policy (alerts without a rule policy)</label><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!pol.repeatLastStep} onChange={(e) => setPol({ ...pol, repeatLastStep: e.target.checked })} /> Repeat last step until acknowledged</label></div>
          <div><h3>Steps</h3>{steps.map((s, i) => <div key={i} className="panel" style={{ padding: 10, marginBottom: 8 }}><div className="row"><b>Step {i + 1}</b><span className="small">after</span><input type="number" min={0} style={{ width: 80 }} value={s.afterMinutes} onChange={(e) => setStep(i, { afterMinutes: Number(e.target.value) })} /><span className="small">min</span><input style={{ flex: 1 }} placeholder="extra message (optional)" value={s.message ?? ''} onChange={(e) => setStep(i, { message: e.target.value })} /><button type="button" className="sm danger" onClick={() => setPol({ ...pol, steps: steps.filter((_, j) => j !== i) })}>✕</button></div>
            <div className="row" style={{ marginTop: 6 }}>{channels.data?.map((c) => <label key={c.channel.id} className="row small" style={{ gap: 4 }}><input type="checkbox" style={{ width: 'auto' }} checked={(s.channelIds ?? []).includes(c.channel.id)} onChange={(e) => setStep(i, { channelIds: e.target.checked ? [...(s.channelIds ?? []), c.channel.id] : (s.channelIds ?? []).filter((x) => x !== c.channel.id) })} />{c.channel.name}</label>)}</div></div>)}
            <button type="button" className="sm" onClick={() => setPol({ ...pol, steps: [...steps, { afterMinutes: 30, channelIds: [] }] })}>+ Add step</button></div>
          <ErrorBox error={error} /><div className="row end"><button type="button" onClick={() => setPol(null)}>Cancel</button><button className="primary">Save</button></div>
        </form></Modal>}
    </div>
  );
}

import { useState, type FormEvent } from 'react';
import { useChannels, useLookups, usePolicies, useRemove, useRules, useSave } from '../api/hooks';
import { useAuth } from '../auth';
import type { Rule, RuleCondition } from '../api/types';
import { Badge, ErrorBox, Modal, toneForSeverity } from '../components/ui';

const blank: Partial<Rule> = { name: '', enabled: true, trigger: 'Moved', conditions: [], action: 'CreateAlert', params: { message: '' }, severity: 'Warning' };

export default function Rules() {
  const { data } = useRules();
  const lookups = useLookups();
  const { user } = useAuth(); const isAdmin = user?.role === 'Admin';
  const channels = useChannels(); const policies = usePolicies();
  const save = useSave<Rule>('/api/rules', ['rules']);
  const remove = useRemove('/api/rules', ['rules']);
  const [edit, setEdit] = useState<Partial<Rule> | null>(null);
  const conds = edit?.conditions ?? [];
  const setCond = (i: number, c: Partial<RuleCondition>) => setEdit({ ...edit!, conditions: conds.map((x, j) => (j === i ? { ...x, ...c } : x)) });
  const parseVal = (s: string): unknown => { if (s === 'true') return true; if (s === 'false') return false; if (s !== '' && !isNaN(Number(s))) return Number(s); return s; };
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: edit }); setEdit(null); };
  return (
    <div>
      <div className="topbar"><h1>Rules</h1><button className="primary" onClick={() => setEdit({ ...blank })}>+ New rule</button></div>
      <p className="muted small">Rules run whenever an item event happens: <i>when</i> trigger <i>and</i> all conditions match → action. Example: <code>Moved</code> + <code>data.direction eq Out</code> + <code>item.state ne Sold</code> → critical alert (loss prevention).</p>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>Rule</th><th>Trigger</th><th>Conditions</th><th>Action</th><th>Severity</th><th>Enabled</th><th></th></tr></thead>
          <tbody>{data?.map((r) => <tr key={r.id}>
            <td><b>{r.name}</b>{r.vertical && <div className="muted small">{r.vertical}</div>}</td><td><Badge>{r.trigger}</Badge></td>
            <td className="small mono">{r.conditions.map((c, i) => <div key={i}>{c.field} {c.op} {JSON.stringify(c.value)}</div>)}{r.conditions.length === 0 && <span className="muted">always</span>}</td>
            <td>{r.action}{(r.notifyChannelIds?.length ?? 0) > 0 && <Badge tone="info">{r.notifyChannelIds!.length} channel(s)</Badge>}{r.action === 'CreateAlert' && r.params?.message ? <div className="muted small">{String(r.params.message)}</div> : null}{r.action === 'SetState' ? <div className="muted small">→ {String(r.params?.state)}</div> : null}</td>
            <td><Badge tone={toneForSeverity(r.severity)}>{r.severity}</Badge></td><td>{r.enabled ? '✓' : '—'}</td>
            <td className="right"><button className="sm" onClick={() => setEdit(r)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete rule?') && remove.mutate(r.id)}>Delete</button></td>
          </tr>)}</tbody>
        </table>
      </div>
      {edit && <Modal title={edit.id ? 'Edit rule' : 'New rule'} onClose={() => setEdit(null)} width={760}>
        <form className="form" onSubmit={submit}>
          <div className="form cols-2">
            <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Trigger (event type)<select value={edit.trigger} onChange={(e) => setEdit({ ...edit, trigger: e.target.value })}>{lookups.data?.eventTypes.map((t) => <option key={t}>{t}</option>)}</select></label>
            <label>Action<select value={edit.action} onChange={(e) => setEdit({ ...edit, action: e.target.value })}>{lookups.data?.ruleActions.map((t) => <option key={t}>{t}</option>)}</select></label>
            <label>Severity<select value={edit.severity} onChange={(e) => setEdit({ ...edit, severity: e.target.value })}>{lookups.data?.severities.map((t) => <option key={t}>{t}</option>)}</select></label>
            {edit.action === 'CreateAlert' && <label style={{ gridColumn: '1 / -1' }}>Message template<input value={String(edit.params?.message ?? '')} onChange={(e) => setEdit({ ...edit, params: { ...edit.params, message: e.target.value } })} placeholder="{item.name} left {fromLocation.name} while {item.state}" /></label>}
            {edit.action === 'SetState' && <label>Set state to<input value={String(edit.params?.state ?? '')} onChange={(e) => setEdit({ ...edit, params: { ...edit.params, state: e.target.value } })} /></label>}
            {edit.action === 'Webhook' && <label style={{ gridColumn: '1 / -1' }}>Webhook URL<input value={String(edit.params?.url ?? '')} onChange={(e) => setEdit({ ...edit, params: { ...edit.params, url: e.target.value } })} /></label>}
            {(edit.action === 'CreateAlert' || edit.action === 'Notify') && isAdmin && <>
              <div><span className="muted small">Notify channels immediately</span><div className="row" style={{ marginTop: 4 }}>{channels.data?.map((c) => <label key={c.channel.id} className="row" style={{ gap: 4 }}><input type="checkbox" style={{ width: 'auto' }} checked={(edit.notifyChannelIds ?? []).includes(c.channel.id)} onChange={(e) => setEdit({ ...edit, notifyChannelIds: e.target.checked ? [...(edit.notifyChannelIds ?? []), c.channel.id] : (edit.notifyChannelIds ?? []).filter((x) => x !== c.channel.id) })} />{c.channel.name}</label>)}{channels.data?.length === 0 && <span className="muted small">No channels yet (Notifications page)</span>}</div></div>
              <label>Escalation policy<select value={edit.escalationPolicyId ?? ''} onChange={(e) => setEdit({ ...edit, escalationPolicyId: e.target.value || null })}><option value="">— default policy —</option>{policies.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
            </>}
          </div>
          <div><h3>Conditions (all must match)</h3>
            {conds.map((c, i) => <div className="row" key={i} style={{ marginBottom: 6 }}>
              <input list="fields" style={{ flex: 2 }} value={c.field} onChange={(e) => setCond(i, { field: e.target.value })} placeholder="item.state" />
              <select style={{ flex: 1 }} value={c.op} onChange={(e) => setCond(i, { op: e.target.value })}>{lookups.data?.ruleOps.map((o) => <option key={o}>{o}</option>)}</select>
              <input style={{ flex: 2 }} value={Array.isArray(c.value) ? c.value.join(',') : String(c.value ?? '')} onChange={(e) => setCond(i, { value: parseVal(e.target.value) })} placeholder="value (comma list for in/nin)" />
              <button type="button" className="sm danger" onClick={() => setEdit({ ...edit, conditions: conds.filter((_, j) => j !== i) })}>✕</button>
            </div>)}
            <datalist id="fields">{lookups.data?.ruleFields.map((f) => <option key={f} value={f} />)}</datalist>
            <button type="button" className="sm" onClick={() => setEdit({ ...edit, conditions: [...conds, { field: 'item.state', op: 'eq', value: '' }] })}>+ Add condition</button>
          </div>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.enabled} onChange={(e) => setEdit({ ...edit, enabled: e.target.checked })} /> Enabled</label>
          <ErrorBox error={save.error} />
          <div className="row end"><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

import { useState, type FormEvent } from 'react';
import { useLocations, useOperationDefinitions, useParties, useRemove, useSave, useWorkflowRuns, useWorkflows } from '../api/hooks';
import type { WorkflowDefinition, WorkflowStep } from '../api/types';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

const ASK = ['toLocation', 'party', 'targetState', 'quantity', 'dueBack', 'container'];
const blankStep = (i: number): WorkflowStep => ({ key: `step-${i}`, title: '', prompt: '', operation: 'Transfer', ask: [], fixed: {}, rescan: false, optional: false, onRejected: 'stop' });
const blank: Partial<WorkflowDefinition> = { code: '', name: '', description: '', enabled: true, itemTypeCodes: [], steps: [blankStep(1)] };

/** Guided workflows: ordered operation definitions with prompts. Each step runs as an ordinary operation carrying the run id. */
export default function Workflows() {
  const { data } = useWorkflows();
  const runs = useWorkflowRuns();
  const defs = useOperationDefinitions(); const locs = useLocations(); const parties = useParties();
  const save = useSave<WorkflowDefinition>('/api/workflows', ['workflows']);
  const remove = useRemove('/api/workflows', ['workflows']);
  const [edit, setEdit] = useState<Partial<WorkflowDefinition> | null>(null);
  const [tab, setTab] = useState<'definitions' | 'runs'>('definitions');
  const steps = edit?.steps ?? [];
  const setStep = (i: number, patch: Partial<WorkflowStep>) => setEdit({ ...edit!, steps: steps.map((s, j) => (j === i ? { ...s, ...patch } : s)) });
  const setFixed = (i: number, patch: Partial<WorkflowStep['fixed']>) => setStep(i, { fixed: { ...steps[i].fixed, ...patch } });
  const move = (i: number, d: number) => { const n = [...steps]; const j = i + d; if (j < 0 || j >= n.length) return; [n[i], n[j]] = [n[j], n[i]]; setEdit({ ...edit!, steps: n }); };
  const submit = async (e: FormEvent) => { e.preventDefault(); if (!edit) return; await save.mutateAsync({ id: edit.id, body: { ...edit, itemTypeCodes: edit.itemTypeCodes ?? [], steps } }); setEdit(null); };
  const opName = (code: string) => defs.data?.find((d) => d.code === code)?.name ?? code;
  return (
    <div>
      <div className="topbar"><h1>Workflows</h1><div className="row"><button className={tab === 'definitions' ? 'primary' : ''} onClick={() => setTab('definitions')}>Definitions</button><button className={tab === 'runs' ? 'primary' : ''} onClick={() => setTab('runs')}>Runs</button><button className="primary" onClick={() => setEdit({ ...blank, steps: [blankStep(1)] })}>+ New workflow</button></div></div>
      <p className="muted small">A workflow guides the handheld through an ordered list of operations (e.g. <i>Return → Decontaminate → Sterilise</i>). Templates ship workflows for their vertical; every step executes as an ordinary operation stamped with the run id, so runs work offline through the same queue and the audit trail is the operations themselves.</p>
      {tab === 'definitions' && <div className="panel table-wrap"><table>
        <thead><tr><th>Workflow</th><th>Item types</th><th>Steps</th><th>Enabled</th><th></th></tr></thead>
        <tbody>{data?.map((w) => <tr key={w.id}>
          <td><b>{w.icon ? `${w.icon} ` : ''}{w.name}</b> <span className="mono small muted">{w.code}</span>{w.vertical && <div className="muted small">{w.vertical}</div>}<div className="small">{w.description}</div></td>
          <td className="small">{w.itemTypeCodes.length ? w.itemTypeCodes.join(', ') : 'any'}</td>
          <td className="small">{w.steps.map((s, i) => <div key={s.key}>{i + 1}. <b>{s.title || s.key}</b> → {opName(s.operation)}{s.ask.length ? <span className="muted"> · asks {s.ask.join(', ')}</span> : null}{s.optional ? <Badge>optional</Badge> : null}{s.rescan ? <Badge tone="info">rescan</Badge> : null}</div>)}</td>
          <td>{w.enabled ? '✓' : '—'}</td>
          <td className="right"><button className="sm" onClick={() => setEdit(w)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete workflow?') && remove.mutate(w.id)}>Delete</button></td>
        </tr>)}</tbody></table>{data?.length === 0 && <div className="empty">No workflows yet — install a template or create one.</div>}</div>}
      {tab === 'runs' && <div className="panel table-wrap"><table>
        <thead><tr><th>Run</th><th>Workflow</th><th>Started</th><th>Last activity</th><th>Progress</th><th>Lines</th><th>Status</th></tr></thead>
        <tbody>{runs.data?.map((r) => <tr key={r.runId}>
          <td className="mono small">{r.runId}</td><td>{r.workflowName ?? r.workflowCode}</td><td className="small">{fmt.dt(r.startedAt)}</td><td className="small">{fmt.ago(r.lastActivityAt)}</td>
          <td>{r.stepsDone}/{r.stepsTotal} <span className="muted small">last: {r.lastStep}</span></td>
          <td><Badge tone="ok">{r.ok} ok</Badge> {r.rejected > 0 && <Badge tone="warn">{r.rejected} rejected</Badge>} {r.unknown > 0 && <Badge tone="info">{r.unknown} unknown</Badge>}</td>
          <td>{r.completed ? <Badge tone="ok">completed</Badge> : <Badge tone="info">in progress</Badge>}</td>
        </tr>)}</tbody></table>{runs.data?.length === 0 && <div className="empty">No runs yet.</div>}</div>}
      {edit && <Modal title={edit.id ? 'Edit workflow' : 'New workflow'} onClose={() => setEdit(null)} width={900}>
        <form className="form" onSubmit={submit}>
          <div className="form cols-2">
            <label>Code *<input required disabled={!!edit.id} value={edit.code ?? ''} onChange={(e) => setEdit({ ...edit, code: e.target.value })} placeholder="cssd-reprocess" /></label>
            <label>Name *<input required value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label style={{ gridColumn: '1 / -1' }}>Description<input value={edit.description ?? ''} onChange={(e) => setEdit({ ...edit, description: e.target.value })} /></label>
            <label>Icon<input value={edit.icon ?? ''} onChange={(e) => setEdit({ ...edit, icon: e.target.value || null })} placeholder="🧪" /></label>
            <label>Item type codes (comma list, blank = any)<input value={(edit.itemTypeCodes ?? []).join(',')} onChange={(e) => setEdit({ ...edit, itemTypeCodes: e.target.value.split(',').map((x) => x.trim()).filter(Boolean) })} /></label>
          </div>
          <h3>Steps</h3>
          {steps.map((st, i) => <div className="panel" key={i} style={{ marginBottom: 8 }}>
            <div className="row" style={{ justifyContent: 'space-between' }}><b>Step {i + 1}</b><div className="row"><button type="button" className="sm" onClick={() => move(i, -1)}>↑</button><button type="button" className="sm" onClick={() => move(i, 1)}>↓</button><button type="button" className="sm danger" onClick={() => setEdit({ ...edit, steps: steps.filter((_, j) => j !== i) })}>✕</button></div></div>
            <div className="form cols-2">
              <label>Key *<input required value={st.key} onChange={(e) => setStep(i, { key: e.target.value })} /></label>
              <label>Title *<input required value={st.title} onChange={(e) => setStep(i, { title: e.target.value })} /></label>
              <label style={{ gridColumn: '1 / -1' }}>Prompt shown to the operator<input value={st.prompt ?? ''} onChange={(e) => setStep(i, { prompt: e.target.value })} /></label>
              <label>Operation *<select value={st.operation} onChange={(e) => setStep(i, { operation: e.target.value })}>{defs.data?.filter((d) => d.enabled && d.baseType !== 'Commission').map((d) => <option key={d.code} value={d.code}>{d.name}{d.isBuiltIn ? '' : ` · ${d.vertical ?? 'custom'}`}</option>)}</select></label>
              <label>On rejected line<select value={st.onRejected} onChange={(e) => setStep(i, { onRejected: e.target.value as WorkflowStep['onRejected'] })}><option value="stop">stop the run for those items</option><option value="continue">continue with the rest</option></select></label>
              <div><span className="muted small">Ask the operator for</span><div className="chips" style={{ marginTop: 4 }}>{ASK.map((a) => <span key={a} className={'chip' + (st.ask.includes(a) ? ' active' : '')} onClick={() => setStep(i, { ask: st.ask.includes(a) ? st.ask.filter((x) => x !== a) : [...st.ask, a] })}>{a}</span>)}</div></div>
              <div className="row" style={{ alignItems: 'end', paddingBottom: 6 }}><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={st.rescan} onChange={(e) => setStep(i, { rescan: e.target.checked })} /> Rescan tags at this step</label><label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={st.optional} onChange={(e) => setStep(i, { optional: e.target.checked })} /> Optional</label></div>
              <label>Fixed destination<select value={st.fixed.toLocationId ?? ''} onChange={(e) => setFixed(i, { toLocationId: e.target.value || null })}><option value="">— ask / none —</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{l.name} ({l.kind})</option>)}</select></label>
              <label>Fixed party<select value={st.fixed.partyId ?? ''} onChange={(e) => setFixed(i, { partyId: e.target.value || null })}><option value="">— ask / none —</option>{parties.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
              <label>Fixed target state<input value={st.fixed.targetState ?? ''} onChange={(e) => setFixed(i, { targetState: e.target.value || null })} placeholder="e.g. InWash" /></label>
              <label>Reference template<input value={st.fixed.reference ?? ''} onChange={(e) => setFixed(i, { reference: e.target.value || null })} placeholder="optional" /></label>
            </div>
          </div>)}
          <button type="button" className="sm" onClick={() => setEdit({ ...edit, steps: [...steps, blankStep(steps.length + 1)] })}>+ Add step</button>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={!!edit.enabled} onChange={(e) => setEdit({ ...edit, enabled: e.target.checked })} /> Enabled</label>
          <ErrorBox error={save.error} />
          <div className="row end"><button type="button" onClick={() => setEdit(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
    </div>
  );
}

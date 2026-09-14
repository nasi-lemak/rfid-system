import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useCreateStocktake, useItemTypes, useLocations, useRemove, useSave, useSchedules, useStocktakes } from '../api/hooks';
import { post } from '../api/client';
import type { StocktakeSchedule } from '../api/types';
import { Badge, ErrorBox, Modal, fmt, toneForStatus } from '../components/ui';

export default function Stocktakes() {
  const { data } = useStocktakes();
  const locs = useLocations();
  const types = useItemTypes();
  const create = useCreateStocktake();
  const nav = useNavigate();
  const [show, setShow] = useState(false);
  const [f, setF] = useState({ name: '', locationId: '', itemTypeId: '' });
  const schedules = useSchedules();
  const saveSched = useSave<StocktakeSchedule>('/api/stocktake-schedules', ['schedules', 'stocktakes']);
  const removeSched = useRemove('/api/stocktake-schedules', ['schedules']);
  const [sched, setSched] = useState<Partial<StocktakeSchedule> | null>(null);
  const submitSched = async (e: FormEvent) => { e.preventDefault(); if (!sched) return; await saveSched.mutateAsync({ id: sched.id, body: sched }); setSched(null); };
  const runNow = async (id: string) => { await post(`/api/stocktake-schedules/${id}/run`); schedules.refetch(); };
  const submit = async (e: FormEvent) => { e.preventDefault(); const s = await create.mutateAsync({ name: f.name || `Stocktake ${new Date().toLocaleDateString()}`, locationId: f.locationId, itemTypeId: f.itemTypeId || undefined }); setShow(false); nav(`/stocktakes/${s.id}`); };
  return (
    <div>
      <div className="topbar"><h1>Stocktakes</h1><button className="primary" onClick={() => setShow(true)}>+ New stocktake</button></div>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>Name</th><th>Location</th><th>Status</th><th>Expected</th><th>Found</th><th>Missing</th><th>Unexpected</th><th>Started</th></tr></thead>
          <tbody>{data?.map((s) => <tr key={s.summary.id} className="clickable" onClick={() => nav(`/stocktakes/${s.summary.id}`)}>
            <td><b>{s.summary.name}</b></td><td>{s.location}</td><td><Badge tone={toneForStatus(s.summary.status)}>{s.summary.status}</Badge></td><td>{s.summary.expected}</td><td className="ok">{s.summary.found}</td><td>{s.summary.missing > 0 ? <Badge tone="crit">{s.summary.missing}</Badge> : 0}</td><td>{s.summary.unexpected > 0 ? <Badge tone="warn">{s.summary.unexpected}</Badge> : 0}</td><td className="small">{fmt.dt(s.startedAt)}</td>
          </tr>)}</tbody>
        </table>
        {data?.length === 0 && <div className="empty">No stocktakes yet.</div>}
      </div>
      <div className="panel" style={{ marginTop: 16 }}>
        <div className="topbar"><h3>Scheduled stocktakes</h3><button className="sm" onClick={() => setSched({ name: '', intervalDays: 7, timeOfDay: '06:00', enabled: true, autoReconcileHours: 24 })}>+ Schedule</button></div>
        <table><thead><tr><th>Name</th><th>Location</th><th>Every</th><th>At (UTC)</th><th>Next run</th><th>Last run</th><th>Auto-reconcile</th><th></th></tr></thead>
          <tbody>{schedules.data?.map((s) => <tr key={s.id}><td><b>{s.name}</b>{!s.enabled && <Badge>paused</Badge>}</td><td>{s.location}</td><td>{s.intervalDays} d</td><td>{s.timeOfDay}</td><td className="small">{fmt.dt(s.nextRunAt)}</td><td className="small">{s.lastStocktakeId ? <Link to={`/stocktakes/${s.lastStocktakeId}`}>{fmt.ago(s.lastRunAt)}</Link> : '—'}</td><td>{s.autoReconcileHours ? `${s.autoReconcileHours} h` : '—'}</td>
            <td className="right"><button className="sm" onClick={() => runNow(s.id)}>Run now</button> <button className="sm" onClick={() => setSched(s)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete schedule?') && removeSched.mutate(s.id)}>Delete</button></td></tr>)}</tbody></table>
        {schedules.data?.length === 0 && <span className="muted">No schedules. A background service opens scheduled stocktakes automatically and can reconcile them after a window.</span>}
      </div>
      {sched && <Modal title={sched.id ? 'Edit schedule' : 'New schedule'} onClose={() => setSched(null)}>
        <form className="form" onSubmit={submitSched}>
          <label>Name *<input required value={sched.name ?? ''} onChange={(e) => setSched({ ...sched, name: e.target.value })} /></label>
          <label>Location *<select required value={sched.locationId ?? ''} onChange={(e) => setSched({ ...sched, locationId: e.target.value })}><option value="">—</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name}</option>)}</select></label>
          <label>Item type<select value={sched.itemTypeId ?? ''} onChange={(e) => setSched({ ...sched, itemTypeId: e.target.value || null })}><option value="">All</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
          <div className="form cols-2"><label>Every (days)<input type="number" min={1} value={sched.intervalDays ?? 7} onChange={(e) => setSched({ ...sched, intervalDays: Number(e.target.value) })} /></label><label>At (UTC, HH:MM)<input value={sched.timeOfDay ?? '06:00'} onChange={(e) => setSched({ ...sched, timeOfDay: e.target.value })} /></label>
            <label>Auto-reconcile after (hours)<input type="number" value={sched.autoReconcileHours ?? ''} onChange={(e) => setSched({ ...sched, autoReconcileHours: e.target.value ? Number(e.target.value) : null })} /></label><label className="row" style={{ alignSelf: 'end' }}><input type="checkbox" style={{ width: 'auto' }} checked={!!sched.enabled} onChange={(e) => setSched({ ...sched, enabled: e.target.checked })} /> Enabled</label></div>
          <ErrorBox error={saveSched.error} />
          <div className="row end"><button type="button" onClick={() => setSched(null)}>Cancel</button><button className="primary">Save</button></div>
        </form>
      </Modal>}
      {show && <Modal title="New stocktake" onClose={() => setShow(false)}>
        <form className="form" onSubmit={submit}>
          <label>Name<input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder="e.g. Ward 3 weekly count" /></label>
          <label>Location (includes sub-locations) *<select required value={f.locationId} onChange={(e) => setF({ ...f, locationId: e.target.value })}><option value="">—</option>{locs.data?.map((l) => <option key={l.id} value={l.id}>{' '.repeat((l.path.split('/').length - 3) * 2)}{l.name} · {l.itemCount ?? 0} items</option>)}</select></label>
          <label>Item type (optional)<select value={f.itemTypeId} onChange={(e) => setF({ ...f, itemTypeId: e.target.value })}><option value="">All types</option>{types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
          <ErrorBox error={create.error} />
          <div className="row end"><button className="primary" disabled={create.isPending}>Start</button></div>
        </form>
      </Modal>}
    </div>
  );
}

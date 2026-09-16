import { useEffect, useState } from 'react';
import { del, post, put } from '../api/client';
import { useBatches, useDevices, useInvalidate, useItemTypes, usePools, useSchemes } from '../api/hooks';
import type { EncodingBatch, EncodingPreview, EncodingRequest, SerialPool } from '../api/types';
import { useAuth } from '../auth';
import { Badge, ErrorBox, Modal, fmt, useDebounce } from '../components/ui';

const schemeLabel: Record<string, string> = { Sgtin96: 'SGTIN-96', Grai96: 'GRAI-96', Giai96: 'GIAI-96', Sscc96: 'SSCC-96' };

export default function Encoding() {
  const { user } = useAuth(); const isAdmin = user?.role === 'Admin';
  const pools = usePools(); const batches = useBatches(); const itemTypes = useItemTypes(); const devices = useDevices(); const inv = useInvalidate();
  const [tab, setTab] = useState<'wizard' | 'pools' | 'batches'>('wizard');
  const [req, setReq] = useState<EncodingRequest>({ scheme: 'Sgtin96', companyPrefix: '0614141', reference: '', filter: 1, poolId: null, firstSerial: 1, count: 10, mode: 'tags', itemTypeId: null, identifierPrefix: '', printerDeviceId: null });
  const [step, setStep] = useState(1); const [preview, setPreview] = useState<EncodingPreview | null>(null); const [error, setError] = useState<unknown>(null); const [done, setDone] = useState<EncodingBatch | null>(null); const [busy, setBusy] = useState(false);
  const schemes = useSchemes(req.poolId ? (pools.data?.find((p) => p.id === req.poolId)?.companyPrefix ?? req.companyPrefix) : req.companyPrefix);
  const scheme = schemes.data?.find((s) => s.scheme === (req.poolId ? pools.data?.find((p) => p.id === req.poolId)?.scheme : req.scheme));
  const debounced = useDebounce(req, 400);
  useEffect(() => { if (step < 3) return; setError(null); post<EncodingPreview>('/api/encoding/preview', debounced).then(setPreview).catch((e) => { setPreview(null); setError(e); }); }, [debounced, step]);
  const commit = async () => { setBusy(true); setError(null); try { const b = await post<EncodingBatch>('/api/encoding/commit', req); setDone(b); inv('batches', 'pools', 'tags', 'items'); } catch (e) { setError(e); } finally { setBusy(false); } };
  const [pool, setPool] = useState<Partial<SerialPool> | null>(null);
  const savePool = async () => { if (!pool) return; setError(null); try { if (pool.id) await put(`/api/encoding/pools/${pool.id}`, pool); else await post('/api/encoding/pools', pool); inv('pools'); setPool(null); } catch (e) { setError(e); } };
  const selectedPool = pools.data?.find((p) => p.id === req.poolId);
  const printers = devices.data?.filter((d) => d.kind === 'Printer') ?? [];
  return (
    <div>
      <div className="topbar"><h1>GS1 encoding</h1><span className="muted small">Encode thousands of EPCs from company prefixes and serial pools · create tags, bind items or create items · queue labels</span></div>
      <div className="tabs">{(['wizard', 'pools', 'batches'] as const).map((t) => <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t === 'wizard' ? 'Encoding wizard' : t === 'pools' ? 'Serial pools' : 'Batches'}</button>)}</div>
      {tab === 'wizard' && (done ? <div className="panel"><h2>✓ Batch “{done.name}” committed</h2><p>{done.count} EPCs · {done.tagsCreated} tags created · {done.itemsBound} items bound{done.printJobs ? ` · ${done.printJobs} labels queued` : ''}</p><p className="mono small">{done.firstEpc} … {done.lastEpc}</p><div className="row"><button className="primary" onClick={() => { setDone(null); setStep(1); setPreview(null); }}>Encode another batch</button><button onClick={() => setTab('batches')}>View batches</button></div></div> : <>
        <div className="row" style={{ marginBottom: 12 }}>{['Scheme & prefix', 'Serials', 'Target', 'Preview & commit'].map((label, i) => <span key={label} className={'badge ' + (step === i + 1 ? 'info' : step > i + 1 ? 'ok' : '')} style={{ cursor: 'pointer' }} onClick={() => i + 1 < step && setStep(i + 1)}>{i + 1}. {label}</span>)}</div>
        <div className="grid" style={{ gridTemplateColumns: '1fr 360px' }}>
          <div className="panel">
            {step === 1 && <div className="form">
              <label className="row"><input type="radio" style={{ width: 'auto' }} checked={!req.poolId} onChange={() => setReq({ ...req, poolId: null })} /> Enter scheme and prefix manually</label>
              {!req.poolId && <div className="form cols-2">
                <label>Scheme<select value={req.scheme} onChange={(e) => setReq({ ...req, scheme: e.target.value, reference: '', filter: schemes.data?.find((s) => s.scheme === e.target.value)?.filterDefault ?? 0 })}>{schemes.data?.map((s) => <option key={s.scheme} value={s.scheme}>{s.label}</option>)}</select></label>
                <label>GS1 company prefix (6–12 digits)<input value={req.companyPrefix} onChange={(e) => setReq({ ...req, companyPrefix: e.target.value.replace(/\D/g, '') })} className="mono" /></label>
                {scheme && scheme.referenceDigits > 0 && <label>{scheme.referenceLabel} · {scheme.referenceDigits} digit(s)<input value={req.reference} maxLength={scheme.referenceDigits} onChange={(e) => setReq({ ...req, reference: e.target.value.replace(/\D/g, '') })} className="mono" /></label>}
                <label>Filter value<input type="number" min={0} max={7} value={req.filter} onChange={(e) => setReq({ ...req, filter: Number(e.target.value) })} /></label>
              </div>}
              <label className="row"><input type="radio" style={{ width: 'auto' }} checked={!!req.poolId} onChange={() => setReq({ ...req, poolId: pools.data?.[0]?.id ?? null })} /> Use a serial pool (recommended – serials are never reused, even across users and handhelds)</label>
              {req.poolId && <select value={req.poolId} onChange={(e) => setReq({ ...req, poolId: e.target.value })}>{pools.data?.map((p) => <option key={p.id} value={p.id}>{p.name} · {schemeLabel[p.scheme]} · {p.companyPrefix}{p.reference ? '/' + p.reference : ''} · next {p.nextSerial}</option>)}</select>}
              {pools.data?.length === 0 && <span className="muted small">No pools yet – create one on the Serial pools tab.</span>}
            </div>}
            {step === 2 && <div className="form cols-2">
              {!req.poolId && <label>First serial<input type="number" min={0} value={req.firstSerial ?? 1} onChange={(e) => setReq({ ...req, firstSerial: Number(e.target.value) })} /></label>}
              {req.poolId && <div><span className="muted small">Serials come from the pool</span><div><b>{selectedPool?.name}</b> · next serial {selectedPool?.nextSerial}{selectedPool?.remaining != null ? ` · ${selectedPool.remaining} left` : ''}</div></div>}
              <label>How many EPCs<input type="number" min={1} max={100000} value={req.count} onChange={(e) => setReq({ ...req, count: Number(e.target.value) })} /></label>
              {scheme && <p className="muted small" style={{ gridColumn: '1 / -1' }}>{scheme.label}: serial field {scheme.serialBits} {req.scheme === 'Sscc96' && !req.poolId ? 'digits' : 'bits'}. Duplicates against existing tags are checked before commit.</p>}
            </div>}
            {step === 3 && <div className="form">
              <label className="row"><input type="radio" style={{ width: 'auto' }} checked={req.mode === 'tags'} onChange={() => setReq({ ...req, mode: 'tags' })} /> <span><b>Create unassigned tags</b> – pre-encode a roll; bind to items later from the handheld or the Tags page</span></label>
              <label className="row"><input type="radio" style={{ width: 'auto' }} checked={req.mode === 'bind'} onChange={() => setReq({ ...req, mode: 'bind' })} /> <span><b>Bind to existing items without a tag</b> – of an item type, in identifier order</span></label>
              <label className="row"><input type="radio" style={{ width: 'auto' }} checked={req.mode === 'items'} onChange={() => setReq({ ...req, mode: 'items' })} /> <span><b>Create items and tags</b> – one item per EPC, identifier = prefix + serial</span></label>
              <div className="form cols-2">
                {req.mode !== 'tags' && <label>Item type{req.mode === 'bind' ? ' (items of this type with no active tag)' : ' *'}<select value={req.itemTypeId ?? ''} onChange={(e) => setReq({ ...req, itemTypeId: e.target.value || null })}><option value="">—</option>{itemTypes.data?.map((t) => <option key={t.id} value={t.id}>{t.name} ({t.code})</option>)}</select></label>}
                {req.mode === 'items' && <label>Identifier prefix<input value={req.identifierPrefix ?? ''} onChange={(e) => setReq({ ...req, identifierPrefix: e.target.value })} placeholder="e.g. TOTE-" /></label>}
                {req.mode !== 'tags' && <label>Queue labels on printer<select value={req.printerDeviceId ?? ''} onChange={(e) => setReq({ ...req, printerDeviceId: e.target.value || null })}><option value="">— no labels —</option>{printers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>}
                <label>Batch name<input value={req.name ?? ''} onChange={(e) => setReq({ ...req, name: e.target.value })} placeholder="auto" /></label>
              </div>
            </div>}
            {step === 4 && <div>
              {preview ? <>
                <div className="grid cols-3" style={{ marginBottom: 12 }}><div className="stat"><span className="label">EPCs</span><span className="value">{preview.count}</span></div><div className="stat"><span className="label">Serials</span><span className="value" style={{ fontSize: 18 }}>{preview.firstSerial} – {preview.lastSerial}</span></div><div className={'stat ' + (preview.duplicates > 0 ? 'crit' : 'ok')}><span className="label">Duplicates</span><span className="value">{preview.duplicates}</span></div></div>
                {preview.warnings.map((w) => <div key={w} className={preview.duplicates > 0 && w.includes('already') ? 'error' : 'panel'} style={{ marginBottom: 8, padding: 8 }}>{w}</div>)}
                <h4 style={{ margin: '8px 0 4px' }}>Sample</h4><div className="mono small" style={{ columns: 2 }}>{preview.sample.map((e) => <div key={e}>{e}</div>)}{preview.count > preview.sample.length && <div className="muted">… {preview.count - preview.sample.length} more</div>}</div>
              </> : <span className="muted">Computing preview…</span>}
            </div>}
            <ErrorBox error={error} />
            <div className="row end" style={{ marginTop: 12 }}>{step > 1 && <button onClick={() => setStep(step - 1)}>Back</button>}{step < 4 && <button className="primary" onClick={() => setStep(step + 1)} disabled={step === 1 && !req.poolId && (req.companyPrefix.length < 6 || (scheme?.referenceDigits ?? 0) !== req.reference.length)}>Next</button>}{step === 4 && <button className="primary" disabled={busy || !preview || preview.duplicates > 0 || (req.mode === 'bind' && preview.targetItems === 0) || (req.mode === 'items' && !req.itemTypeId)} onClick={commit}>{busy ? 'Encoding…' : `Commit ${preview?.count ?? ''} EPCs`}</button>}</div>
          </div>
          <div className="panel"><h3>Summary</h3><dl className="kv"><dt>Scheme</dt><dd>{schemeLabel[selectedPool?.scheme ?? req.scheme]}</dd><dt>Prefix</dt><dd className="mono">{selectedPool?.companyPrefix ?? req.companyPrefix}{(selectedPool?.reference ?? req.reference) ? ' / ' + (selectedPool?.reference ?? req.reference) : ''}</dd><dt>Filter</dt><dd>{selectedPool?.filter ?? req.filter}</dd><dt>Serials</dt><dd>{req.poolId ? `pool ${selectedPool?.name ?? ''}` : `from ${req.firstSerial}`} × {req.count}</dd><dt>Target</dt><dd>{req.mode === 'tags' ? 'unassigned tags' : req.mode === 'bind' ? 'bind to items' : 'new items'}</dd></dl>
            <p className="muted small" style={{ marginTop: 10 }}>EPCs follow the GS1 Tag Data Standard 96-bit encodings. Decode any EPC with <code>GET /api/encoding/decode/{'{epc}'}</code>. Handhelds can pull the next serial from a pool while commissioning.</p></div>
        </div>
      </>)}
      {tab === 'pools' && <>
        <div className="topbar"><p className="muted small" style={{ margin: 0 }}>A pool fixes the scheme, company prefix, reference and filter, and hands out serials atomically – web wizard, handhelds and external systems all draw from the same counter.</p>{isAdmin && <button className="primary" onClick={() => setPool({ name: '', scheme: 'Sgtin96', companyPrefix: '0614141', reference: '', filter: 1, nextSerial: 1 })}>+ New pool</button>}</div>
        <div className="panel table-wrap"><table><thead><tr><th>Pool</th><th>Scheme</th><th>Prefix / reference</th><th>Next serial</th><th>Remaining</th><th>Next EPC</th><th></th></tr></thead>
          <tbody>{pools.data?.map((p) => <tr key={p.id}><td><b>{p.name}</b>{p.itemType && <div className="muted small">{p.itemType}</div>}</td><td><Badge>{schemeLabel[p.scheme]}</Badge></td><td className="mono">{p.companyPrefix}{p.reference ? ' / ' + p.reference : ''} · f{p.filter}</td><td>{p.nextSerial}</td><td>{p.remaining ?? '∞'}</td><td className="mono small">{p.sampleEpc}</td><td className="right">{isAdmin && <><button className="sm" onClick={() => setPool(p)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete pool?') && del(`/api/encoding/pools/${p.id}`).then(() => inv('pools'))}>✕</button></>}</td></tr>)}
            {pools.data?.length === 0 && <tr><td colSpan={7} className="muted">No pools yet.</td></tr>}</tbody></table></div>
      </>}
      {tab === 'batches' && <div className="panel table-wrap"><table><thead><tr><th>Batch</th><th>Scheme</th><th>Serials</th><th>Mode</th><th>Result</th><th>EPC range</th><th>When</th></tr></thead>
        <tbody>{batches.data?.map((b) => <tr key={b.id}><td><b>{b.name}</b>{b.user && <div className="muted small">{b.user}</div>}</td><td><Badge>{schemeLabel[b.scheme]}</Badge> <span className="mono small">{b.companyPrefix}{b.reference ? '/' + b.reference : ''}</span></td><td>{b.firstSerial}–{b.lastSerial}</td><td>{b.mode}</td><td className="small">{b.tagsCreated} tags{b.itemsBound ? ` · ${b.itemsBound} items` : ''}{b.printJobs ? ` · ${b.printJobs} labels` : ''}</td><td className="mono small">{b.firstEpc}<br />{b.lastEpc}</td><td className="small muted">{fmt.ago(b.createdAt)}</td></tr>)}
          {batches.data?.length === 0 && <tr><td colSpan={7} className="muted">No batches yet.</td></tr>}</tbody></table></div>}
      {pool && <Modal title={pool.id ? 'Edit pool' : 'New serial pool'} onClose={() => setPool(null)}>
        <form className="form" onSubmit={(e) => { e.preventDefault(); savePool(); }}>
          <div className="form cols-2">
            <label>Name *<input required value={pool.name ?? ''} onChange={(e) => setPool({ ...pool, name: e.target.value })} /></label>
            <label>Scheme<select value={pool.scheme} disabled={!!pool.id} onChange={(e) => setPool({ ...pool, scheme: e.target.value })}>{Object.entries(schemeLabel).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
            <label>Company prefix<input className="mono" disabled={!!pool.id} value={pool.companyPrefix ?? ''} onChange={(e) => setPool({ ...pool, companyPrefix: e.target.value.replace(/\D/g, '') })} /></label>
            <label>Reference (item ref / asset type / extension digit)<input className="mono" disabled={!!pool.id} value={pool.reference ?? ''} onChange={(e) => setPool({ ...pool, reference: e.target.value.replace(/\D/g, '') })} /></label>
            <label>Filter<input type="number" min={0} max={7} value={pool.filter ?? 0} onChange={(e) => setPool({ ...pool, filter: Number(e.target.value) })} /></label>
            <label>Next serial<input type="number" min={0} value={pool.nextSerial ?? 1} onChange={(e) => setPool({ ...pool, nextSerial: Number(e.target.value) })} /></label>
            <label>Max serial (blank = unlimited)<input type="number" min={0} value={pool.maxSerial ?? ''} onChange={(e) => setPool({ ...pool, maxSerial: e.target.value ? Number(e.target.value) : null })} /></label>
            <label>Item type (optional)<select value={pool.itemTypeId ?? ''} onChange={(e) => setPool({ ...pool, itemTypeId: e.target.value || null })}><option value="">any</option>{itemTypes.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
          </div>
          <label>Notes<input value={pool.notes ?? ''} onChange={(e) => setPool({ ...pool, notes: e.target.value })} /></label>
          <ErrorBox error={error} /><div className="row end"><button type="button" onClick={() => setPool(null)}>Cancel</button><button className="primary">Save</button></div>
        </form></Modal>}
    </div>
  );
}

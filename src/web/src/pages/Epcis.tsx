import { useState } from 'react';
import { api, getToken } from '../api/client';
import { useEpcisCaptures, useInvalidate } from '../api/hooks';
import { Badge, ErrorBox, fmt } from '../components/ui';

const sample = `{
  "@context": ["https://ref.gs1.org/standards/epcis/2.0.0/epcis-context.jsonld"],
  "type": "EPCISDocument", "schemaVersion": "2.0", "creationDate": "2026-09-16T10:00:00Z",
  "epcisBody": { "eventList": [
    { "type": "ObjectEvent", "eventTime": "2026-09-16T10:00:00Z", "eventTimeZoneOffset": "+00:00", "action": "OBSERVE",
      "bizStep": "receiving", "disposition": "in_progress",
      "epcList": ["urn:epc:id:sgtin:0614141.812345.6789"],
      "readPoint": { "id": "urn:epc:id:sgln:0614141.00001.0" }, "bizLocation": { "id": "urn:epc:id:sgln:0614141.00001.0" },
      "bizTransactionList": [{ "type": "po", "bizTransaction": "PO-1001" }] }
  ] }
}`;

export default function Epcis() {
  const captures = useEpcisCaptures(); const inv = useInvalidate();
  const [q, setQ] = useState({ bizStep: '', action: '', epc: '', hours: 24 }); const [result, setResult] = useState<string | null>(null); const [busy, setBusy] = useState(false); const [error, setError] = useState<unknown>(null);
  const [doc, setDoc] = useState(sample); const [captureResult, setCaptureResult] = useState<string | null>(null);
  const run = async () => {
    setBusy(true); setError(null);
    try {
      const params = new URLSearchParams(); if (q.bizStep) params.set('EQ_bizStep', q.bizStep); if (q.action) params.set('EQ_action', q.action); if (q.epc) params.set('MATCH_epc', q.epc); params.set('GE_eventTime', new Date(Date.now() - q.hours * 3600e3).toISOString()); params.set('perPage', '20');
      const res = await fetch(`/api/epcis/v2/events?${params}`, { headers: { Authorization: `Bearer ${getToken()}` } });
      setResult(`HTTP ${res.status} · X-Total-Count ${res.headers.get('X-Total-Count')} · GS1-EPCIS-Version ${res.headers.get('GS1-EPCIS-Version')}\n\n${await res.text()}`);
    } catch (e) { setError(e); } finally { setBusy(false); }
  };
  const capture = async () => { setBusy(true); setError(null); try { const r = await api<{ captureID: string; events: number; applied: number; rejected: number; errors: string[] }>('/api/epcis/v2/capture', { method: 'POST', body: doc, headers: { 'Content-Type': 'application/json' } }); setCaptureResult(`Capture ${r.captureID}: ${r.events} event(s), ${r.applied} applied, ${r.rejected} rejected${r.errors.length ? '\n' + r.errors.join('\n') : ''}`); inv('epcis-captures', 'events', 'items'); } catch (e) { setError(e); } finally { setBusy(false); } };
  return (
    <div>
      <div className="topbar"><h1>EPCIS 2.0</h1><span className="muted small">GS1 EPCIS 2.0 JSON/JSON-LD capture and query interface over the item event log</span></div>
      <p className="muted small">Every item event is exposed as an EPCIS <code>ObjectEvent</code> (or <code>AggregationEvent</code> for pack/unpack) with CBV business steps and dispositions, EPC URNs (<code>urn:epc:id:sgtin/grai/giai/sscc</code>), SGLN read points (set a location's <code>sgln</code> attribute) and business transactions from operation references. Trading partners can push their own EPCISDocuments to the capture endpoint: OBSERVE events become reads, receiving/shipping events with a bizLocation become operations.</p>
      <div className="grid cols-2">
        <div className="panel"><h3>Query · GET /api/epcis/v2/events</h3>
          <div className="form cols-2">
            <label>EQ_bizStep<select value={q.bizStep} onChange={(e) => setQ({ ...q, bizStep: e.target.value })}><option value="">any</option>{['commissioning', 'receiving', 'shipping', 'storing', 'inspecting', 'cycle_counting', 'repairing', 'destroying', 'packing', 'unpacking', 'transforming', 'arriving', 'departing'].map((b) => <option key={b}>{b}</option>)}</select></label>
            <label>EQ_action<select value={q.action} onChange={(e) => setQ({ ...q, action: e.target.value })}><option value="">any</option><option>ADD</option><option>OBSERVE</option><option>DELETE</option></select></label>
            <label>MATCH_epc (hex or URN)<input className="mono" value={q.epc} onChange={(e) => setQ({ ...q, epc: e.target.value })} placeholder="3034257BF4…" /></label>
            <label>GE_eventTime<select value={q.hours} onChange={(e) => setQ({ ...q, hours: Number(e.target.value) })}>{[1, 24, 168, 720, 8760].map((h) => <option key={h} value={h}>last {h < 24 ? `${h} h` : `${h / 24} d`}</option>)}</select></label>
          </div>
          <div className="row end" style={{ marginTop: 8 }}><button className="primary" disabled={busy} onClick={run}>Run query</button></div>
          {result && <pre className="small" style={{ maxHeight: 420, overflow: 'auto', whiteSpace: 'pre-wrap', wordBreak: 'break-all', background: 'var(--bg)', padding: 8, borderRadius: 6 }}>{result}</pre>}
        </div>
        <div className="panel"><h3>Capture · POST /api/epcis/v2/capture</h3>
          <textarea className="mono small" rows={16} value={doc} onChange={(e) => setDoc(e.target.value)} style={{ width: '100%' }} />
          <div className="row end" style={{ marginTop: 8 }}><button onClick={() => setDoc(sample)}>Reset sample</button><button className="primary" disabled={busy} onClick={capture}>Capture document</button></div>
          {captureResult && <pre className="small" style={{ whiteSpace: 'pre-wrap' }}>{captureResult}</pre>}
          <ErrorBox error={error} />
          <h4 style={{ margin: '12px 0 4px' }}>Recent captures</h4>
          <table><tbody>{captures.data?.map((c) => <tr key={c.id}><td className="small muted">{fmt.ago(c.createdAt)}</td><td className="small">{c.events} events · {c.applied} applied · {c.rejected} rejected</td><td><Badge tone={c.status === 'Completed' ? 'ok' : c.status === 'Partial' ? 'warn' : 'crit'}>{c.status}</Badge></td></tr>)}{captures.data?.length === 0 && <tr><td className="muted">None yet</td></tr>}</tbody></table>
        </div>
      </div>
      <div className="panel" style={{ marginTop: 16 }}><h3>Endpoints</h3><table><tbody>
        <tr><td className="mono small">GET /api/epcis/v2</td><td className="small">Discovery: versions, resources, supported query parameters</td></tr>
        <tr><td className="mono small">GET /api/epcis/v2/events?EQ_bizStep&amp;EQ_disposition&amp;EQ_action&amp;MATCH_epc&amp;GE_eventTime&amp;LT_eventTime&amp;eventType&amp;EQ_readPoint&amp;perPage&amp;page</td><td className="small">Simple event query → EPCISDocument (application/ld+json, X-Total-Count, Link rel=next)</td></tr>
        <tr><td className="mono small">GET /api/epcis/v2/events/{'{eventId}'}</td><td className="small">One event by urn:uuid</td></tr>
        <tr><td className="mono small">GET /api/epcis/v2/epcs/{'{epc}'}/events</td><td className="small">History of one EPC</td></tr>
        <tr><td className="mono small">POST /api/epcis/v2/capture · GET /api/epcis/v2/capture/{'{captureID}'}</td><td className="small">Capture interface (synchronous) and job status</td></tr>
      </tbody></table></div>
    </div>
  );
}

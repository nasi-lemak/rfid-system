import { useState } from 'react';
import { getToken } from '../api/client';
import { useReport, useReportCatalog } from '../api/hooks';

export default function Reports() {
  const catalog = useReportCatalog();
  const [code, setCode] = useState('inventory');
  const [days, setDays] = useState('');
  const params = days ? { days } : {};
  const report = useReport(code, params);
  const fmtCell = (v: unknown) => v == null ? '' : typeof v === 'boolean' ? (v ? '✓' : '') : typeof v === 'string' && /^\d{4}-\d\d-\d\dT/.test(v) ? new Date(v).toLocaleString() : String(v);
  const download = async () => {
    const q = new URLSearchParams({ format: 'csv', ...(days ? { days } : {}) });
    const res = await fetch(`/api/reports/${code}?${q}`, { headers: { Authorization: `Bearer ${getToken()}` } });
    const blob = await res.blob(); const a = document.createElement('a'); a.href = URL.createObjectURL(blob); a.download = `${code}.csv`; a.click(); URL.revokeObjectURL(a.href);
  };
  const def = catalog.data?.find((r) => r.code === code);
  return (
    <div>
      <div className="topbar"><h1>Reports</h1><div className="row"><input placeholder="days" value={days} onChange={(e) => setDays(e.target.value)} style={{ width: 80 }} title="Window in days for missing / expiry / inspection / dwell" /><button className="primary" onClick={download}>Download CSV</button></div></div>
      <div className="grid" style={{ gridTemplateColumns: '260px 1fr' }}>
        <div className="panel">{catalog.data?.map((r) => <div key={r.code} className={'node' + (r.code === code ? ' active' : '')} style={{ padding: '8px 10px', borderRadius: 6, cursor: 'pointer', background: r.code === code ? 'color-mix(in srgb, var(--primary) 10%, transparent)' : undefined }} onClick={() => setCode(r.code)}><b>{r.name}</b><div className="muted small">{r.description}</div></div>)}</div>
        <div className="panel table-wrap">
          <div className="topbar"><h2>{def?.name}</h2><span className="muted small">{report.data?.count ?? 0} rows · also available as <code>GET /api/reports/{code}?format=csv</code> for BI / ERP pulls</span></div>
          <table><thead><tr>{report.data?.columns.map((c) => <th key={c}>{c}</th>)}</tr></thead><tbody>{report.data?.rows.slice(0, 500).map((r, i) => <tr key={i}>{r.map((v, j) => <td key={j} className="small">{fmtCell(v)}</td>)}</tr>)}</tbody></table>
          {report.data?.count === 0 && <div className="empty">No rows.</div>}
        </div>
      </div>
    </div>
  );
}

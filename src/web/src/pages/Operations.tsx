import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { get } from '../api/client';
import { useOperationDefinitions, useOperations } from '../api/hooks';
import { Badge, Modal, Pager, fmt, toneForStatus } from '../components/ui';
import { OperationForm } from '../components/OperationForm';

export default function Operations() {
  const [sp, setSp] = useSearchParams();
  const [page, setPage] = useState(1);
  const [showNew, setShowNew] = useState(false);
  const [type, setType] = useState('');
  const defs = useOperationDefinitions();
  const { data } = useOperations({ page, pageSize: 50, operation: type });
  const id = sp.get('id');
  const detail = useQuery({ queryKey: ['operation', id], queryFn: () => get<{ id: string; type: string; reference?: string; notes?: string; startedAt: string; lines: { id: string; epc?: string; itemName?: string; itemIdentifier?: string; quantity?: number; result: string; message?: string }[] }>(`/api/operations/${id}`), enabled: !!id });
  return (
    <div>
      <div className="topbar"><h1>Operations</h1><div className="row"><select value={type} onChange={(e) => { setType(e.target.value); setPage(1); }}><option value="">All operations</option>{(defs.data ?? []).map((d) => <option key={d.code} value={d.code}>{d.name}{d.isBuiltIn ? '' : ` · ${d.vertical ?? 'custom'}`}</option>)}</select><button className="primary" onClick={() => setShowNew(true)}>+ Run operation</button></div></div>
      <div className="panel table-wrap">
        <table>
          <thead><tr><th>When</th><th>Type</th><th>From</th><th>To</th><th>Party</th><th>Reference</th><th>Lines</th><th>User</th></tr></thead>
          <tbody>{data?.items.map((o) => <tr key={o.id} className="clickable" onClick={() => setSp({ id: o.id })}>
            <td className="small">{fmt.dt(o.startedAt)}</td><td><Badge>{o.definitionCode && o.definitionCode !== o.type ? o.definitionCode : o.type}</Badge>{o.targetState && <span className="muted small"> → {o.targetState}</span>}</td><td>{o.fromLocation ?? '—'}</td><td>{o.toLocation ?? '—'}</td><td>{o.party ?? '—'}</td><td>{o.reference ?? ''}</td>
            <td><Badge tone="ok">{o.ok}</Badge> {o.rejected > 0 && <Badge tone="warn">{o.rejected} rejected</Badge>} {o.unknown > 0 && <Badge tone="info">{o.unknown} unknown</Badge>}</td><td className="small">{o.user ?? (o.notes?.startsWith('client:') ? 'handheld' : '')}</td>
          </tr>)}</tbody>
        </table>
        {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />}
      </div>
      {showNew && <Modal title="Run operation" onClose={() => setShowNew(false)}><OperationForm onDone={() => setShowNew(false)} /></Modal>}
      {id && detail.data && <Modal title={`${detail.data.type} · ${fmt.dt(detail.data.startedAt)}`} onClose={() => setSp({})}>
        {detail.data.reference && <p className="muted">Ref: {detail.data.reference}</p>}
        <table><thead><tr><th>EPC</th><th>Item</th><th>Qty</th><th>Result</th></tr></thead><tbody>{detail.data.lines.map((l) => <tr key={l.id}><td className="mono">{l.epc}</td><td>{l.itemName} <span className="muted small">{l.itemIdentifier}</span></td><td>{l.quantity ?? ''}</td><td><Badge tone={toneForStatus(l.result)}>{l.result}</Badge> <span className="small muted">{l.message}</span></td></tr>)}</tbody></table>
      </Modal>}
    </div>
  );
}

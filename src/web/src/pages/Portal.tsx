import { useState } from 'react';
import { getToken } from '../api/client';
import { usePortalActivity, usePortalInvoices, usePortalItems, usePortalLedger, usePortalMe } from '../api/hooks';
import { useAuth } from '../auth';
import { Badge, Pager, fmt, useDebounce } from '../components/ui';
import { LANGS, useI18n } from '../i18n';

/** Read-only supplier / customer portal: what is in their custody, what moved, what they owe. */
export default function Portal() {
  const { user, logout } = useAuth(); const { t, lang, setLang } = useI18n();
  const me = usePortalMe();
  const [tab, setTab] = useState<'overview' | 'items' | 'activity' | 'invoices'>('overview');
  const [q, setQ] = useState(''); const [custody, setCustody] = useState(true); const [page, setPage] = useState(1); const dq = useDebounce(q);
  const items = usePortalItems({ q: dq, inCustody: custody ? true : undefined, page, pageSize: 50 }); const activity = usePortalActivity(); const invoices = usePortalInvoices(); const ledger = usePortalLedger();
  const money = (v: number, c: string) => `${v.toLocaleString(undefined, { minimumFractionDigits: 2 })} ${c}`;
  const openPrint = async (id: string) => { const res = await fetch(`/api/portal/invoices/${id}/print`, { headers: { Authorization: `Bearer ${getToken()}` } }); const w = window.open('', '_blank'); if (w) { w.document.write(await res.text()); w.document.close(); } };
  return (
    <div className="app" style={{ gridTemplateColumns: '1fr' }}>
      <main className="main" style={{ maxWidth: 1100, margin: '0 auto', width: '100%' }}>
        <div className="topbar"><div><h1 style={{ margin: 0 }}>{me.data?.party.name ?? 'Portal'}</h1><span className="muted small">{me.data?.party.kind} portal · {user?.displayName}</span></div><div className="row"><select value={lang} onChange={(e) => setLang(e.target.value as typeof lang)}>{LANGS.map((l) => <option key={l.code} value={l.code}>{l.label}</option>)}</select><button onClick={logout}>{t('Sign out')}</button></div></div>
        <div className="tabs">{(['overview', 'items', 'activity', 'invoices'] as const).map((x) => <button key={x} className={tab === x ? 'active' : ''} onClick={() => setTab(x)}>{x === 'overview' ? 'Overview' : x === 'items' ? t('Items') : x === 'activity' ? 'Activity' : 'Invoices'}</button>)}</div>
        {tab === 'overview' && me.data && <>
          <div className="grid cols-4" style={{ marginBottom: 16 }}>
            <div className="panel stat"><span className="label">Items in your custody</span><span className="value">{me.data.inCustody}</span></div>
            <div className={'panel stat ' + (me.data.overdue > 0 ? 'crit' : '')}><span className="label">Overdue for return</span><span className="value">{me.data.overdue}</span></div>
            <div className={'panel stat ' + (me.data.dueSoon > 0 ? 'warn' : '')}><span className="label">Due back within 7 days</span><span className="value">{me.data.dueSoon}</span></div>
            <div className={'panel stat ' + (me.data.outstanding > 0 ? 'warn' : '')}><span className="label">Outstanding invoices</span><span className="value">{me.data.openInvoices}</span><span className="muted small">{me.data.outstanding.toFixed(2)} due · deposits held {me.data.depositsHeld.toFixed(2)}</span></div>
          </div>
          <div className="grid cols-2">
            <div className="panel"><h3>Recent activity</h3>{activity.data?.slice(0, 8).map((a) => <div key={a.id} className="row" style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><Badge tone={a.direction.startsWith('Issued') ? 'info' : 'ok'}>{a.direction}</Badge><span style={{ flex: 1 }}>{a.item} <span className="muted small">{a.identifier}</span></span><span className="muted small">{fmt.ago(a.occurredAt)}</span></div>)}{activity.data?.length === 0 && <span className="muted">No activity yet</span>}</div>
            <div className="panel"><h3>Recent charges</h3>{ledger.data?.slice(0, 8).map((l) => <div key={l.id} className="row" style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}><Badge>{l.kind}</Badge><span style={{ flex: 1 }} className="small">{l.description}</span><span style={{ color: l.amount < 0 ? 'var(--ok)' : undefined }}>{money(l.amount, l.currency)}</span></div>)}{ledger.data?.length === 0 && <span className="muted">No charges</span>}</div>
          </div>
        </>}
        {tab === 'items' && <>
          <div className="row" style={{ marginBottom: 12 }}><input placeholder="Search name or identifier…" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} style={{ maxWidth: 300 }} /><label className="row small" style={{ gap: 4 }}><input type="checkbox" style={{ width: 'auto' }} checked={custody} onChange={(e) => { setCustody(e.target.checked); setPage(1); }} />Only items currently with you</label><span className="muted small">{items.data?.total ?? 0} items</span></div>
          <div className="panel table-wrap"><table><thead><tr><th>Item</th><th>Type</th><th>Status</th><th>Due back</th><th>Last seen</th></tr></thead>
            <tbody>{items.data?.items.map((i) => <tr key={i.id}><td><b>{i.name}</b><div className="muted small">{i.identifier}{i.epc ? ` · ${i.epc}` : ''}</div></td><td className="small">{i.itemType}</td><td>{i.inCustody ? <Badge tone="info">with you</Badge> : <span className="muted small">returned{i.location ? ` · ${i.location}` : ''}</span>}</td><td className="small">{i.dueBackAt ? <span style={{ color: new Date(i.dueBackAt) < new Date() ? 'var(--crit)' : undefined }}>{fmt.d(i.dueBackAt)}</span> : '—'}</td><td className="small muted">{fmt.ago(i.lastSeenAt)}</td></tr>)}
              {items.data?.items.length === 0 && <tr><td colSpan={5} className="empty">Nothing here.</td></tr>}</tbody></table>
            {items.data && <Pager page={items.data.page} pageSize={items.data.pageSize} total={items.data.total} onPage={setPage} />}</div>
        </>}
        {tab === 'activity' && <div className="panel table-wrap"><table><thead><tr><th>When</th><th>Item</th><th>Direction</th><th>Reference</th></tr></thead><tbody>{activity.data?.map((a) => <tr key={a.id}><td className="small muted">{fmt.dt(a.occurredAt)}</td><td>{a.item}<div className="muted small">{a.identifier}</div></td><td><Badge tone={a.direction.startsWith('Issued') ? 'info' : 'ok'}>{a.direction}</Badge> <span className="muted small">{a.operation}</span></td><td className="small">{a.reference ?? '—'}</td></tr>)}</tbody></table></div>}
        {tab === 'invoices' && <div className="panel table-wrap"><table><thead><tr><th>Invoice</th><th>Period</th><th>Status</th><th className="right">Total</th><th>Due</th><th></th></tr></thead>
          <tbody>{invoices.data?.map((i) => <tr key={i.id}><td className="mono">{i.number}</td><td className="small">{fmt.d(i.periodFrom)} – {fmt.d(i.periodTo)}</td><td><Badge tone={i.status === 'Paid' ? 'ok' : 'warn'}>{i.status}</Badge></td><td className="right"><b>{money(i.total, i.currency)}</b></td><td className="small">{fmt.d(i.dueAt)}</td><td className="right"><button className="sm" onClick={() => openPrint(i.id)}>View / print</button></td></tr>)}
            {invoices.data?.length === 0 && <tr><td colSpan={6} className="muted">No invoices.</td></tr>}</tbody></table></div>}
      </main>
    </div>
  );
}

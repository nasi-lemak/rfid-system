import { useState } from 'react';
import { del, getToken, post, put } from '../api/client';
import { useBalances, useInvalidate, useInvoices, useItemTypes, useLedger, useParties, useRateCards } from '../api/hooks';
import type { RateCard } from '../api/types';
import { useAuth } from '../auth';
import { Badge, ErrorBox, Modal, fmt } from '../components/ui';

const money = (v: number, c: string) => `${v < 0 ? '−' : ''}${Math.abs(v).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${c}`;
const kindTone = (k: string): 'ok' | 'warn' | 'crit' | 'info' | '' => k === 'DepositRefund' || k === 'Payment' ? 'ok' : k === 'LateFee' || k === 'LossFee' ? 'crit' : k === 'Deposit' ? 'info' : '';

export default function Billing() {
  const { user } = useAuth(); const isAdmin = user?.role === 'Admin';
  const [tab, setTab] = useState<'accounts' | 'invoices' | 'rates'>('accounts');
  const balances = useBalances(); const cards = useRateCards(); const parties = useParties(); const itemTypes = useItemTypes(); const inv = useInvalidate();
  const [party, setParty] = useState<string>(''); const ledger = useLedger({ partyId: party || undefined, take: 200 });
  const [status, setStatus] = useState(''); const invoices = useInvoices({ status: status || undefined, partyId: party || undefined });
  const [card, setCard] = useState<Partial<RateCard> | null>(null); const [error, setError] = useState<unknown>(null); const [busy, setBusy] = useState(false); const [msg, setMsg] = useState<string | null>(null);
  const [gen, setGen] = useState(() => { const d = new Date(); return { from: new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() - 1, 1)).toISOString().slice(0, 10), to: new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), 1)).toISOString().slice(0, 10) }; });
  const act = async (fn: () => Promise<unknown>, done?: string) => { setBusy(true); setError(null); try { await fn(); inv('balances', 'ledger', 'invoices', 'rate-cards'); if (done) setMsg(done); } catch (e) { setError(e); } finally { setBusy(false); } };
  const saveCard = () => act(async () => { if (!card) return; if (card.id) await put(`/api/billing/rate-cards/${card.id}`, card); else await post('/api/billing/rate-cards', card); setCard(null); });
  const openPrint = async (id: string) => { const res = await fetch(`/api/billing/invoices/${id}/print`, { headers: { Authorization: `Bearer ${getToken()}` } }); const html = await res.text(); const w = window.open('', '_blank'); if (w) { w.document.write(html); w.document.close(); } };
  return (
    <div>
      <div className="topbar"><h1>Billing</h1><span className="muted small">Deposits, cycle and rental fees for returnable assets · ledger per party · periodic invoices</span></div>
      <div className="tabs">{(['accounts', 'invoices', 'rates'] as const).map((t) => <button key={t} className={tab === t ? 'active' : ''} onClick={() => setTab(t)}>{t === 'accounts' ? 'Accounts & ledger' : t === 'invoices' ? 'Invoices' : 'Rate cards'}</button>)}</div>
      <ErrorBox error={error} />{msg && <div className="panel" style={{ marginBottom: 12 }}>{msg} <button className="sm" onClick={() => setMsg(null)}>✕</button></div>}
      {tab === 'accounts' && <>
        <div className="row" style={{ marginBottom: 12 }}>{isAdmin && <button disabled={busy} onClick={() => act(async () => { const r = await post<{ created: number }>('/api/billing/accrue'); setMsg(`Accrual created ${r.created} ledger entr${r.created === 1 ? 'y' : 'ies'}`); })}>Accrue custody events now</button>}<span className="muted small">Issue/dispatch → deposit + cycle fee · return → refund, rental beyond free days, late fee · dispose while out → loss fee. Runs hourly.</span></div>
        <div className="grid" style={{ gridTemplateColumns: '380px 1fr' }}>
          <div className="panel table-wrap"><h3>Accounts</h3><table><thead><tr><th>Party</th><th className="right">Unbilled</th><th className="right">Outstanding</th></tr></thead>
            <tbody>{balances.data?.map((b) => <tr key={b.partyId} className="clickable" onClick={() => setParty(b.partyId)} style={party === b.partyId ? { background: 'color-mix(in srgb, var(--primary) 8%, transparent)' } : undefined}><td>{b.party}<div className="muted small">{b.kind} · deposits held {b.depositsHeld.toFixed(2)}</div></td><td className="right">{b.unbilled.toFixed(2)}</td><td className="right">{b.outstanding > 0 ? <Badge tone="warn">{b.outstanding.toFixed(2)}</Badge> : '—'}</td></tr>)}
              {balances.data?.length === 0 && <tr><td colSpan={3} className="muted">No ledger activity yet – add a rate card and issue some returnable items.</td></tr>}</tbody></table></div>
          <div className="panel table-wrap"><div className="topbar"><h3 style={{ margin: 0 }}>Ledger {party && balances.data?.find((b) => b.partyId === party) ? `· ${balances.data.find((b) => b.partyId === party)!.party}` : ''}</h3>{party && <button className="sm" onClick={() => setParty('')}>All parties</button>}</div>
            <table><thead><tr><th>When</th><th>Party</th><th>Kind</th><th>Description</th><th className="right">Amount</th><th>Invoice</th></tr></thead>
              <tbody>{ledger.data?.map((l) => <tr key={l.id}><td className="small muted">{fmt.d(l.occurredAt)}</td><td>{l.party}</td><td><Badge tone={kindTone(l.kind)}>{l.kind}</Badge></td><td className="small">{l.description}</td><td className="right" style={{ color: l.amount < 0 ? 'var(--ok)' : undefined }}>{money(l.amount, l.currency)}</td><td className="small muted">{l.invoiceId ? 'billed' : 'unbilled'}</td></tr>)}
                {ledger.data?.length === 0 && <tr><td colSpan={6} className="muted">No entries</td></tr>}</tbody></table></div>
        </div>
      </>}
      {tab === 'invoices' && <>
        <div className="row" style={{ marginBottom: 12 }}>
          <select value={status} onChange={(e) => setStatus(e.target.value)} style={{ maxWidth: 140 }}><option value="">All statuses</option><option>Draft</option><option>Issued</option><option>Paid</option><option>Void</option></select>
          <select value={party} onChange={(e) => setParty(e.target.value)} style={{ maxWidth: 220 }}><option value="">All parties</option>{parties.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select>
          {isAdmin && <><span className="small">Generate for</span><input type="date" value={gen.from} onChange={(e) => setGen({ ...gen, from: e.target.value })} style={{ maxWidth: 150 }} /><span className="small">→</span><input type="date" value={gen.to} onChange={(e) => setGen({ ...gen, to: e.target.value })} style={{ maxWidth: 150 }} /><button className="primary" disabled={busy} onClick={() => act(async () => { const r = await post<unknown[]>('/api/billing/invoices/generate', { from: gen.from, to: gen.to, partyId: party || null }); setMsg(`${r.length} draft invoice(s) generated`); })}>Generate drafts</button></>}
        </div>
        <div className="panel table-wrap"><table><thead><tr><th>Invoice</th><th>Party</th><th>Period</th><th>Status</th><th className="right">Charges</th><th className="right">Credits</th><th className="right">Total</th><th>Due</th><th></th></tr></thead>
          <tbody>{invoices.data?.map((i) => <tr key={i.id}><td className="mono">{i.number}</td><td>{i.party}</td><td className="small">{fmt.d(i.periodFrom)} – {fmt.d(i.periodTo)}</td><td><Badge tone={i.status === 'Paid' ? 'ok' : i.status === 'Issued' ? 'warn' : i.status === 'Void' ? '' : 'info'}>{i.status}</Badge></td><td className="right">{money(i.charges, i.currency)}</td><td className="right">{money(i.credits, i.currency)}</td><td className="right"><b>{money(i.total, i.currency)}</b></td><td className="small muted">{fmt.d(i.dueAt)}</td>
            <td className="right"><button className="sm" onClick={() => openPrint(i.id)}>Print</button> {isAdmin && i.status === 'Draft' && <button className="sm primary" disabled={busy} onClick={() => act(() => post(`/api/billing/invoices/${i.id}/issue`))}>Issue</button>} {isAdmin && i.status === 'Issued' && <button className="sm" disabled={busy} onClick={() => act(() => post(`/api/billing/invoices/${i.id}/pay`))}>Mark paid</button>} {isAdmin && i.status !== 'Paid' && i.status !== 'Void' && <button className="sm danger" disabled={busy} onClick={() => confirm('Void invoice? Its entries return to the unbilled pool.') && act(() => post(`/api/billing/invoices/${i.id}/void`))}>Void</button>}</td></tr>)}
            {invoices.data?.length === 0 && <tr><td colSpan={9} className="muted">No invoices yet.</td></tr>}</tbody></table></div>
      </>}
      {tab === 'rates' && <>
        <div className="topbar"><p className="muted small" style={{ margin: 0 }}>The most specific enabled card wins: party › party kind › item type › catch-all, then priority. Amounts are in the card's currency; free days are deducted before daily rental.</p>{isAdmin && <button className="primary" onClick={() => setCard({ name: '', currency: 'USD', depositAmount: 0, cycleFee: 0, dailyFee: 0, freeDays: 0, lateFeePerDay: 0, lossFee: 0, enabled: true, priority: 0 })}>+ New rate card</button>}</div>
        <div className="panel table-wrap"><table><thead><tr><th>Card</th><th>Applies to</th><th className="right">Deposit</th><th className="right">Cycle</th><th className="right">Daily (after free)</th><th className="right">Late / day</th><th className="right">Loss</th><th></th></tr></thead>
          <tbody>{cards.data?.map((c) => <tr key={c.id}><td><b>{c.name}</b>{!c.enabled && <Badge>off</Badge>}<div className="muted small">{c.currency} · priority {c.priority}</div></td><td className="small">{c.party ?? c.partyKind ?? 'any party'} · {c.itemType ?? 'any type'}</td><td className="right">{c.depositAmount.toFixed(2)}</td><td className="right">{c.cycleFee.toFixed(2)}</td><td className="right">{c.dailyFee.toFixed(2)} <span className="muted small">({c.freeDays} d free)</span></td><td className="right">{c.lateFeePerDay.toFixed(2)}</td><td className="right">{c.lossFee.toFixed(2)}</td><td className="right">{isAdmin && <><button className="sm" onClick={() => setCard(c)}>Edit</button> <button className="sm danger" onClick={() => confirm('Delete rate card?') && act(() => del(`/api/billing/rate-cards/${c.id}`))}>✕</button></>}</td></tr>)}
            {cards.data?.length === 0 && <tr><td colSpan={8} className="muted">No rate cards yet – nothing is charged until one exists.</td></tr>}</tbody></table></div>
      </>}
      {card && <Modal title={card.id ? 'Edit rate card' : 'New rate card'} onClose={() => setCard(null)}>
        <form className="form" onSubmit={(e) => { e.preventDefault(); saveCard(); }}>
          <div className="form cols-2">
            <label>Name *<input required value={card.name ?? ''} onChange={(e) => setCard({ ...card, name: e.target.value })} /></label>
            <label>Currency<input value={card.currency ?? 'USD'} onChange={(e) => setCard({ ...card, currency: e.target.value.toUpperCase() })} maxLength={3} /></label>
            <label>Item type<select value={card.itemTypeId ?? ''} onChange={(e) => setCard({ ...card, itemTypeId: e.target.value || null })}><option value="">any</option>{itemTypes.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
            <label>Party kind<select value={card.partyKind ?? ''} onChange={(e) => setCard({ ...card, partyKind: e.target.value || null })}><option value="">any</option>{['Customer', 'Supplier', 'Department', 'Employee', 'Person', 'Other'].map((k) => <option key={k}>{k}</option>)}</select></label>
            <label>Specific party<select value={card.partyId ?? ''} onChange={(e) => setCard({ ...card, partyId: e.target.value || null })}><option value="">any</option>{parties.data?.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
            <label>Priority<input type="number" value={card.priority ?? 0} onChange={(e) => setCard({ ...card, priority: Number(e.target.value) })} /></label>
            <label>Deposit<input type="number" step="0.01" value={card.depositAmount ?? 0} onChange={(e) => setCard({ ...card, depositAmount: Number(e.target.value) })} /></label>
            <label>Cycle fee (per issue)<input type="number" step="0.01" value={card.cycleFee ?? 0} onChange={(e) => setCard({ ...card, cycleFee: Number(e.target.value) })} /></label>
            <label>Daily fee<input type="number" step="0.01" value={card.dailyFee ?? 0} onChange={(e) => setCard({ ...card, dailyFee: Number(e.target.value) })} /></label>
            <label>Free days<input type="number" value={card.freeDays ?? 0} onChange={(e) => setCard({ ...card, freeDays: Number(e.target.value) })} /></label>
            <label>Late fee per day<input type="number" step="0.01" value={card.lateFeePerDay ?? 0} onChange={(e) => setCard({ ...card, lateFeePerDay: Number(e.target.value) })} /></label>
            <label>Loss fee<input type="number" step="0.01" value={card.lossFee ?? 0} onChange={(e) => setCard({ ...card, lossFee: Number(e.target.value) })} /></label>
          </div>
          <label className="row"><input type="checkbox" style={{ width: 'auto' }} checked={card.enabled !== false} onChange={(e) => setCard({ ...card, enabled: e.target.checked })} /> Enabled</label>
          <div className="row end"><button type="button" onClick={() => setCard(null)}>Cancel</button><button className="primary">Save</button></div>
        </form></Modal>}
    </div>
  );
}

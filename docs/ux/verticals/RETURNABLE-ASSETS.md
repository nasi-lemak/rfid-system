# Vertical journey — Returnable assets, kegs and rental

Template `returnable-assets`. Item types RTI (returnable container: InPool → AtCustomer → InPool;
Damaged; Scrapped), KEG (Empty → Filled → AtDistributor → AtCustomer → Empty), RENTAL (Available →
Rented → Returned → Available via Inspect; Maintenance). Rules *Returnable overdue at customer* and
*Rental overdue* (Warning). Party kind Customer. Optional module **Billing** (deposits, cycle/daily/late
fees, invoices) and the **Portal** for customers.

Markers: **[H]** handheld · **[F]** fixed reader · **[A]** automatic · **[W]** web · **[P]** portal.

## 1. Deployment shape

```
     Pool / yard                    Customer                       Back
 ┌────────────────┐  Dispatch    ┌─────────────────┐   Return    ┌──────────────────┐
 │ InPool         │────────────► │ AtCustomer      │───────────► │ Inspect → InPool │
 │ [F] yard gate  │  party +     │ custodian, due  │  [H] scan   │ or Damaged       │
 └────────────────┘  due-back    └─────────────────┘             └──────────────────┘
```

Customers are Parties (kind Customer) and may also be Locations (kind Customer) for the destination.

## 2. Happy path — a crate cycle with billing

```
[USER][W]  Admin: Billing → Rate cards → deposit + daily fee for RTI
[USER][W]  Dispatch (web form): destination "Customer ACME (site)", party "ACME", due-back +14 d
           → EPCs from the handheld Inventory screen or typed                          InPool→AtCustomer, custodian, due-back
           (handheld Dispatch lacks due-back → web-driven, or accept no due date)
[HW][F]    Yard gate (Out) reads the crates                                            Moved (Out)
[AUTO][A]  Billing accrual (Admin → Accrue custody events now, or scheduled): deposit + daily fee ledger entries
[USER][P]  Customer signs in to the portal: Items in custody, Activity, Invoices
    …14 days…
[AUTO][A]  Rule "Returnable overdue at customer" on Seen with item.overdue → Warning alert (fires on the *next read* after due-back)
[USER][H]  Return: scan crates at the dock → (optional) location Pool → Submit          AtCustomer→InPool, custody and due-back cleared
[USER][H]  Inspect: damaged ones → target Damaged                                       *→Damaged
[USER][W]  Billing → Generate drafts → Issue → Mark paid
```

## 3. Exception paths

| Situation | What happens | Who sees what | Label |
|---|---|---|---|
| **Overdue detection relies on a read** | rule trigger is `Seen`; an asset that is overdue *and never read again* never fires. Schedule rules exist (`RuleKind.Schedule`) but the template uses Seen | web | **Recommended improvement** (content): make the overdue rules Schedule-kind (daily) — backend supports it |
| **Dispatch without due-back from the handheld** | no due-back → never overdue | | Missing UX (handheld due-back), P2 — third vertical with the same finding |
| **Customer returns a crate that is not theirs** | Return clears custody regardless | — | works; minor |
| **Keg partially filled / wrong state** | KEG Filled→AtDistributor→AtCustomer via two Dispatches; `Returned` state is declared but unreachable | — | Recommended improvement (content): remove `Returned` from KEG or add a transition |
| **Damaged crate** | Inspect → Damaged; Maintain → InPool; Dispose → Scrapped | | works |
| **Rental inspection interval** | RENTAL `InspectionIntervalDays=90` → *Inspection due* chip | web | works |
| **Portal user tries the main app** | `portalPartyId` forces the Portal shell; API refuses other paths (403) | — | works |
| **Billing dispute** | Void invoice returns entries to the pool (confirmed); manual ledger adjustment via API only (`ledger/adjustment` has no UI) | Admin | **Missing UX** (adjustment form), P3 |
| **Customer sees invoices before issue** | the portal lists only non-Draft invoices (`Status != Draft`) | customer sees Issued/Paid/Void only | works |

## 4. Gaps specific to this vertical

| Gap | Label | Priority |
|---|---|---|
| Overdue rules on `Seen` instead of a schedule | Recommended improvement (content) | P2 |
| Handheld due-back on Dispatch | Missing UX | P2 |
| Ledger adjustment UI | Missing UX | P3 |
| KEG `Returned` unreachable state | Recommended improvement (content) | P3 |

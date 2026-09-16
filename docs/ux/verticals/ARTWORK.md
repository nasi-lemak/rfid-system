# Vertical journey — Museum collections and artwork

Template `museum-artwork`. Item types ARTEFACT (InStorage · OnDisplay · OnLoan · Conservation ·
Deaccessioned), ARTWORK (InStorage → Packed → InTransit → Installed → InStorage), ART-CRATE
(container). Rules *Artefact left building without loan* (Critical) and *High-value artefact missing from
gallery count* (Critical). No workflow.

Markers: **[H]** handheld · **[F]** fixed reader · **[A]** automatic · **[W]** web.

## 1. Deployment shape

```
   Storage vault              Galleries                Loading dock / exit
 ┌───────────────┐  Transfer ┌────────────┐  Pack+Dispatch ┌──────────────────┐
 │ InStorage     │──────────►│ OnDisplay  │───────────────►│ [F] exit gate    │──► Borrower (Party)
 │ [H] Count     │◄──────────│ [H] Count  │                │ direction Out    │    OnLoan, due-back
 └───────────────┘  Return   └────────────┘                └──────────────────┘
```

Setup specifics: the exit antenna needs `Direction = Out` (the rule matches `data.direction eq Out`);
borrowers are Parties (kind Customer or Other); insured value and accession number are attributes
(`accessionNo` required on ARTEFACT, `title` required on ARTWORK).

## 2. Happy paths

### 2.1 Record and display (ARTEFACT)

```
[USER][W]  Registrar: Items → + New item → type ARTEFACT, identifier, accessionNo, insuredValue     state InStorage
[USER][W]  Item detail → Bind EPC (or [H] Commission with the tag on the object's mount)           tag bound
[USER][H]  Transfer: scan → destination "Gallery 2" → Submit                                        *→OnDisplay
[USER][H]  Periodic Stocktake of "Gallery 2": New stocktake → scan → Finish & reconcile             Open→Reconciled
[USER][W]  Supervisor: Stocktake detail → review Missing/Unexpected → Apply result (confirmed)      items Missing/moved
[AUTO][A]  Rule "High-value artefact missing from gallery count" on Counted/result=Missing → Critical alert
[USER][H]  Return: scan → (optional) location Vault                                                  OnDisplay→InStorage
```

### 2.2 Loan out and back (ARTWORK in a crate)

```
[USER][H]  Pack: scan crate tag as container → scan artworks → Submit                InStorage→Packed; children inside ART-CRATE
[USER][W]  Dispatch (web operation form): destination "External – Borrower", party "Museum B",
           due-back date → EPCs: the crate                                            crate + children Moved; ARTWORK Packed→InTransit
           (the handheld Dispatch cannot set due-back — Missing UX; so this step is web-driven)
[HW][F]    Exit gate (Out) reads the crate                                             Moved to parent (outside); rule checks state:
                                                                                       InTransit is not in [InStorage,OnDisplay,Conservation] → no alert
[USER][H]  At the borrower (if they use the platform): Unpack → location "Museum B Gallery"  *→Installed
    …loan…
[USER][H]  Return: scan → location Vault                                               *→InStorage, custody cleared, due-back cleared
```

## 3. Exception paths

| Situation | What happens | Who sees what | Label |
|---|---|---|---|
| **Object passes the exit while InStorage/OnDisplay** (not dispatched) | [F] Out read → Moved → rule → **Critical alert** "Mona passed exit while 'OnDisplay'" | web Alerts; notification/escalation if configured; handheld nothing | works; no local alarm (see LINEN §3) |
| **Loan overdue** | `item.overdue` derived from due-back; the museum template has **no overdue rule** (returnable-assets and tool templates do) | web Items → *Overdue* chip only | **Recommended improvement** (template content): add "Loan overdue" Seen/Schedule rule |
| **Dispatch from the handheld** | Dispatch requires a destination; the handheld never asks due-back | loan has no due date → overdue never fires | **Missing UX** (handheld due-back input), P2 |
| **Object found in the wrong gallery during a count** | line Unexpected with expected location; *Apply result* moves it here | supervisor decides | works |
| **Object missing from count** | Missing after Reconcile; Apply sets status Missing and fires the Critical rule | web | works |
| **Object goes to Conservation** | [H]/[W] Maintain → *→Conservation, optionally move to Conservation lab; Return brings it InStorage | | works |
| **Deaccession** | [W] Item detail → Dispose (now confirmed) → Deaccessioned, tag released | | works |
| **Crate nested in a crate** | Pack allows nesting; cycle refused ("Cannot pack X into Y: it already contains X") | operator line | works |
| **Read at the gate while the network is down** | edge agent buffers; alert fires when the batch arrives (minutes later) | delayed alert | works but the *delay* is invisible (**Missing UX**: alert shows `occurredAt` vs created? The alert row shows created time only) |
| **Condition report / photos** | not a capability | — | **Missing product capability**, Optional (attach files to events) |

## 4. UI usage split

| Step | Surface | Why |
|---|---|---|
| Item record, attributes, insured value | web | rich fields, keyboard |
| Bind tag | either | handheld when the tag is on the object |
| Transfer to gallery, Return to vault | handheld | on the floor |
| Pack | handheld | at the crate |
| Dispatch with due-back | **web only today** | handheld lacks due-back |
| Gallery count | handheld count + **web Apply** | apply is web-only by design |
| Alerts | web | |

## 5. Gaps specific to this vertical

| Gap | Label | Priority |
|---|---|---|
| Handheld Dispatch/Issue cannot set due-back | Missing UX | P2 |
| No overdue-loan rule in the template | Recommended improvement (content) | P3 |
| Condition reports/attachments | Missing product capability | Optional |
| Alert does not show the event time when the read arrived late | Missing UX | P3 |

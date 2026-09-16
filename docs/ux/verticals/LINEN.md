# Vertical journey — Linen, laundry and uniforms

Template `linen-laundry`. Item types LINEN (Clean → Issued → Soiled → InWash → Clean; Contaminated
branch; Retired), UNIFORM (Stock → Issued → InLaundry → Stock), LAUNDRY-CAGE (container). Rules
*Linen reached max wash cycles* (Warning) and *Contaminated linen in clean zone* (Critical). Workflow
*Wash cycle* (Load washer → Unload clean).

Markers: **[H]** handheld · **[F]** fixed reader · **[A]** automatic (rule/background) · **[W]** web.

## 1. Deployment shape

```
 Ward / hotel floor            Central laundry
 ┌──────────────┐  soiled cage  ┌──────────────────────────────────────────────┐
 │ Issue point  │──────────────►│ Soiled receiving → Wash hall → Clean store   │
 │ [H] Issue    │◄──────────────│ [F] gate (In)      [H] Wash    [F] gate (In) │
 │ [H] Return   │  clean cage   │                     cycle       clean = true │
 └──────────────┘               └──────────────────────────────────────────────┘
```

Setup specifics (beyond [SETUP-FLOWS.md](../SETUP-FLOWS.md)): zones *Clean store* and ward linen rooms
need the location attribute `clean: true` (JSON) for the contamination rule; wards are **Parties**
(kind Department) if linen is *issued* to them, or **Locations** if it is merely *transferred*. The
template uses Issue, so wards are parties. Handheld operators set *Settings → Current location*.

## 2. Happy path — one sheet, one cycle

```
[USER][H]  Commission: read tag → type LINEN → identifier "SH-0412" → Bind        state Clean, at Clean store
[USER][H]  Issue: scan cage contents → Party "Ward 3" → Submit                    Clean→Issued, custodian Ward 3, due-back none
[HW][F]    Ward gate antenna (In) reads the cage                                  Moved to Ward 3 linen room; Seen thereafter (30 s debounce)
    …used…
[USER][H]  Return: scan soiled cage → (optional) location Soiled receiving        Issued→Soiled, custodian cleared
[HW][F]    Laundry inbound gate (In)                                              Moved to Soiled receiving
[USER][H]  Workflows → Wash cycle
           step 1 "Load washer": scan → target state InWash → Submit              Soiled→InWash, cycle +1
           step 2 "Unload clean" (rescan): scan → target Clean → dest Clean store Clean, at Clean store
[AUTO][A]  Rule "Linen reached max wash cycles" checks cyclesRemaining ≤ 0        alert Warning when the 200th wash is recorded
[USER][W]  Supervisor sees the alert → Item detail → Dispose (confirmed)           Retired/Disposed, tag released
```

Everything on the ward is a fixed read or an Issue/Return on the handheld; everything in the laundry
is the workflow. No web use is needed for daily operation.

## 3. Exception paths

| Situation | What happens | Who sees what | Label |
|---|---|---|---|
| **Contaminated item** identified on the ward | [H] Process stage → target *Contaminated* (Issued→Contaminated). Later the wash cycle accepts Contaminated→InWash | operator: line ok, new state Contaminated | works |
| **Contaminated item enters a clean zone** | [F] read at a `clean: true` zone → Moved event → rule → **Critical alert** "Contaminated SH-0412 detected in Clean store" → notifications/escalation if configured | web Alerts, sidebar badge; handheld nothing | works. **Missing product capability**: no local signal at the gate (a rule action that drives a reader GPO/stack light does not exist; GPO is only a manual API call) |
| **Sheet scanned into Wash cycle while still Issued** | step 1 line **Rejected** "No transition from 'Issued' to 'InWash'" (Process stage is free-form, but the LINEN lifecycle has no such transition) | operator sees the rejected line and message; with `onRejected=stop` (default) the tag is excluded from step 2 | works; message is precise but technical |
| **Unknown tag in the cage** | line Unknown "Tag not commissioned" | operator: line; supervisor: Tags → unknown EPCs | works; operator must leave the workflow to commission (**Recommended improvement**: "Commission now" from the line) |
| **Max cycles reached** | Warning alert; item keeps working | web only | works; **Missing UX**: handheld does not show cycle count in the line list (Lookup shows it) |
| **Cage (container) moves through a gate** | [F] read of the LAUNDRY-CAGE tag → container move cascades to packed children (`viaContainer`) | web Presence shows the children in the zone | works if items were **Packed** into the cage; the linen template has Pack/Unpack, but the daily flow above never packs — teams must decide whether cages are containers or just carriers |
| **Network down in the laundry** | [H] workflow steps queue; fixed gate reads buffered by the edge agent | operator: "Offline – step queued"; web: gateway Degraded/Offline | works ([OFFLINE.md](../OFFLINE.md)) |
| **Shrinkage** — items not seen for weeks | [W] Items → *Not seen 7d* chip; Count via stocktake at the ward marks Missing after Apply | web | works |
| **Wrong ward returned it** | Return clears custody regardless of who returns | — | works; the *from* party is not recorded (**Missing product capability**: minor) |
| **Uniform issued to an employee** | same Issue flow with Party kind Employee; UNIFORM lifecycle Stock→Issued | | works |

## 4. Where linen differs from artwork

| Aspect | Linen | Artwork |
|---|---|---|
| Volume per operation | tens to hundreds of tags per submit; dedupe and count matter | one to a few; certainty per item matters |
| Who acts | laundry operators on handhelds all day | registrars on the web, handlers on handhelds occasionally |
| Automation | fixed gates + contamination rule + cycle counting | exit gate rule + gallery count rule |
| Workflow | *Wash cycle* guided workflow is the daily tool | no workflow; single operations (Pack, Dispatch, Unpack) |
| Custody | wards as parties, no due-back | borrower as party **with due-back** (loans) |
| Containers | cages optional | crates essential (Pack before Dispatch) |
| Web role | supervisor handles alerts and disposal | registrar maintains the record, prints condition reports (not built) |

## 5. Gaps specific to this vertical

| Gap | Label | Priority |
|---|---|---|
| No local alarm on contamination (GPO/rule) | Missing product capability | P3 for a pilot |
| Cycle count not visible on handheld operation lines | Missing UX | P2 |
| Commission from a rejected/unknown line | Recommended improvement | P2 |
| Wash cycle step 1 hard-codes the state via *ask targetState*; a fixed `targetState: InWash` would remove a tap | Recommended improvement (template content) | P3 |

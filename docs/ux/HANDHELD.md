# Handheld UX model

Scope: the Expo/React Native app in `src/mobile` as implemented today, on Android pistol-grip or
sled readers (Zebra RFD40/8500, Chainway C72/C66, BLE sleds) and in the simulated-reader mode used for
development. Everything below is grounded in the code; anything that does not exist yet is labelled
**Missing UX**, **Missing product capability**, **Recommended improvement** or **Optional future
enhancement**.

## 1. Who uses it and in what conditions

The operator is standing or walking, often holding assets, sometimes wearing gloves, in a warehouse,
laundry, hangar, theatre corridor or gallery store. They look at the screen for a second at a time.
The design goals that follow from this are: one primary action per screen, the trigger drives
scanning, big counters, and a result the operator can read at arm's length.

## 2. Information architecture

```
Login (user e-mail/password  •  or device provisioning token)
└─ Home ─────────────────────────────────────────────────────────────────────
   │  Reader status · current location · offline queue banner (auto-sync on focus)
   ├─ Scan / Inventory   free scan, resolve tags, "post as sighting", hand off to Operations
   ├─ Lookup             one tag → item card, history, quick actions, print label
   ├─ Locate             geiger-counter search for one EPC
   ├─ Stocktake          continue an open count or start a new one for a location
   ├─ Operations         one operation on a scanned set (definition-driven form)
   ├─ Workflows          guided multi-step tasks → Workflow run
   ├─ Commission         bind / encode a tag to a new or existing item
   ├─ Floor plan         anchors + last positions (works from cache offline)
   ├─ Map & geofences    own GPS position vs fences
   └─ Settings           reader driver, RF power, current location, printer, queue, offline cache, language, GPS, sign out
```

Home tiles (title · subtitle) as implemented: Scan / Inventory, Lookup, Locate, Stocktake, Operations,
Workflows, Commission, Floor plan, Map & geofences, plus a Settings button. All nine tiles are shown to
every user regardless of role or installed template (**Missing UX: capability/role-driven home** — see
`INFORMATION-ARCHITECTURE.md` §6).

### Home screen (as built)

```
┌──────────────────────────────────────────┐
│ Reader: zebra            [ ready ]       │
│ Location: Ward 3 store                   │
├──────────────────────────────────────────┤
│ ⚠ 3 operation(s) waiting to sync         │   ← only when queue > 0
│                            [ Sync now ]  │
├──────────────────────────────────────────┤
│ 📡 Scan / Inventory                      │
│    Read everything in range…             │
│ 🔍 Lookup      🎯 Locate                 │
│ 📋 Stocktake   🔁 Operations             │
│ 🧭 Workflows   🏷️ Commission             │
│ 🗺️ Floor plan  📍 Map & geofences        │
├──────────────────────────────────────────┤
│ [ Settings ]                             │
└──────────────────────────────────────────┘
```

## 3. The common operation shape

Every operation on the handheld follows the same shape. The form is generated from the operation
definition (`requires` + `effects`) that the server publishes at `/api/operations/definitions`, so a
template-defined operation such as *Sterilise* gets the same screen as *Transfer* with no app change.

```
Start (tile or quick action from Lookup)
→ choose context   operation chip · destination / party / container picker · target state · quantity
→ scan             trigger (hold) or ▶ Scan (latch) · 📷 barcodes · tags de-duplicated by EPC
→ review           N tag(s) · list of EPCs (first 60)
→ submit           enabled only when the definition's requirements are met
→ result           per-line badge: new state (ok) · Unknown (info) · rejection message (crit)
                   summary "x ok · y unknown · z rejected"
→ next             success clears the list; partial result keeps the scanned set for retry
```

### Wireframe — Transfer (as built, annotated)

```
┌────────────────────────────────────────────┐
│ ‹ Operation                                │
│ (Receive)(Transfer)(Issue)(Return)(Count)… │  ← chips, incl. template ops (Sterilise…)
├────────────────────────────────────────────┤
│ Destination *            Warehouse B  ›    │  ← picker (full-screen filter list)
│ Party                    choose…      ›    │  ← shown because Transfer has SetCustodian(optional)
│ [ 📷 Scan barcodes (0 added) ]             │
│ Reference  [ PO / work order / case… ]     │
├────────────────────────────────────────────┤
│ 43 tag(s)                 [Clear] [■ Stop] │
│ 3034…0001   ✓ PutAway                      │
│ 3034…0002   ✓ PutAway                      │
│ E280…BEEF   Unknown                        │
│ 3034…0007   Transfer not allowed while…    │
│ … and 3 more                               │
├────────────────────────────────────────────┤
│ 40 ok · 2 unknown · 1 rejected             │
│ [        Submit Transfer        ]          │
└────────────────────────────────────────────┘
```

What differs from the ideal in the brief: there is no separate **Review** step and no dedicated
**Stop scan → review → confirm** pause; the operator stops scanning and submits from the same
screen. The count line says "43 tag(s)" but does not pre-classify tags as valid/unknown before submit
(**Missing UX: pre-submit resolution**; the Scan / Inventory screen does resolve tags live, the
Operation screen does not).

## 4. Operation-by-operation

| Operation | Context asked for (from the definition) | Notes as built |
|---|---|---|
| Receive | Destination * | `Activate` + `Move`; brings Missing items back to Active |
| Transfer | Destination *, Party (optional) | Containers move their contents |
| Issue | Party *, Destination (optional); due-back is web-only | The handheld form has no due-back field (**Missing UX**: `SetDueBack` effect exists, the app never asks) |
| Return | Destination (optional) | Clears custody and due-back |
| Dispatch | Destination *, Party (optional); due-back web-only | Same due-back gap |
| Count | none | Records "seen"; clears Missing; quantity items record counted/variance |
| Inspect | Target state (result) | Sets last-inspected/next-due |
| Maintain | Destination (optional), Target state | |
| Dispose | none | Retires tags; irreversible; **no confirmation prompt** (Missing UX) |
| Pack | Container * | Nested containers allowed, cycles refused by the server |
| Unpack | Destination (optional) | |
| Stage (ProcessStage) | Target state, Destination (optional) | Free-text state; datalist of lifecycle states exists only on the web |
| Adjust | Quantity delta * | Quantity items |
| Move quantity | Destination *, Quantity * | Splits/merges lot rows |
| Template operations (Sterilise, Decontaminate, Calibrate…) | as defined | Appear as chips automatically |
| Commission | own screen | see §6 |

## 5. Scanning behaviour (as built)

- **Continuous scanning.** The hardware trigger is *hold-to-scan* (`onTrigger(pressed)` starts and
  stops inventory). The on-screen ▶ Scan / ■ Stop button is a latch. The two can disagree: releasing
  the trigger always stops, even when the button latched scanning on (**Recommended improvement**:
  make the button and trigger share one state and show "scanning" prominently).
- **Duplicate reads** are collapsed into one row per EPC by `useInventory` (count and best RSSI kept),
  flushed to the screen every 200 ms. Barcode scans and EPCs passed in from Lookup are merged into
  the same set.
- **Unknown EPCs** are not flagged until the server answers: Scan / Inventory resolves them live and
  shows "Unknown tag"; the Operation screen shows Unknown as a per-line result *after* submit.
  Unknown lines never block the rest of the batch. The simulated reader always emits two unknown EPCs
  on purpose.
- **Accidental scans / clearing.** `Clear` empties the set (and, on Inventory, the resolution cache);
  there is no per-row remove (**Missing UX**: swipe/tap to exclude a tag before submit).
- **Large tag counts.** Rows are capped at 60 with "… and N more"; the request carries every EPC.
  Lists inside the ScrollView are not virtualised and re-render five times a second while scanning
  (**Recommended improvement**: virtualised list, render counters not rows while scanning).
- **Partially rejected batches.** The result shows counts and per-line reasons; the scanned set is kept
  so the operator can fix context (e.g. choose a party) and resubmit. Re-submitting sends the whole set
  again; already-processed lines are rejected by the lifecycle, not de-duplicated
  (**Recommended improvement**: "Retry rejected only").
- **Operator cancellation.** Android back pops the screen; nothing is submitted. There is no explicit
  Cancel on the Operation screen.

## 6. Commission (tag binding)

```
Step 1 Tag      EPC/barcode in hand · 📡 Read nearest tag (1 s best-RSSI window) · 📷 Barcode
                Optional: encode a new EPC (hex) or take the next EPC from a GS1 serial pool → Write EPC to tag
Step 2 Item     item type chips · identifier (defaults to EPC) · name · required attributes (*)
                "Initial state: Clean · location: Ward 3 store" (from Settings → current location)
Submit          Commission → "✓ Commissioned <name>" · type stays selected for batch commissioning
                → 🖨 Print label for last item
```

Offline: queued like any operation. Good fit for batch tagging of new linen or tools; the serial pool
step is the only place the app writes to a tag.

## 7. Stocktake

```
Picker      Continue an open stocktake  |  Start a new one (name, find location → tap)
Counting    ┌ Expected 312 │ Found 87% │ Not seen 41 │ Unexpected 3 ┐  progress bar
            "298 tags read on device · 2 unknown"
            [▶ Scan] [📷 Barcode]  … scans auto-push every 1.5 s
            [Leave open]                       [Finish & reconcile]
Result      "Reconciled: 271 found, 41 missing, 3 unexpected. Apply the result from the web app."
```

Facts that matter for the field: scans are pushed as they arrive (server-side dedupe), failed pushes
are retried automatically, but **the stocktake is not offline-capable** (pushes need the server) and
**Finish & reconcile has no confirmation** (Missing UX). Applying the result (flag missing, relocate
unexpected) is a web action only.

## 8. Lookup and Locate

Lookup answers "what is this?" in one scan: item card (state, status, location, custodian, due date,
quantity, cycles, expiry, attributes), history (last 20 events) and quick actions (Transfer, Issue,
Return, Count, ProcessStage, Inspect, Locate, On map, Print label). Unknown tag → "Commission it".
Locate is a proximity meter (0–100 % from RSSI, peak marker, ten dots) with hold-to-search on the
trigger.

## 9. Status and messages on the handheld (as built)

| Concept | Where shown | Representation |
|---|---|---|
| Reader status | Home header, Settings | Badge with raw driver status (`ready`, `inventory`, `locating`, `error`, `disconnected`) |
| Current location | Home header, Settings | text, "not set" when empty |
| Offline queue | Home banner, Settings | "N operation(s) waiting to sync" |
| Per-line result | Operation / Workflow rows | Badge: ok=new state, info=Unknown, crit=message |
| Operation outcome | inline message | "✓ Transfer: 40 item(s)" green · warnings orange |
| Offline outcome | inline message | "Offline – queued (3 pending). It will sync automatically." |

There is no toast, dialog or haptic feedback; all feedback is inline text. Colour is always paired
with text, so colour is not the only indicator.

## 10. Known gaps specific to the handheld

| # | Gap | Label | Notes |
|---|---|---|---|
| H1 | No confirmation for Dispose, Discard queue, Sign out, Finish & reconcile | Missing UX | One tap, irreversible. **Fixed** (GAP-ANALYSIS G-H2): native confirm dialogs on all four |
| H2 | Token expiry (401) is shown as a per-screen error; no auto-logout or re-login prompt | Missing UX | **Fixed** (G-H1): a 401 with a stored token signs the operator out to the login screen with "Your session has expired" and the pending count; live submits are queued instead of failing; sync pauses without counting attempts |
| H3 | Rejected queued operations are stored but never shown | Missing UX | **Fixed** (G-H3): Settings → Offline queue lists them with reason; Home shows the count |
| H4 | "Sync now" does not force past back-off | Missing UX | **Fixed** (G-H4): Home and Settings call `sync(true)` |
| H5 | Sync only runs when Home is focused; no connectivity listener | Recommended improvement | |
| H6 | Stocktake scans, sightings, GPS fixes are not queued offline | Missing product capability (handheld) | Server contract supports batchId idempotency already |
| H7 | Due-back date cannot be entered for Issue/Dispatch | Missing UX | |
| H8 | No pre-submit resolution of unknown tags on Operation screen | Missing UX | |
| H9 | Small buttons (~33 px) and chips (~29 px) are below the 44 px glove target | Recommended improvement | Chips select item type, operation, reader, language |
| H10 | Four screen titles fall back to route names; most screen strings untranslated | Recommended improvement | |
| H11 | Home shows every tile regardless of template/role | Missing UX | |
| H12 | Barcode screen passes a function through navigation params | Recommended improvement | works, but fragile for state restoration |

Severity and priority for these are in [GAP-ANALYSIS.md](GAP-ANALYSIS.md) (handheld rows G-H1…G-H15). Sections 3–9 above describe the screens before the four fixes; the fixes add dialogs and a list, not new screens.

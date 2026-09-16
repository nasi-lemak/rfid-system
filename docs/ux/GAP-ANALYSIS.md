# UX gap analysis and pilot blockers

Severity scale:

| Severity | Meaning |
|---|---|
| **P0** | the platform cannot realistically be deployed or used safely in a pilot |
| **P1** | a pilot would produce misleading results, lost work, or unsafe actions without it |
| **P2** | real friction; fix during the pilot from user feedback |
| **P3** | polish, content, or optional |

Labels follow [README.md](README.md): Missing UX · Missing product capability · Recommended improvement ·
Optional future enhancement. "Backend" says whether server work is needed. "Before PV" = should be done
before production validation starts. "Wait" = can wait for user feedback.

## 1. Gap table

### Handheld

| ID | Workflow | Current support | Problem | Proposed improvement | Sev | Backend | Before PV | Wait |
|---|---|---|---|---|---|---|---|---|
| G-H1 | Any operation after 12 h (user token) | 401 shown as raw text per screen; live submit not queued; sync treats 401 as transport error for ~3 h then drops the queue to a hidden list | **Lost work and no way back in** except knowing to Sign out in Settings | On 401: keep the queue, do not count attempts, queue the live submit, sign out to the Login screen with "Session expired – n operations will sync after you sign in" | **P1** | no | yes | no |
| G-H2 | Dispose, Discard queue, Finish & reconcile, Sign out with a pending queue | no confirmation anywhere on the handheld | one tap retires items and releases tags / deletes unsent work / marks items missing | native confirm dialogs on these four | **P1** | no | yes | no |
| G-H3 | Queue sync | operations rejected on sync or after 20 attempts go to a list with no UI | operator believes work was recorded | Settings shows the rejected list (when, operation, reason) with Clear; Home shows "n rejected" | **P1** | no | yes | no |
| G-H4 | Sync now | calls `sync()` without `force`, so items in back-off are skipped ("Sent 0, n remaining") | button appears broken | `sync(true)` on Home and Settings | **P1** (trivial) | no | yes | no |
| G-H5 | Stocktake | not offline-capable: scans posted live, lost when leaving the screen offline; create requires the server | counts in basements/yards fail | local scan buffer per stocktake, flushed when online; create queued | P2 (P1 for sites without Wi-Fi) | no (scan batches are idempotent already) | site-dependent | yes |
| G-H6 | Issue / Dispatch | handheld never asks due-back | overdue rules never fire for handheld issues (three verticals) | add a due-back date field when the definition has `SetDueBack` | P2 | no | no | yes |
| G-H7 | Any operation | no per-row remove; no "resubmit failed only"; no Commission from an Unknown row | rescans after one wrong tag | row swipe/remove; retry-failed button; link to Commission | P2 | no | no | yes |
| G-H8 | Home / Operations | all tiles and all 15 operations shown regardless of template | irrelevant choices for a linen or tool crib operator | capability gating from cached definitions/workflows/plans/fences; template operation list | P2 | small (expose installed templates' operation list) | no | yes |
| G-H9 | Commission / Receive | current location optional | items created with no location | require a current location before those operations | P2 | no | no | yes |
| G-H10 | Workflow run | no resume after leaving; `dueBack`/`container` asks not rendered | interrupted reprocessing runs must restart | run record + "runs in progress"; render the two asks | P2 | small (run record) | no | yes |
| G-H11 | Touch targets | chips ≈ 29 px, small buttons ≈ 33 px | glove use | ≥ 44 px for chips used as pickers | P2 | no | no | yes |
| G-H12 | Language | tiles/settings translated, everything else English | selector over-promises | translate the handheld fully or hide the selector | P2 | no | no | yes |
| G-H13 | Sightings / GPS / stocktake scans | not queued offline | lost when offline (operations are safe) | queue them like operations | P2 | no | no | yes |
| G-H14 | Barcode screen | navigation param is a function | React Navigation warning; state loss on reload | callback registry | P3 | no | no | yes |
| G-H15 | Feedback | no vibration on rejected lines; text colours vary per screen | | one Notice component; haptic on reject | P3 | no | no | yes |

### Web

| ID | Workflow | Current support | Problem | Proposed improvement | Sev | Backend | Before PV | Wait |
|---|---|---|---|---|---|---|---|---|
| G-W1 | Stocktake Cancel / Apply result; Item Dispose; ERP Import; Cluster Release; Rotate token | no confirmation | irreversible in one click; Apply flags hundreds of items and fires alerts | `confirm()` with consequences spelled out on Cancel, Apply, Dispose, Import (Release/Rotate: P2) | **P1** | no | yes | no |
| G-W2 | Login | demo credentials pre-filled and printed | any pilot user sees admin credentials | show only in dev builds | **P1** | no | yes | no |
| G-W3 | Session expiry | silent logout to the login page | confusion, retyping | "Your session expired" notice on the login page | P2 (bundled with G-H1 as a one-liner) | no | yes | no |
| G-IA3 | Role reflection | routes not guarded; mutation buttons visible to Viewer/Operator; server refuses with raw text | "the system is broken" reports | one `can()` helper mirroring server policies; guard routes; hide buttons | P2 | no | no | yes |
| G-IA1 | Navigation | 33 links to every tenant | discoverability | capability-gated, task-grouped sidebar ([INFORMATION-ARCHITECTURE.md](INFORMATION-ARCHITECTURE.md) §4) | P2 | optional | no | yes |
| G-IA4 | Reader onboarding | Config JSON | biggest setup cliff; typos silent | kind-specific device form, then a wizard with "first read" verification | P1 for *self-service* onboarding, P2 when developers install the pilot readers | no | recommended | partly |
| G-W4 | Stocktakes list | Reconciled-not-applied rows look like any other | supervisors forget Apply; items never flagged | highlight unapplied; dashboard count | P2 | no | no | yes |
| G-W5 | Device/gateway health | edge heartbeat metrics not rendered; antenna without location silent | outages diagnosed on the box | render metrics on the gateway card; warning badge | P2 | no | no | yes |
| G-W6 | Error rendering | 13 pages without error state, 9 without empty state; five success patterns; failures in green boxes | invisible failures | one error/notice component; empty states everywhere | P2 | no | no | yes |
| G-W7 | Locations / Item types / Devices | JSON attribute editors | rule/presence/plan keys undiscoverable | named fields per kind | P2 | no | no | yes |
| G-W8 | Alerts | no severity/site filter, no item link, Viewer sees actions | slow triage | filters, link, gating | P2 | no | no | yes |
| G-W9 | Accessibility | no focus styles on buttons/links/chips; chips and rows not keyboard-reachable; no dialog roles; low-contrast badges | excludes keyboard users; fails basic audits | CSS focus-visible; chips as buttons; Modal roles + focus trap; badge ink | P2 | no | no | yes |
| G-W10 | i18n | 5 % translated; dates follow the browser | over-promise | remove selector or translate | P2 | no | no | yes |
| G-W11 | Templates | *Load demo data* and *Install* without confirmation on a production tenant; `prompt()` for export version | accidental demo data | confirm on demo seed; form field | P2 | no | no | yes |
| G-W12 | Overlaps | two reader-uptime views, two spatial views, four commissioning entry points | confusion | merges per IA review | P3 | no | no | yes |
| G-W13 | Dead elements | Parties custody link ignored; *Mark done* simulator button; `Retired` status | | fix/remove | P3 | no | no | yes |
| G-W14 | Developer tools in nav | EPCIS console, Cluster, warehouse export | audience mismatch | move under Admin | P3 | no | no | yes |
| G-W15 | Login | no tenant-code field although the API can demand one | multi-tenant e-mail collision | add optional field | P3 | no | no | yes |

### Setup and platform

| ID | Workflow | Current support | Problem | Proposed improvement | Sev | Backend | Before PV | Wait |
|---|---|---|---|---|---|---|---|---|
| G-S1 | First deployment | seeder default `Seed:Scenarios=all` | a real tenant starts with 23 demo sites | default to `none` outside Development; document; "reset demo data" action | **P1** (configuration + docs; one-line code default) | tiny | yes | no |
| G-S2 | First deployment | nine pages, implicit order | new admin lost | checklist ([SETUP-FLOWS.md](SETUP-FLOWS.md) §2) now; wizard later | P2 | optional | docs yes | wizard yes |
| G-S3 | Roles | no role between Operator and Admin; finance needs Admin | over-privilege in multi-site rollouts | site-manager / billing roles | P3 for a single-site pilot | yes | no | yes |
| G-S4 | Local alarms | no rule action drives a reader GPO / stack light | contamination/FOD alerts only on the web | `Gpo` rule action | P3 | yes | no | yes |
| G-S5 | Overdue rules in templates | `Seen`-triggered | overdue assets never read again never alert | Schedule-kind rules in templates | P2 (content) | no | no | yes |
| G-S6 | Kits | custody not cascaded to packed children | tool kits show custody on the kit only | cascade option on SetCustodian | P2 | yes | no | yes |
| G-S7 | CSSD | no tray completeness view | template promises it | expected-set per tray vs scanned | P2 | yes | no | yes |
| G-S8 | Warehouse | allocation not visible on handheld; Receive of quantity needs two operations | | handheld task list (later); `ReceiveStock` template op (now, content) | P2 | partly | no | yes |
| G-S9 | Handheld attribution | shared handheld with device token attributes to the device, not the person | audit weakness | operator badge-in | Optional | yes | no | yes |
| G-S10 | Alerts | never auto-close when the condition clears | stale alerts | auto-resolve rules | Optional | yes | no | yes |
| G-S11 | Outbox / deliveries | backlog and per-delivery log not visible | | System status stat; endpoint Deliveries tab | P3 | small | no | yes |

## 2. What is *not* a gap

Things reviewed and found sound, so the pilot should exercise them as they are:

- The operation model (one submit, per-line results, idempotent replay) and the handheld queue's
  ordering, batching and back-off.
- Guided workflows as ordinary operations (offline for free, audit trail for free).
- Stocktake's two-surface design (count on the handheld, decide on the web).
- Edge agent store-and-forward, late-read protection, duplicate acknowledgement.
- Server-side authorization (policies, site RBAC, portal scoping).
- The GS1 Encoding wizard as a pattern.

## 3. P0/P1 shortlist — what blocks meaningful real-world testing

There are **no P0 items**: the platform can be deployed and used safely by a team that knows it. The
P1 items are the ones that would make a pilot produce *false* results (lost or silently dropped work),
*unsafe* one-tap actions, or expose credentials.

| # | Item | Why it blocks a realistic pilot | Scope of the fix |
|---|---|---|---|
| 1 | **G-H1 handheld session expiry and queue safety** (+ G-W3 one-line web notice) | Operators signed in with a user account lose their token after 12 hours: live submits fail with "401", queued work is retried for hours and then dropped to an invisible list. A pilot would under-count real activity and blame the operators | handheld `api/client.ts`, `store/queue.ts`, `store/settings.ts`, `App.tsx`, `LoginScreen.tsx`; web `auth.tsx`, `Login.tsx` |
| 2 | **G-H2 + G-W1 confirmations on irreversible actions** | Dispose releases tags; Discard queue deletes unsent work; Finish & reconcile marks items missing; Apply result changes hundreds of items and fires alerts; Import writes master data. One mis-tap during a pilot corrupts the data the pilot is measuring | handheld `OperationScreen`, `SettingsScreen`, `StocktakeScreen`; web `StocktakeDetail`, `ItemDetail`, `Import` |
| 3 | **G-H3 + G-H4 queue visibility and Sync now** | Rejected operations exist in storage but no screen shows them; *Sync now* skips items in back-off so it looks broken. Both make the offline story untestable | handheld `SettingsScreen`, `HomeScreen` |
| 4 | **G-W2 demo credentials** (+ handheld login prefill; G-S1 seeder default) | Pilot users must not see admin credentials on the login page; a real tenant must not boot with demo data | web `Login.tsx`, handheld `LoginScreen.tsx` (dev-only prefill); `Program.cs` seeder default and README note |

Everything else in §1 stays documented and unimplemented until the pilot produces feedback.

## 4. Fixes applied (this change set)

| ID | Change | Files |
|---|---|---|
| G-H1 | `api()` reports a 401 that occurred *with* a token through `onUnauthorized`; `App.tsx` clears the token and sets `sessionExpired`; `LoginScreen` shows "Your session expired – sign in again" with the pending-queue count; `runOrQueue` queues an operation on 401 instead of throwing; `sync()` stops on 401 without incrementing attempts (the queue waits for re-login) | `src/mobile/src/api/client.ts`, `store/queue.ts`, `store/settings.ts`, `App.tsx`, `screens/LoginScreen.tsx` |
| G-W3 | web: `rfid:unauthorized` sets a session-expired notice shown on the login page | `src/web/src/auth.tsx`, `pages/Login.tsx` |
| G-H2 | confirm dialogs: Dispose (operation submit), Discard queue, Finish & reconcile, Sign out with pending operations | `screens/OperationScreen.tsx`, `SettingsScreen.tsx`, `StocktakeScreen.tsx` |
| G-W1 | `confirm()` on Stocktake Cancel and Apply result, Item detail Dispose (before the modal opens), ERP Import | `src/web/src/pages/StocktakeDetail.tsx`, `ItemDetail.tsx`, `Import.tsx` |
| G-H3 | Settings → Offline queue shows the rejected list (time, operation, reason) with *Clear rejected* (confirmed); Home shows "n operation(s) rejected on sync – see Settings" | `SettingsScreen.tsx`, `HomeScreen.tsx` |
| G-H4 | *Sync now* forces past back-off on Home and Settings | `HomeScreen.tsx`, `SettingsScreen.tsx` |
| G-W2 | demo credentials pre-filled and printed only in development builds (`import.meta.env.DEV`, `__DEV__`) | `Login.tsx`, `LoginScreen.tsx` |
| G-S1 | demo scenarios are opt-in outside Development: the code default is `none` unless the environment is Development, `appsettings.json` no longer forces `all` (moved to `appsettings.Development.json`), `docker compose` exposes `SEED_SCENARIOS` (default `all` for evaluation), README says to set `none` for a real tenant | `src/backend/Rfid.Api/Program.cs`, `appsettings.json`, `appsettings.Development.json`, `docker-compose.yml`, `README.md` |

No backend behaviour other than the seeder default changed. No screen was added.

# Screen inventory

Every web page and handheld screen as built. Columns: **Role** = minimum server role needed to use its
main actions (V Viewer · O Operator · A Admin · D Device) · **Core/Opt** = core platform screen or an
optional module (would be hidden for tenants without the capability under the proposed capability-gated
navigation) · **Standalone** = does the screen make sense on its own, or should it be a tab/panel of
another.

Flags: **D** duplicated · **X** dead or unreachable element · **P** poorly discoverable · **T** too technical
for its audience.

## 1. Web pages

| Screen | Purpose | Role | Main actions | Depends on | Part of workflows | Core/Opt | UX concerns | Standalone? |
|---|---|---|---|---|---|---|---|---|
| **Login** | password or SSO sign-in | — | Sign in, SSO | `/api/auth/config` | all | Core | Demo credentials pre-filled and printed (fixed, dev-only now); no tenant-code field although the API can demand one (P) | yes |
| **SSO callback** | OIDC redirect landing | — | — | oidc-client | login | Core | failure shows nothing | yes |
| **Dashboard** | user-configurable widgets | V (edit O, share A) | + Widget, Save, Edit, Delete | dashboards API | overview | Core | Only page that role-gates buttons; widgets duplicate Alerts/Events/Devices/Stocktakes (D, acceptable) | yes |
| **Live reads** | SignalR read stream + live alerts | V | Clear | live hub, devices, locations | reader onboarding verification | Core | empty state quotes an API path (T); alerts pane duplicates sidebar badge (D) | yes; could be a tab of Devices |
| **Alerts** | open/ack/close alerts | V (ack/close O) | Acknowledge, Close | alerts API | alert handling | Core | Viewer sees action buttons; no error/loading state; no filter by severity/site; no link to the item | yes |
| **Presence & location** | zones, floor plan x/y, muster, checkpoint timing | V | filters only | presence, positions, locations | monitoring | Opt (fixed readers / RTLS / people) | four unrelated tabs; floor plan is a second spatial view (D with Map); no errors rendered | yes as *Presence*; Floor plan should join Map under one *Location* section |
| **Map & geofences** | GPS assets and fence CRUD | V (fences A) | + New geofence, Edit, Delete | geo API | GPS tracking | Opt (GPS) | empty state quotes API (T); only meaningful for GPS tenants | tab of *Location* |
| **Event log** | raw item events | V | filter type/location | events API | audit | Core | no empty/error/loading state; no date filter or paging (200 rows) | yes |
| **Reports** | catalogue + CSV | V | Download CSV | reports API | audit / BI | Core | caption quotes API (T); no error state | yes |
| **Analytics** | trends, utilisation, dwell, accuracy, alert response, uptime; warehouse export (A) | V / A | export actions | analytics, warehouse | management | Opt | reader uptime duplicates Devices → Health (D); warehouse export is a developer tool (T) | yes; export → Admin |
| **Anomalies** | baseline deviation findings | V (actions O) | Run detector, Confirm, Dismiss | anomalies API | monitoring | Opt | Viewer sees action buttons; strong findings also become alerts (D) | yes |
| **Items** | master list, filters, chips | V (+ New item O) | + New item, search, chips | items, item types, locations | everything | Core | `custodianPartyId` link from Parties ignored (X); no error state | yes |
| **Item detail** | one item: state, tags, history, quick operations, print | V (ops O) | 12 quick operations, Bind, Label, Print | items, events, labels, printers | everything | Core | no confirmation on Dispose (fixed); history duplicates Event log (D, correct) | yes (not in nav, reached by row click) |
| **Tags** | EPC registry + unknown EPCs | V (Commission O) | Commission ×2, Unassigned filter | tags API | commissioning | Core | table has no empty state; Commission entry duplicated (D) | yes; unknown-EPC panel belongs also on Devices |
| **Operations** | history + run + line detail | V (run O) | + Run operation, filter | operations API, definitions | all operations | Core | no empty/error state; the modal is the same `OperationForm` as Item detail (D, correct) | yes |
| **Stocktakes** | list + schedules | V (create O) | + New stocktake, + Schedule, Run now | stocktakes, schedules | stocktake | Core | fine | yes |
| **Stocktake detail** | counters, lines, manual scans, Reconcile/Apply/Cancel | V (actions O) | Reconcile, Apply result, Cancel, Submit scans | stocktakes API | stocktake | Core | Apply/Cancel had no confirmation (fixed); "Apply" is the step supervisors miss (P) | yes |
| **Stock** | quantity balances, movements, FEFO allocate | V (allocate O) | Allocate, Move picked lots | stock API, operations | inventory | Opt (Quantity types) | balances/movements no empty state; shown to every tenant | yes |
| **Print queue** | jobs + label stock | O (labels policy) | Process now, Retry, Cancel, Set stock | labels API, printers | labelling | Opt (Printer) | H1 differs from nav label; no error state | yes |
| **Encoding** | GS1 wizard, pools, batches | O (pools A) | Back/Next/Commit, + New pool | encoding API | commissioning at scale | Opt (serial pools) | the only true wizard; good | yes |
| **Billing** | ledger, invoices, rate cards | V (mutations A) | Accrue, Generate drafts, Issue/Pay/Void, rate cards | billing API | returnables | Opt (rate cards) | duplicates Portal (D, two audiences) | yes → Admin group |
| **Maintenance** | predictive risk scores | V (run O) | Snapshot & alert now | maintenance API | maintenance | Opt | no error state; Viewer can trigger a run that creates alerts | yes |
| **Item types** | type cards, lifecycle, JSON editors | V (CRUD A) | + New type, Edit, Delete, Label | item types API | setup | Core | raw JSON (T); Delete blocked with 409 when items exist | yes |
| **Locations** | tree + detail + CRUD | V (CRUD A) | + New under X, Edit, Delete | locations API | setup | Core | attributes JSON hides rule/presence/floor-plan keys (T, P) | yes |
| **Parties** | custodian directory | V (CRUD O/A) | + New party, Edit, Delete | parties API | custody | Core (hide when no custody ops) | no empty state; dead link to Items filter (X) | yes |
| **Readers & devices** | devices, antennas, tokens, LLRP, health, firmware | V (CRUD/tokens A; LLRP O) | + New device, Edit, Issue/Rotate token, Status/GPIO, SLA, firmware rollouts | devices, firmware, llrp APIs | reader onboarding | Core | Config JSON (T); no gateway metrics (P); *Mark done* simulator button (X); no confirmation on Rotate token; Firmware tab is an optional module | yes; Firmware → sub-tab already |
| **Rules** | event/schedule rules | V (CRUD A) | + New rule, conditions, actions | rules, lookups, channels, definitions | automation | Core | condition language (T); no empty state; no test | yes; merge with Notifications as *Rules & notifications* |
| **Workflows** | definitions + runs | V (CRUD A) | + New workflow, steps, Runs tab | workflows API | guided workflows | Opt (workflows) | runs show raw ids; disable-instead-of-delete unexplained | yes |
| **Notifications** (A) | channels, policies, log | A | + New channel, Send test, + New policy | notifications API | alert handling | Core | secrets partly plain text | tab of *Rules & notifications* |
| **Label designer** | ZPL designer per type | O (designs policy) | add elements, Save to item type, Reset | label designs API | labelling | Opt (Printer) | green box used for a warning (E-10) | yes; reachable from Item types (good) |
| **Solution templates** | install, demo, export/import | V (apply/demo A) | Install, Load demo data, Export, Import | templates API | setup | Core | `prompt()` for version (T); no "what next"; *Load demo data* on a production tenant is one click with no confirmation (Recommended improvement: confirm) | yes; first step of setup wizard |
| **Integrations** (A) | outbound endpoints | A | + New endpoint, Deliver now | integrations API | ERP | Opt | failure text in green box (E-10); secrets plain | yes → Admin |
| **ERP import** (A) | CSV/JSON import + reconcile | A | Reconcile, Dry run, Import | import API | commissioning at scale | Opt | no confirmation before Import (fixed) | tab of Integrations |
| **Users** (A) | users, roles, portal link, sites | A | + New user, Edit, Sites | users API | setup | Core | no table empty state; no invite | yes |
| **Cluster & system** (A) | nodes, leases, flags | A | Release lease | cluster API | operations | Core (tooling) | Release has no confirmation; raw error div | yes → Admin *System status* |
| **Audit & retention** (A) | HTTP audit log, retention | A | search, Save changes, Run retention now | audit, retention | compliance | Core | fine; only the delete confirms | yes → Admin |
| **EPCIS 2.0** (A) | query/capture console | A | Run query, Capture | epcis API | GS1 interop | Opt | developer tool (T) | tab of Integrations |
| **Portal** | read-only party portal | V + portal | search, View/print invoice, Sign out | portal API | external parties | Opt (portal users) | untranslated tabs; no error states | yes (own shell) |

Shared components: `Layout` (sidebar), `OperationForm` (used by Operations, Item detail, Stock),
`NewItemModal` (Items, Tags), `ui.tsx` (Badge, Modal, ErrorBox, Pager, charts).

### Web summary

- **Screens that should not be top-level**: Map & geofences (→ Presence/Location), Notifications (→
  Rules), ERP import and EPCIS (→ Integrations), Cluster/Audit/Warehouse export (→ Admin). That takes
  the sidebar from 33 links to about 22 without removing any capability.
- **Screens hidden for tenants without the capability**: Stock, Print queue, Label designer, Encoding,
  Billing, Maintenance, Anomalies, Map, Workflows, Presence floor plan.
- **No screen is dead**, but three elements are: the Parties custody link, the *Mark done* simulator
  button, and the unused `Empty` component.

## 2. Handheld screens

| Screen | Purpose | Role | Main actions | Depends on | Workflows | Core/Opt | UX concerns | Standalone? |
|---|---|---|---|---|---|---|---|---|
| **Login** | server URL + e-mail/password or device token | — | Sign in | `/api/auth/login`, `/api/auth/device` | all | Core | demo credentials pre-filled (fixed, dev-only); token typed by hand (P) | yes |
| **Home** | tiles, reader status, sync panel | any | tiles, Retry reader, Sync now, Settings | queue, reader | all | Core | all nine tiles for everyone; *Sync now* did not force past back-off (fixed); no session-expired state (fixed) | yes |
| **Scan / Inventory** | read everything in range, identify | V | Scan/Stop, tap row → Lookup, "Operate on these" | reader, `/api/items/by-epc` | receive/transfer entry | Core | fine | yes |
| **Lookup** | one tag: item, state, lifecycle, history, quick actions | V (actions O) | Read tag, Barcode, quick operations, Locate, Commission | items API | everything | Core | quick actions offer all operations regardless of lifecycle | yes |
| **Locate** | Geiger search by RSSI | V | Start/Stop | reader | finding items | Core | fine | yes |
| **Stocktake** | open/create, scan, finish | O | New stocktake, Scan/Pause, Barcode, Leave open, Finish & reconcile | stocktakes API | stocktake | Core | not offline-capable; *Finish & reconcile* had no confirmation (fixed); apply is web-only (P) | yes |
| **Operations** | pick operation → scan → inputs → submit | O | operation chips, Scan/Stop, Clear, pickers, Submit | definitions, locations, parties, queue | all operations | Core | all 15 operations offered; no per-row remove; no due-back; Dispose had no confirmation (fixed) | yes |
| **Workflows** | list enabled workflows | O | pick | workflows API (cached) | guided workflows | Opt | tile shown when there are none | yes |
| **Workflow run** | step-by-step execution | O | Scan, Submit step, Next/Skip, Done | definitions, queue | guided workflows | Opt | no resume; `dueBack`/`container` asks not rendered | yes |
| **Commission** | bind EPC to a new item (+ pool allocation, print) | O | Read nearest tag, Barcode, pool chips, Bind, Print last | item types, pools, queue, printer | commissioning | Core | requires current location set for placement (P); no confirmation needed | yes |
| **Floor plan** | anchors + live positions (offline from cache) | V | pick plan | positions API (cached) | RTLS | Opt (anchors) | shown without anchors | yes |
| **Map & geofences** | own GPS vs fences | V | — | geo API (cached), GPS | GPS | Opt (fences) | shown without fences | yes |
| **Barcode** | camera scanner (single/continuous) | — | scan | camera | hybrid flows | Core | navigation param is a function (React Navigation warning) | modal |
| **Settings** | reader kind/power/beep, location, printer, GPS, language, queue, offline cache, sign out | any | Reconnect, Seed simulator, set location, Sync now, Discard queue, Download for offline, Clear cache, Sign out | settings store | all | Core | *Discard queue* had no confirmation (fixed); rejected list was invisible (fixed); Sign out had no confirmation (fixed) | yes |

### Handheld summary

Fourteen screens, ten of which an operator uses; that is already close to "fewer, clearer workflows".
The improvements are inside screens (validity, confirmations, visibility of the queue), not more
screens. The one structural recommendation is capability gating of the Home tiles and the operation
chips ([INFORMATION-ARCHITECTURE.md](INFORMATION-ARCHITECTURE.md) §5).

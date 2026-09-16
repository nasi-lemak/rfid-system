# Role-based user journeys

The platform has four technical roles (`Admin`, `Operator`, `Viewer`, `Device`) plus a *portal* flag on a
user, and per-site overrides of the three human roles. Real deployments have many more *jobs* than
that. This document maps each job to the technical role it needs, the surface it works on and the
screens it lives in, and records what is hard to reach today.

Legend: **W** = web, **H** = handheld, **P** = portal. "Hidden" = exists but the person would not find
it without being told.

## 1. Role → technical role matrix

| Job | Technical role | Surface | Notes |
|---|---|---|---|
| Platform administrator (single tenant) | Admin | W | Everything under *Configure*; also sees the operational screens |
| Multi-site / enterprise administrator | Admin, or Admin on some sites | W | Site restriction via Users → Sites. A user restricted to sites sees only items/devices/locations under those sites |
| Site supervisor | Operator (global or on the site) | W (+H occasionally) | Runs stocktakes to completion, works alerts, checks device health. Cannot edit configuration (server rejects; UI still shows the buttons – see §3) |
| Handheld operator (warehouse / linen / tool crib) | Operator, or the handheld signed in with a **device token** (role Device = Operator rank) | H | Never needs the web |
| Fixed-reader / gateway (no human) | Device | — | Authenticates with the gateway token; heartbeats and reads only |
| Reader / IT technician | Admin (device CRUD and tokens are Admin-only) | W + on-site | Needs Devices, Health & SLAs, LLRP status, edge agent config; cannot delegate token issuing to Operators |
| Auditor / compliance | Viewer (or Admin for Audit & retention) | W | Events, Reports, EPCIS query, Audit log (Admin only) |
| Read-only management viewer | Viewer | W | Dashboard, Alerts (read), Reports, Analytics |
| Customer / supplier (external) | Viewer + portal party | P | Sees only items in their custody, activity, invoices, ledger |
| Laundry operator (linen) | Operator / Device | H | Workflow *Wash cycle*, Issue/Return, Count |
| Museum registrar / collections manager | Operator (Admin for loans setup) | W + H | Web for item records and history; handheld for Pack/Dispatch/Unpack/Count |
| Tool crib attendant | Operator / Device | H | Issue/Return, workflow *Tool return & check*, Count |
| Warehouse operator | Operator / Device | H (+W Stock for pick lists) | Receive/Transfer/Pack/Dispatch/Count, Move quantity |
| CSSD technician | Operator / Device | H | Workflow *CSSD reprocessing*, Return, Issue |
| Billing / finance | Admin (accrual, invoices are Admin-only) | W | Billing; Viewer can only view |

**Missing product capability**: there is no role between Operator and Admin (for example a *Site
manager* who may edit locations and devices on their site but not users or rules). Item types,
locations, devices, rules and workflows are all Admin-only on the server. A pilot at a single site with
one trusted administrator does not need this; a multi-site rollout will.

## 2. Journeys per job

Each journey lists: goals · screens · frequent actions · occasional actions · what must be visible
immediately · what errors look like · what is hidden · desktop vs handheld.

### 2.1 Platform administrator (W)

- **Goals**: get a tenant from empty to operational; keep it configured as the site changes.
- **Screens**: Solution templates → Item types → Locations → Parties → Readers & devices → Rules → Workflows → Users; then Notifications, Integrations, Label designer as needed.
- **Frequent**: create locations, register a handheld or reader and issue its token, add a user, tune a rule.
- **Occasional**: install a template, seed demo data, export/import a template package, set retention, set SLAs, firmware rollouts.
- **Must see immediately**: whether readers are online (Devices → Health, Dashboard devices widget), open critical alerts (sidebar badge), whether the live hub is connected (sidebar dot – colour only).
- **Errors**: raw API message in a red box inside the modal (`ErrorBox`); 409 conflicts appear as "Conflict: …" with a database message. No success toast after Save – the modal just closes.
- **Hidden**: the device token is shown once in a modal and nowhere else (correct, but the *fact* that a device has a token is only a `HasToken` badge); `edgeManaged`/`edgeGatewayId` and all LLRP settings are free-text keys in a JSON config field; `presenceTimeoutSec` and `clean: true` (used by contamination rules) are location attributes typed as raw JSON; the *Export configuration* button uses a browser `prompt()` for the version.
- **Desktop only.** Nothing in setup is possible from the handheld.

### 2.2 Site supervisor (W, some H)

- **Goals**: know what is where, close the loop on alerts, complete stocktakes, keep the shift moving.
- **Screens**: Dashboard, Alerts, Items, Stocktakes / Stocktake detail, Presence, Operations, Devices → Health.
- **Frequent**: acknowledge/close alerts; open a stocktake for a location; Reconcile → Apply result; look up an item's history; check who has an overdue item (Items → Overdue chip).
- **Occasional**: run an operation from the web (Operations → Run operation), bind an EPC by hand, print a replacement label, cancel a stocktake.
- **Must see immediately**: open alerts by severity, stocktakes still Open, readers Offline, items Missing.
- **Errors**: same `ErrorBox` pattern; *Apply result* and *Cancel* on a stocktake have no confirmation (P1, fixed – see GAP-ANALYSIS §4); an Operator clicking an Admin-only button (for example Delete device) gets a raw "Forbidden"/400 message after the fact rather than not seeing the button (G-IA3).
- **Hidden**: the *Apply result* step. Reconcile marks items missing only inside the stocktake; nothing changes on the items until *Apply result*, and the handheld's *Finish & reconcile* never applies. Supervisors must know to come to the web (documented in [flows/STOCKTAKE.md](flows/STOCKTAKE.md)).
- **Desktop for review; handheld to count.** The supervisor's handheld use is the same operator flow.

### 2.3 Handheld operator (H)

- **Goals**: do the physical job (receive, move, issue, return, count, pack) with as few taps as possible and never lose a scan.
- **Screens**: Home → Operations (or a direct tile: Stocktake, Commission, Workflows, Lookup, Locate).
- **Frequent**: pick operation → scan → pick destination/party → Submit → repeat. Look up one tag.
- **Occasional**: stocktake, commission new tags, a guided workflow, set "current location" in Settings, Sync now.
- **Must see immediately**: reader connected, how many tags are in the current set, whether the last submit succeeded, whether anything is waiting to sync.
- **Errors**: per-line results after submit (ok/unknown/rejected with message); "Offline – queued"; a bare "401 Unauthorized" when the token has expired (P1, fixed); reader `error` badge with Retry.
- **Hidden**: rejected queued operations (no screen – P1, fixed), the *Return* operation's optional location, due-back on Issue/Dispatch (**Missing UX**: the handheld never asks), removing a single scanned tag from the set (**Missing UX**), which operations apply to the items they are holding (all 15 operations are offered).
- **Handheld only.** Nothing here needs the web except reviewing what was queued and rejected, which is exactly what the handheld should show itself.

### 2.4 Reader / IT technician (W + on site)

- **Goals**: bring a reader online, place its antennas on locations, confirm reads arrive, keep it online.
- **Screens**: Readers & devices (Devices tab, Edit modal, Status / GPIO modal, Health & SLAs tab), Live reads, Tags → unknown EPCs.
- **Frequent**: add a device, add antennas with location and direction, set LLRP host/power, watch Live reads.
- **Occasional**: issue a gateway token and configure an edge agent, mark readers as edge-managed, set SLA minutes, roll out firmware, reconnect a reader.
- **Must see immediately**: connected/not connected, tags received, last heartbeat, queue depth and poison count of the gateway (**Missing UX**: gateway heartbeat metrics are stored but not rendered).
- **Errors**: LLRP status modal shows connected=false with no reason; GPO failures return the driver exception text; there is no "test read" affordance other than watching Live reads.
- **Hidden**: every reader-specific setting is a free-text key in the *Config* JSON field (`llrpHost`, `llrpPower`, `edgeManaged`, `mqttTopic`, …) – see [flows/READER-ONBOARDING.md](flows/READER-ONBOARDING.md). This is the single most technical form in the product.
- **Desktop plus physical access.** The edge agent is configured in `appsettings.json`/environment on the box; only the reader list can be pulled from the server.

### 2.5 Auditor / compliance (W)

- **Goals**: reconstruct what happened to an item, who did it, and when; export evidence.
- **Screens**: Item detail → History (chain of custody), Event log, Reports (CSV), EPCIS 2.0 query, Audit & retention (Admin).
- **Frequent**: search item → read timeline; filter Event log by type/location; download a report.
- **Occasional**: EPCIS query by EPC; audit log by user.
- **Must see immediately**: an item's full timeline with actor (operator/device) and source.
- **Hidden**: the actor of an event is in the operation, not on the event row; Event log has no pagination and no date filter (200 most recent); Audit log is Admin-only, which usually excludes the auditor.
- **Desktop only.**

### 2.6 Read-only management viewer (W)

- **Goals**: a glance at health and exceptions.
- **Screens**: Dashboard, Alerts, Analytics, Reports.
- **Errors/hidden**: a Viewer still sees *+ New item*, *Acknowledge*, *Run detector now*, *Snapshot & alert now* and CRUD buttons everywhere; the server refuses with a raw message (G-IA3). Dashboard hides Edit for Viewers – the only page that does.

### 2.7 Customer / supplier (P)

- **Goals**: see what they hold, what moved, what they owe.
- **Screens**: Portal Overview / Items / Activity / Invoices. No sidebar, no other routes.
- **Errors**: none rendered (no `ErrorBox`); an expired token silently logs out.
- **Hidden**: nothing important; the portal is deliberately small. Untranslated tab labels despite a language selector.

### 2.8 Laundry operator (H) — see [verticals/LINEN.md](verticals/LINEN.md)

Frequent: *Wash cycle* workflow (Load washer → Unload clean), Issue to ward, Return from ward, Count.
Immediate needs: cycle count near max, contaminated items in the set (both are rule-driven alerts on
the web; the handheld shows only the per-line result). **Missing UX**: the handheld does not show the
item's cycle count or state in the operation line list before submit; it does on Lookup.

### 2.9 Museum registrar (W + H) — see [verticals/ARTWORK.md](verticals/ARTWORK.md)

Web for the record (attributes accessionNo, insuredValue, loans as Dispatch to a party with due-back);
handheld for Pack into crate, Dispatch, Unpack, Count in a gallery. **Missing UX**: due-back for a loan
can only be set from the web operation form; the handheld Dispatch never asks.

### 2.10 Tool crib attendant (H) — see [verticals/TOOL-CONTROL.md](verticals/TOOL-CONTROL.md)

Issue to employee (party), Return, *Tool return & check* workflow (Return → Inspect → optional
Calibrate), Count at close of shift. Immediate need: which tools are overdue and who has them — a web
chip (Items → Overdue), not a handheld view (**Missing UX** on handheld).

### 2.11 Warehouse operator (H + W Stock) — see [verticals/INVENTORY.md](verticals/INVENTORY.md)

Receive at dock, Transfer to bin, Pack cartons onto pallet, Dispatch. For quantity stock: Count with
quantity, Adjust, Move quantity; FEFO allocation is web-only (Stock → Allocate → Move picked lots).
**Missing UX**: a pick list produced on the web cannot be handed to a handheld.

### 2.12 CSSD technician (H) — see [verticals/HEALTHCARE-CSSD.md](verticals/HEALTHCARE-CSSD.md)

*CSSD reprocessing* workflow (Receive dirty trays → Decontaminate → Sterilise to sterile store), Issue
to theatre, Return. Immediate need: a tray issued while not Sterilised must be impossible or loud — the
rule exists (Critical alert) but the handheld line is merely *Rejected* if the lifecycle forbids it;
TRAY allows Issue only from Sterilised, so the reject message "Issue not allowed while TRAY-1 is
'Dirty'" is the guard.

### 2.13 Billing / finance (W)

Billing → Accrue, Generate drafts, Issue, Mark paid, Void; Rate cards. Admin-only for every mutation,
which forces finance staff to hold the Admin role (**Missing product capability**: a billing role).

## 3. Cross-cutting observations

1. **The server enforces roles; the web mostly does not reflect them.** Only seven sidebar links and a
   handful of buttons (Dashboard, Map, Billing, Analytics warehouse panel, Rules notification section,
   Encoding pools) check the role. A Viewer sees and can click almost every button. This is safe but
   confusing, and it will produce "the system is broken" reports in a pilot. Recommended improvement
   (G-IA3): a single `can(action)` helper in the web client mirroring the server policies, applied to
   buttons and routes.
2. **Per-site roles are invisible.** The footer says "Operator · 2 site(s)" but no screen says *which*
   sites or what the user can do there.
3. **The handheld has one role: whoever holds it.** Device-token sign-in is the right pilot default: the
   30-day token avoids the 12-hour user-token expiry, and every operation is attributed to the device.
   Attribution to a *person* then requires the operator to be a party (Issue to self), which no flow
   offers (**Missing product capability**: operator badge-in on a shared handheld; optional future enhancement).

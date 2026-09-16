# UX architecture and workflow documentation

This folder describes the RFID platform **as built** from the point of view of the people who deploy,
configure and operate it, and records where the experience is hard to discover, configure or use.
It was written before production validation so that the pilot exercises real workflows rather than
screens.

Every statement about the product is taken from the code (web client `src/web`, handheld `src/mobile`,
API `src/backend`). Anything that does not exist is labelled explicitly:

| Label | Meaning |
|---|---|
| **Missing UX** | the platform can do it (API/backend exists) but no screen exposes it, or the screen hides it |
| **Missing product capability** | neither UI nor backend does it |
| **Recommended improvement** | exists, but should change before or during the pilot |
| **Optional future enhancement** | nice to have; not needed for a pilot |

Nothing in these documents has been implemented on the strength of the finding alone; the only code
changes made alongside them are the P0/P1 items listed in [GAP-ANALYSIS.md](GAP-ANALYSIS.md) §4.

## Documents

| Document | Answers |
|---|---|
| [INFORMATION-ARCHITECTURE.md](INFORMATION-ARCHITECTURE.md) | What the web navigation looks like, what overlaps, what should merge, and the capability-driven navigation model |
| [ROLES.md](ROLES.md) | Who uses the product, what each role needs, and which surface (web or handheld) they live on |
| [SETUP-FLOWS.md](SETUP-FLOWS.md) | How a deployment is configured from an empty tenant to a running site, step by step, and which steps should become wizards |
| [HANDHELD.md](HANDHELD.md) | The handheld information architecture and the behaviour of every scanning operation |
| [OFFLINE.md](OFFLINE.md) | What happens when the network is down, on the handheld and at the edge |
| [ERROR-STATES.md](ERROR-STATES.md) | Every error and exception a user can meet, what they see, and who has to intervene |
| [WIZARDS.md](WIZARDS.md) | Guided workflows (handheld) and the admin wizards that exist or should exist |
| [SCREEN-INVENTORY.md](SCREEN-INVENTORY.md) | Every web page and handheld screen, its role, dependencies and UX concerns |
| [UX-CONSISTENCY.md](UX-CONSISTENCY.md) | Consistency, accessibility and field-usability review |
| [GAP-ANALYSIS.md](GAP-ANALYSIS.md) | The prioritised gap table (P0–P3) and the short list fixed before production hardening |
| `verticals/` | End-to-end journeys: [LINEN](verticals/LINEN.md), [ARTWORK](verticals/ARTWORK.md), [TOOL-CONTROL](verticals/TOOL-CONTROL.md), [INVENTORY](verticals/INVENTORY.md), [HEALTHCARE-CSSD](verticals/HEALTHCARE-CSSD.md), [RETURNABLE-ASSETS](verticals/RETURNABLE-ASSETS.md) |
| `flows/` | Step diagrams: [READER-ONBOARDING](flows/READER-ONBOARDING.md), [COMMISSIONING](flows/COMMISSIONING.md), [STOCKTAKE](flows/STOCKTAKE.md), [TRANSFER](flows/TRANSFER.md), [WORKFLOW-RUN](flows/WORKFLOW-RUN.md) |

Technical background lives one level up: [ARCHITECTURE.md](../ARCHITECTURE.md), [DATA-MODEL.md](../DATA-MODEL.md),
[SOLUTION-TEMPLATES.md](../SOLUTION-TEMPLATES.md), [EDGE-AGENT.md](../EDGE-AGENT.md), [INTEGRATION.md](../INTEGRATION.md).

---

## 1. Product UX architecture

The platform has four user-facing surfaces and one machine-facing surface. All of them talk to the
same API; there is no separate "admin backend".

```
                     ┌──────────────────────────────────────────────────────────────┐
                     │  WEB CLIENT (src/web) – desktop browser                       │
   Administrator ───►│  Configure: item types · locations · parties · devices ·     │
   Supervisor    ───►│             rules · workflows · templates · users · …         │
   Viewer        ───►│  Overview:  dashboard · live · alerts · presence · map ·     │
                     │             events · reports · analytics · anomalies          │
                     │  Track:     items · tags · operations · stocktakes · stock ·  │
                     │             print queue · encoding · billing · maintenance    │
                     └──────────────┬───────────────────────────────────────────────┘
   Customer /                       │ REST + SignalR
   supplier ────► PORTAL (same web bundle, read-only, party-scoped)
                                    │
                     ┌──────────────▼───────────────────────────────────────────────┐
                     │  PLATFORM API (src/backend/Rfid.Api)                           │
                     │  tenant + site RBAC · operations · lifecycle · rules · outbox │
                     └───┬──────────────────────────┬───────────────────────┬───────┘
                         │ /api/operations          │ /api/ingest/*         │ outbox
                         │ /api/stocktakes          │ heartbeats, config    │
   ┌─────────────────────▼──────┐   ┌───────────────▼──────────────┐   ┌────▼──────────────────┐
   │ HANDHELD (src/mobile)      │   │ FIXED READERS                │   │ RULES → ALERTS        │
   │ Operator: scan → operation │   │ direct LLRP, vendor pushes,  │   │ NOTIFICATIONS         │
   │ stocktake · commission ·   │   │ MQTT, or via the EDGE AGENT  │   │ INTEGRATIONS (ERP)    │
   │ lookup · locate · workflow │   │ (store-and-forward, on site) │   │ EPCIS                  │
   └────────────────────────────┘   └──────────────────────────────┘   └───────────────────────┘
                         │                          │
                         └──────────┬───────────────┘
                                    ▼
                     ITEM STATE  (location · custodian · lifecycle state · status · quantity)
                     ITEM HISTORY (events: Moved, CustodyChanged, StateChanged, Counted, …)
```

### How intent flows

1. An **administrator** installs a solution template (or creates item types and lifecycles), builds the
   location tree, registers readers and handhelds, and creates users. All of this is on the web under
   *Configure*.
2. **Operators** perform operations on the **handheld**: scan tags, pick a destination/party/state,
   submit. Each submit is one `Operation` with one line per tag; the server applies the operation's
   effects to each item, records events and evaluates rules.
3. **Fixed readers** produce reads without anyone acting. Reads become `Seen`/`Moved` events, open and
   close presence sessions, and can trigger rules (exit-without-checkout, contaminated linen in a clean
   zone, …). With an **edge agent** on site the reads survive a WAN outage.
4. **Rules** turn events into alerts, state changes, notifications, webhooks or further operations.
   **Integrations** deliver events to ERP/EAM systems through the outbox. **Supervisors** work alerts and
   stocktakes on the web.
5. Everything the customer sees is a projection of item state and item history.

### What is generic and what is template-driven

| Generic (identical for every tenant) | Template-driven (differs per vertical) |
|---|---|
| Screens, navigation, forms, the operation runner, stocktakes, alerts, devices | Item types, lifecycles and which operation moves an item between which states |
| The 15 built-in operations (Receive … MoveQuantity) | Extra operations (`Calibrate`, `Decontaminate`, `Sterilise`) and workflows (`wash-cycle`, `tool-return-check`, `cssd-reprocess`) |
| Status vocabulary (§2 below) | Lifecycle state names (Clean/Soiled/InWash, InStorage/OnDisplay/OnLoan, …) |
| Rules engine and alert screens | The rules themselves ("Tool left hangar while checked out") |
| Handheld screens | Which operations are meaningful (the handheld still shows all of them – see GAP-ANALYSIS G-H11) |

The UI is therefore **generic with template-provided content**, not template-specific screens. That is
the right architecture; the gap is that the generic UI does not yet *hide* what a template does not use.

---

## 2. Status vocabulary

The same words must mean the same thing on web, handheld and in alerts. The table below is the
vocabulary as implemented (enum names on the wire) with the meaning a user should read into them and
the visual tone each surface uses today.

| Domain | Values (as implemented) | Meaning for the user | Web tone | Handheld tone |
|---|---|---|---|---|
| **Item status** (`ItemStatus`) | Active · Missing · Disposed · Retired | Active = normal. Missing = a stocktake was applied and the item was not found; it clears on the next read/count. Disposed = retired, tags released. Retired = declared, unused by operations | ok · warn · crit · neutral | ok/warn/crit via `toneForStatus` |
| **Lifecycle state** (`Item.State`) | template-defined (Clean, Issued, InWash, …) | Where the item is in its process. Which operations are allowed next depends on it | neutral badge; allowed operations tinted on Item detail | text; lifecycle diagram on Lookup |
| **Location** | location name + kind; `—` when none | Where the platform believes the item is | text | text |
| **Custody** | party name or none; due-back date | Who holds the item and when it is due back; *overdue* is derived (`item.overdue`) | text; "Overdue" chip filter on Items | text on Lookup |
| **Presence** (`PresenceSession`) | open (present) / closed (exited) with dwell | Whether a fixed reader has seen the item in a zone within the timeout (5 min default, `presenceTimeoutSec`) | occupancy counts | not shown (**Missing UX** on handheld, acceptable) |
| **Operation status** (`OperationStatus`) | Draft · Completed · Cancelled | An operation always completes; failures are per line | — | — |
| **Line result** (`LineResult`) | Ok · Unknown · Unexpected · Rejected | Ok applied. Unknown = tag not commissioned. Rejected = business rule refused this item (message says why). Unexpected is only used by stocktakes | ok · info · warn · warn | ok · info · warn · crit (`resultTone`) — **inconsistent with web** |
| **Stocktake status** (`StocktakeStatus`) | Open · Reconciled · Applied · Cancelled | Open = scanning. Reconciled = unseen items are now Missing *in this stocktake*. Applied = the item records were changed | info · neutral · ok · neutral | badge |
| **Stocktake result** (`StocktakeResult`) | Pending · Found · Missing · Unexpected · Unknown | Pending/“Not yet seen” while open, Missing after reconcile; Unexpected = belongs elsewhere; Unknown = uncommissioned tag | neutral · ok · warn (stat shows crit) · warn · info | ok · crit · warn · info |
| **Workflow progress** | per step: done · current · upcoming; per step result ok/rejected/queued | Handheld only; badge row 1…n | — | ok · info · neutral |
| **Device health** (`DeviceHealth`) | Unknown · Online · Degraded · Offline · Updating | Online = heartbeat within SLA (15 min fixed, 24 h handheld/printer). Degraded ≤ 2×SLA. Offline beyond | ok · warn · crit · info | reader driver status: ready/inventory/locating (ok), disconnected (warn), error (crit) — a *different* vocabulary for the local reader |
| **Sync state** (handheld queue) | n waiting · sent/remaining · rejected (hidden) | Operations not yet accepted by the server | — | warning panel on Home; **rejected list is Missing UX** |
| **Alert severity** (`Severity`) | Info · Warning · Critical | Critical needs action now (safety, security, contamination); Warning needs action today (overdue, low stock, max cycles); Info is for the record | info · warn · crit | not shown |
| **Alert status** (`AlertStatus`) | Open · Acknowledged · Closed | Open = nobody has looked; Acknowledged = someone owns it; Closed = resolved | info · neutral · neutral | — |
| **Print job** (`PrintJobStatus`) | Queued · Printing · Printed · Failed · Cancelled | | neutral/ok/crit | message text |

**Recommended improvement (consistency).** Two disagreements to settle before the pilot, both without
backend work: (1) *Rejected* is `warn` on web and `crit` on the handheld; (2) *Missing* is `warn` in
`toneForStatus` but rendered as a `crit` stat on the stocktake page. Pick one tone per word and use it
on both surfaces (see [UX-CONSISTENCY.md](UX-CONSISTENCY.md) §1).

---

## 3. Capability-driven navigation model

Today the web sidebar shows every page to every user (admin pages hidden for non-admins) and the
handheld home shows every tile to everyone. The model the product *should* follow, and can follow
without new backend concepts, is:

```
Solution template(s) installed ──► item types, operations, workflows, rules, location kinds
        │
        ▼
Capabilities present in this tenant           Role of the signed-in user (global or per site)
  · serialized items?  · quantity items?          · Admin  · Operator  · Viewer  · Device
  · containers?        · custody (Issue/Return)?  · portal party?
  · lifecycle states?  · fixed readers?
  · RTLS anchors?      · GPS assets?
  · printers?          · billing rate cards?
        │                                                 │
        └──────────────────────┬──────────────────────────┘
                               ▼
              Navigation actually shown (web sidebar, handheld home)
```

Concretely:

| Capability signal (already queryable) | Show | Hide when absent |
|---|---|---|
| Any item type with `Category = Quantity` (`GET /api/item-types`) | Stock, Adjust/Move quantity operations | Stock page, Adjust tile |
| Any operation definition that requires a party, or a template op set containing Issue/Return | Parties, Issue/Return operations, custody chips | Parties page for a pure warehouse tenant |
| Any workflow (`GET /api/workflows`) | Workflows tile (handheld), Workflows → Runs (web) | Workflows tile |
| Any device with antennas having X/Y (`GET /api/devices`) | Presence → Floor plan, handheld Floor plan | both |
| Any geofence or GPS fix (`/api/geo/*`) | Map & geofences, handheld Map | both |
| Any Printer device | Print queue, Label designer, print buttons | all three |
| Any billing rate card (`/api/billing/rate-cards`) | Billing, Portal invoices | Billing |
| Any `Category = Serialized` item type with `TracksExpiry`/`ReorderPoint` | Expiry/low-stock chips | chips |

The operation list is already capability-aware in one place: `GET /api/operations/definitions`
returns `itemTypeCodes` per definition, and the web operation form shows definitions grouped as
built-in and "Vertical / custom". Extending that to the sidebar and the handheld home is a UI-only
change and is recommended (GAP-ANALYSIS G-IA1, G-H11). The **role** half of the model is already
implemented on the server (policies Admin/Operator/Viewer plus per-site roles); the web only applies it
to sidebar *links*, not to routes or to buttons inside pages (GAP-ANALYSIS G-IA3).

---

## 4. The questions this documentation set answers

| Question | Where |
|---|---|
| How is a deployment configured, in what order, and what is validated? | [SETUP-FLOWS.md](SETUP-FLOWS.md) |
| What does an administrator see? What does an operator see? What does a handheld user see? | [ROLES.md](ROLES.md), [SCREEN-INVENTORY.md](SCREEN-INVENTORY.md), [HANDHELD.md](HANDHELD.md) |
| What happens automatically when a fixed reader reads a tag? | [flows/READER-ONBOARDING.md](flows/READER-ONBOARDING.md) §4, [verticals/LINEN.md](verticals/LINEN.md) exception paths |
| What happens when the network is down? | [OFFLINE.md](OFFLINE.md) |
| How are errors surfaced and recovered? | [ERROR-STATES.md](ERROR-STATES.md) |
| How does a linen deployment differ from an artwork deployment end to end? | [verticals/LINEN.md](verticals/LINEN.md) vs [verticals/ARTWORK.md](verticals/ARTWORK.md) |
| Which parts of the UI are generic and which are template-driven? | §1 above, [INFORMATION-ARCHITECTURE.md](INFORMATION-ARCHITECTURE.md) §4 |
| Which setup steps should be wizards? | [SETUP-FLOWS.md](SETUP-FLOWS.md) §3, [WIZARDS.md](WIZARDS.md) §3 |
| Which screens are unnecessary or confusing? | [INFORMATION-ARCHITECTURE.md](INFORMATION-ARCHITECTURE.md) §2–3, [SCREEN-INVENTORY.md](SCREEN-INVENTORY.md) |
| Which UX gaps block a realistic pilot? | [GAP-ANALYSIS.md](GAP-ANALYSIS.md) §3–4 |

## 5. Conventions used in flow diagrams

Flow diagrams in `flows/` and `verticals/` tag every step with who or what performs it:

```
[USER]   USER ACTION          a person does something on a screen or with a device
[SYS]    SYSTEM ACTION        the API does something synchronously in response
[HW]     HARDWARE EVENT       a reader, antenna, printer or GPS unit produces or consumes data
[AUTO]   BACKGROUND AUTOMATION a hosted service, outbox handler, rule or scheduler acts with nobody present
```

Wireframes are low-fidelity ASCII; they show what is on the screen and the order of decisions, not
visual design.

# First-time deployment and setup journey

What an administrator has to do to take an empty tenant to a running site, in the order the
dependencies require, and how well each step is supported today.

Legend for **UI support**: ✅ dedicated UI · 🟡 possible but awkward · ❌ not in the UI (config file or
API only).

## 1. Step-by-step

| # | Step | UI support | Page | Wizard? | Prerequisites | Validation today | Success state | What fails / how the user finds out |
|---|---|---|---|---|---|---|---|---|
| 1 | **Create the tenant** | ❌ | none. The startup seeder creates the tenant (`Seed:*` configuration) and the first Admin. There is no tenant management API or UI | no | database, `ConnectionStrings`, `Seed:AdminEmail/AdminPassword`, `Seed:Scenarios` (`all` loads 23 demo scenarios – set to `none` for a real tenant) | none | admin can sign in | If seeding was left at `all`, the tenant is full of demo sites and items. **Missing UX**: a "reset demo data" or "new tenant" action. Acceptable for a single-tenant pilot run by the developers; a blocker for self-service |
| 2 | **Choose vertical / install solution template** | ✅ | Configure → Solution templates → *Install* | one click; **should be the first screen of a setup wizard** | step 1 | server validates template ops/workflows; idempotent (`already present` count) | green summary "n item types and n rules installed"; the *Installed* badge | Nothing tells the admin what to do next. Installing several templates is allowed and mixes rule sets. No confirmation before install (harmless: idempotent) |
| 3 | **Define item types and lifecycle** (if not from a template) | 🟡 | Configure → Item types → + New type; lifecycle and attribute schema as raw JSON | no | — | JSON parse (`alert()`), server code uniqueness | card with lifecycle diagram | Wrong state names surface only later as "Invalid state 'X'" when an operation runs |
| 4 | **Create locations** (site → building → zones → racks/bins…) | ✅ | Configure → Locations → + New location under X | no; tree editor is adequate | none; a **Site** must exist first (users' site roles, stocktakes, muster all key on Site) | kind list; parent cycle check | tree | Attributes that rules depend on (`clean: true` for contamination rules, `presenceTimeoutSec`, `widthM/heightM` for floor plans) are raw JSON with no hint that they exist. **Missing UX**: per-kind fields |
| 5 | **Create parties** (custodians, customers, departments) | ✅ | Configure → Parties | no | — | server | list | No empty state on the page. Needed only for Issue/Return/Dispatch-to-party templates; the page is shown to every tenant |
| 6 | **Register readers/gateways and antennas** | 🟡 | Configure → Readers & devices → + New device; antennas with port, location, direction, power, x/y | **should be a wizard** ([flows/READER-ONBOARDING.md](flows/READER-ONBOARDING.md)) | locations for the antennas | none beyond required name/kind; LLRP connection is attempted in the background | device card; *Status / GPIO* shows `connected: true`; Live reads shows reads | `llrpHost`, `edgeManaged`, `edgeGatewayId`, `mqttTopic` typed into a JSON field; a typo is silent (the reader never connects). No "first read received" confirmation |
| 7 | **Configure the edge agent** (optional) | ❌ + 🟡 | web: create a *Gateway* device, *Issue token*, tick `edgeManaged` and set `edgeGatewayId` on each reader (JSON). Box: `appsettings.json` / env (`Edge__Server__Url`, `Edge__Server__DeviceToken`) | partly (the config *pull* means the reader list can be central) | step 6 | none on the web; the agent logs | gateway device `Online`; heartbeat metrics show `readers[].connected` (not rendered – **Missing UX**) | Agent misconfiguration is only visible in agent logs or as a permanently `Unknown`/`Offline` gateway |
| 8 | **Register handhelds and issue tokens** | ✅ | Readers & devices → + New device (kind Handheld) → *Issue token* (shown once) → type on the handheld login | 🟡 should show a QR | — | token exchange `POST /api/auth/device` | handheld signs in; device `LastSeenAt` | 48-character token typed by hand; no confirmation on *Rotate token* (old token stops working) |
| 9 | **Create users and assign roles / sites** | ✅ | Configure → Users → + New user; *Sites* for restriction | no | a Site (step 4) for site restriction | password required; cannot restrict self | list row; "Changes take effect at the user's next sign-in" | No deactivate confirmation (checkbox); no e-mail invite (**Missing product capability**, fine for a pilot) |
| 10 | **Configure rules and alerts** | ✅ (template-provided) / 🟡 (custom) | Configure → Rules | no | item types, locations (for location conditions), notification channels (Admin) for Notify | server validates action params | row | Condition syntax (`item.state eq Contaminated`) is a query language; no test button (**Missing UX**: "evaluate against the last 100 events") |
| 11 | **Configure notifications / escalation** | ✅ | Configure → Notifications (Admin) → channel → *Send test* → policy | no | — | *Send test* | delivery log | Secrets partly in plain inputs (HMAC secret, API token) |
| 12 | **Configure integrations** | ✅ | Configure → Integrations (Admin) → endpoint → *Deliver now* | no | — | *Deliver now*, `lastError` | row shows last delivery | HMAC shared secret in a plain input |
| 13 | **Configure guided workflows** | ✅ (template) / 🟡 (custom) | Configure → Workflows | no | operations exist; item types | server validates steps | row; handheld tile | Deleting a used workflow "disables" it silently |
| 14 | **Print and encode labels** | ✅ | Configure → Label designer (per type) → Track → Encoding (serial pools, batches) → Print queue | Encoding is a wizard ✅ | a Printer device with `host`/`port` in Config JSON; a serial pool | preview with duplicates check | committed batch; printed jobs | Printer host in JSON; printer failures only in the queue |
| 15 | **Commission items** | ✅ | handheld Commission; web Items → + New item, Tags → Commission, ERP import (Admin) | no | item types; tags | server uniqueness | item with tag | Four entry points ([INFORMATION-ARCHITECTURE.md](INFORMATION-ARCHITECTURE.md) §2 #8) |
| 16 | **Verify end to end** (scan a tag on the handheld, read it on a fixed reader, see the event and an alert) | 🟡 | Live reads, Item detail history, Alerts | **should be the last step of the setup wizard** | all above | none automated | event visible | Nothing tells the admin "your deployment is complete" |

## 2. Recommended minimal checklist (until a wizard exists)

Written for the pilot administrator; every line is verifiable on an existing page.

```
[ ] 1  API started with Seed:Scenarios=none (or the demo tenant was reset)   → Login works
[ ] 2  Solution template installed                                            → Templates: Installed badge
[ ] 3  Site + zones created; attributes set where rules need them              → Locations tree
[ ] 4  Parties created (if the template uses Issue/Dispatch)                   → Parties list
[ ] 5  Each fixed reader: device + antennas mapped to zones + direction        → Devices card, Status: connected
[ ] 6  Gateway device + token issued, edge agent running, readers edgeManaged  → Devices: gateway Online
[ ] 7  Handheld device + token issued, handheld signed in, location chosen     → Devices: LastSeen updates
[ ] 8  Users created with roles (and sites)                                    → Users list
[ ] 9  One tag commissioned from the handheld                                  → Items: item with tag
[ ] 10 Tag read by a fixed reader                                              → Live reads, Item detail history: Moved/Seen
[ ] 11 One template rule fired and the alert appears                           → Alerts
[ ] 12 One operation submitted offline and synced                              → Operations list
```

## 3. Which steps should be wizards

| Step | Wizard | Reason | Backend work |
|---|---|---|---|
| 2 + 4 + 8 + 9 + 16 | **Tenant setup wizard** (see wireframe below) | Nine pages with an implicit order; the end state is verifiable | none (optionally a capabilities/status endpoint) |
| 6 (+7) | **Reader onboarding wizard** | Highest usability cliff; verifiable by "first read received" | none |
| 8 | **Handheld provisioning** (QR) | Removes a 48-character typing step | none |
| 3, 10 | not wizards | Better served by structured editors; templates cover the pilot | — |

### Wireframe — tenant setup wizard (Recommended improvement, not built)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Set up your deployment                                       step 2 of 6    │
│ ● Template   ● Site & zones   ○ Devices   ○ Handhelds   ○ People   ○ Verify │
├─────────────────────────────────────────────────────────────────────────────┤
│ Site & zones                                                                │
│                                                                             │
│ Site name          [ Central Laundry              ]                         │
│ Zones the template expects (Linen, Laundry & Uniforms):                     │
│   [x] Soiled receiving      kind Zone                                       │
│   [x] Wash hall             kind Zone                                       │
│   [x] Clean store           kind Zone   attributes: clean = true            │
│   [x] Ward issue point      kind Zone                                       │
│   [ ] + add another zone                                                    │
│                                                                             │
│ ⓘ "clean = true" is what the rule "Contaminated linen in clean zone" uses.  │
│                                                                             │
│                                              [ ‹ Back ]   [ Create & next › ]│
└─────────────────────────────────────────────────────────────────────────────┘

Step 6 – Verify
┌─────────────────────────────────────────────────────────────────────────────┐
│ Verify                                                                      │
│ ✓ Template installed (3 item types, 2 rules, 1 workflow)                    │
│ ✓ Site "Central Laundry" with 4 zones                                       │
│ ✓ Reader "Clean store gate" connected · antenna 1 → Clean store (In)        │
│ ⧗ Waiting for a handheld to sign in with token …  (Handheld "HH-01")        │
│ ✗ No tag commissioned yet            [ How to commission on the handheld ]  │
│ ✗ No fixed read received yet         [ Open Live reads ]                    │
│                                                                             │
│ Your deployment is ready when every line is ✓.        [ Finish ]           │
└─────────────────────────────────────────────────────────────────────────────┘
```

The template already knows its location kinds (`LocationKinds`) and party kinds (`PartyKinds`); it does
not know *suggested zone names*. Adding a `SuggestedLocations` list to templates would be a small
content change and is Optional.

## 4. Flow diagram — first deployment (as built)

```
[USER]  Ops engineer sets ConnectionStrings, Seed:AdminEmail/Password, Seed:Scenarios=none; starts API
[AUTO]  Migrations run; tenant + Admin user created (SeedData.EnsureSeededAsync)
[USER]  Admin signs in (web) ─► Solution templates ─► Install "linen-laundry"
[SYS]   TemplateProvisioner creates item types, rules, operation definitions, workflows (idempotent)
[USER]  Locations: create Site → zones (+ attributes JSON where rules need them)
[USER]  Parties: wards / customers / employees (if Issue/Dispatch are used)
[USER]  Readers & devices: + New device (Fixed) → antennas → port/location/direction → Config JSON (llrpHost…)
[SYS]   LlrpReaderService supervises: connects to llrpHost:5084, applies power/session/antennas
[HW]    Reader connects; capabilities read; ROSpec started
[USER]  (optional) + New device (Gateway) → Issue token → configure edge agent on site → mark readers edgeManaged
[AUTO]  Edge agent logs in with token, pulls /api/edge/config, drives readers, heartbeats every 60 s
[USER]  + New device (Handheld) → Issue token → type token on handheld login
[SYS]   POST /api/auth/device issues a 30-day Device JWT
[USER]  Users: create supervisor/operator accounts; Sites restriction if needed
[USER]  Handheld: Settings → set current location; Commission first tags
[SYS]   Commission creates items bound to EPCs at the current location
[HW]    Fixed reader reads a commissioned tag
[SYS]   ReadIngestionService: resolve EPC → Moved/Seen event → presence → rules
[AUTO]  Rule "Contaminated linen in clean zone" (if matched) → CreateAlert → outbox → live.alert, notifications
[USER]  Admin sees the alert on Alerts / Dashboard → deployment verified
```

## 5. Where setup is blocked or silently wrong today

| Situation | Symptom | Label |
|---|---|---|
| Real tenant started with default seeding | 23 demo sites and thousands of demo items in the tenant | Recommended improvement: default `Seed:Scenarios` to `none` outside Development, and document it |
| Reader Config typo | reader never connects; nothing red anywhere except `connected: false` in a modal | Recommended improvement (IA-4, reader wizard) |
| Antenna without location | reads recorded, item never moves | Missing UX (E-8) |
| Location attribute expected by a rule is missing | rule never fires; no indication | Missing UX: rules could list the attributes they reference; location form could offer them |
| Two templates installed with overlapping rule names | second install skips those rules (`Skipped` count) | acceptable; shown in the summary |
| Admin forgets to set the handheld's current location | Commission and Receive place items nowhere (`toLocationId` undefined) | Recommended improvement: the handheld should require a current location before Commission/Receive (small, handheld only) |

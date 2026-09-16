# Information architecture review (web)

The web client has 38 pages in three sidebar groups plus two detail routes, the login/SSO pages and the
portal. This document reviews what is there, what overlaps, what should merge or nest, what is too
technical for its audience, and proposes the capability-driven navigation the product should move to.

## 1. Navigation as built

```
Overview                  Track                    Configure
  Dashboard                 Items                    Item types
  Live reads                Tags                     Locations
  Alerts  [n]               Operations               Parties
  Presence & location       Stocktakes               Readers & devices
  Map & geofences           Stock                    Rules
  Event log                 Print queue              Workflows
  Reports                   Encoding                 Notifications          (Admin)
  Analytics                 Billing                  Label designer
  Anomalies                 Maintenance              Solution templates
                                                     Integrations           (Admin)
                                                     ERP import             (Admin)
                                                     Users                  (Admin)
                                                     Cluster & system       (Admin)
                                                     Audit & retention      (Admin)
                                                     EPCIS 2.0              (Admin)
  ── not in nav: Items/:id, Stocktakes/:id, Portal (replaces the whole app for portal users)
```

Facts that shape the review:

- Admin-only affects **links only**. Routes are not guarded; an Operator can type `/users`.
- Every link is shown to every tenant regardless of what is installed: a linen tenant sees *Stock*,
  *Billing*, *Encoding*, *Map & geofences*, *Maintenance* and *EPCIS 2.0*.
- Nine pages have no empty state at all (Events, Tags table, Operations, Parties, Rules table, Users,
  Stock balances/movements, Devices grid, Stocktake lines) and thirteen have no error rendering.
- Only the sidebar, the login page, Dashboard verbs and two Portal strings are translated.

## 2. Overlaps and duplication

| # | The same thing shown in | Assessment |
|---|---|---|
| 1 | **Alerts**: `/alerts`, Live page pane, Dashboard list widget, sidebar badge, Anomalies (raises alerts), Devices health (offline alerts) | Acceptable: one source of truth (`/api/alerts`), several views. The Live page pane is redundant with the sidebar badge and could go |
| 2 | **Reader health**: Devices → Health & SLAs (`/api/devices/health`) vs Analytics → Reader uptime (`/api/analytics/uptime`) vs Dashboard devices widget | Two different endpoints answering "are my readers up". **Merge**: keep Devices → Health as the operational view; Analytics keeps only the 30-day trend |
| 3 | **Stocktake accuracy**: Stocktakes table vs Analytics → Inventory accuracy vs Dashboard widget | Acceptable as summary vs detail |
| 4 | **Spatial**: Presence → Floor plan (indoor x/y, heat map, replay) vs Map & geofences (outdoor GPS) | Two map UIs with separate hour selectors and item pickers. **Nest**: one *Location* section with tabs *Zones · Floor plan · Map*; the current top-level *Map & geofences* is only meaningful for GPS tenants |
| 5 | **Event history**: Event log, Item detail history, Dashboard list widget, EPCIS console, Audit log (HTTP level) | Event log and Item detail history are the two that matter. EPCIS console is a developer tool (§3). Audit is a different thing (who called which API) and is correctly separate |
| 6 | **Printing**: Item detail print buttons + "Print history" line vs Print queue page | Acceptable: act on the item, review in the queue |
| 7 | **Billing vs Portal** invoices/ledger | Two audiences, two endpoint families; acceptable. Two `money()` helpers disagree on negative formatting (UX-CONSISTENCY §1) |
| 8 | **Creating an item / commissioning**: `/items` → + New item, `/tags` → Commission (twice), Encoding wizard "Create items and tags", handheld Commission | Four entry points to `POST /api/items`. **Merge**: Tags should not have its own Commission form; it should route to the item creation modal with the EPC prefilled (it does, via `NewItemModal`) and say so |
| 9 | **Template install** on `/templates` and linked from `/item-types` | Fine |
| 10 | **Run operation** on `/operations`, Item detail quick actions, Stock → Move picked lots | Fine: one `OperationForm` component |
| 11 | **Low stock**: Items → Low stock chip, Stock → Summary shortfall, dashboard stat | Fine |
| 12 | **Notification channels** edited on `/notifications`, attached inside `/rules` | Fine, but the rule modal says "No channels yet (Notifications page)" to Operators who cannot open that page |
| 13 | **Inspection due**: Maintenance risk table, Items chip, Item detail | Fine |

**Dead or unreachable elements found**

- Parties → "Items in custody" links to `/items?custodianPartyId=…`, which the Items page ignores (filter silently dropped). Recommended improvement: honour the parameter or remove the link.
- `Empty` component exists in `ui.tsx` but pages inline their own empty markup.
- `ItemStatus.Retired` exists in the enum but no operation produces it (Dispose sets Disposed); the templates use lifecycle state *Retired* instead. Not a UI bug, but the two "Retired"s will confuse.
- Devices → Firmware → *Mark done* is labelled `title="Simulate the device finishing the update"`: a test affordance on a production screen.
- Login page pre-fills demo credentials and prints them under the form. Must go before a pilot (G-SEC1; small, fixed).

## 3. Too technical for the audience

| Screen / element | Why | Recommendation |
|---|---|---|
| Readers & devices → Edit → **Config (JSON)** | `llrpHost`, `llrpPower`, `llrpAntennas`, `edgeManaged`, `edgeGatewayId`, `mqttTopic`, `vendor`, `host`/`port`, `labelStock` are typed by hand | **Recommended improvement**: kind-specific fields (Fixed reader: LLRP host/port/power/session/antennas, "Driven by edge agent" checkbox + gateway picker; Printer: host/port/label stock; Gateway: nothing). No backend change: the fields serialise into the same `Config` dictionary |
| Item types → Edit → **Lifecycle JSON** and **Attribute schema JSON** | Raw JSON with an `alert()` on parse failure | Recommended improvement: a table editor for states/transitions and attributes. Until then, keep templates as the primary path (they are) |
| Locations → **Attributes JSON** (`clean: true`, `presenceTimeoutSec`, `widthM`/`heightM`, `order`) | These keys drive rules, presence and floor plans and are undiscoverable | Recommended improvement: named optional fields per kind |
| Rules → condition editor (`item.state eq Contaminated`, `toLocation.clean eq true`, `data.direction eq Out`) | Field paths are a query language | Acceptable for Admins with the lookups help; add a field picker (Optional future enhancement) |
| **EPCIS 2.0** console | Raw query parameters and a JSON capture body | Developer tool. Move to an *Integrations → EPCIS* tab, or out of the nav entirely with the API docs |
| **Cluster & system** | Nodes, leases, feature flags | Operations tool. Keep Admin-only; rename *System status* |
| **Encoding** wizard | GS1 schemes and company prefixes | Appropriate for the person who does it; hide the page for tenants with no serial pool |
| Live reads empty state "Post to /api/ingest/reads to see them here" and Map empty state "Post to POST /api/ingest/gps …" | API instructions in operator-facing copy | Recommended improvement: reword ("Reads appear here as soon as a reader or handheld scans") |
| Templates → Export uses `prompt('Package version')` | Native prompt | Small form field |

## 4. Module- and role-gated navigation (proposed)

Group by **what the user is doing**, not by data type, and show a group only when the tenant has the
capability. Fewer top-level entries, clearer purpose:

```
HOME            Dashboard · Alerts [n]
OPERATE         Items (+ detail) · Operations (history + run) · Stocktakes (+ detail) · Workflows → Runs
                Stock                      ← only when a Quantity item type exists
                Print queue                ← only when a Printer device exists
MONITOR         Live reads · Presence (Zones · Floor plan · Map) · Event log · Reports · Analytics
                Anomalies, Maintenance     ← Optional modules; show when enabled by template or admin toggle
CONFIGURE       Solution templates · Item types (+ label design) · Locations · Parties
                Readers & devices (Devices · Health · Firmware · Edge gateways)
                Rules & notifications (Rules · Channels · Policies)
                Workflows (definitions)
                Tags & encoding (Tags · Encoding · Serial pools)   ← Encoding only with a pool
ADMIN           Users & sites · Integrations (Endpoints · ERP import · EPCIS · Warehouse export) ·
                Billing (with rate cards)   ← only when the returnable-assets/rental content is installed
                Audit & retention · System status
```

Rules of the model (see README §3 for the capability signals):

1. **Capability first**: a link appears only if the tenant has the thing the page manages. Signals are
   already available from `GET /api/item-types`, `/api/devices`, `/api/workflows`, `/api/geo/fences`,
   `/api/billing/rate-cards`, `/api/encoding/pools`.
2. **Role second**: hide links *and* guard routes *and* hide mutation buttons using one client-side
   `can()` mirror of the server policies (Admin / Operator / Viewer, per-site aware).
3. **Detail pages carry the actions**: Item detail is where operations, printing and binding happen;
   list pages filter and navigate.
4. **One spatial section**, one health section, one alerts list.
5. **Developer tools out of the main nav**: EPCIS console, Cluster, warehouse export live under Admin.

Backend work needed: none for the grouping and capability gating. A `GET /api/tenant/capabilities`
endpoint would make the client simpler and is a small addition (Optional).

## 5. Handheld information architecture

See [HANDHELD.md](HANDHELD.md) §2. The handheld shows nine tiles to everyone. Applying the same model:

- *Floor plan* and *Map & geofences* only when the tenant has anchors / fences (both are pulled by
  `Download for offline`, so the signal is local).
- *Workflows* only when at least one workflow is enabled.
- *Commission* only for users/devices allowed to create items (Operator or above; it already is).
- The *Operations* screen should list only operations whose `itemTypeCodes` intersect the installed
  item types, and should default to the operation the template uses most (Issue/Return for linen and
  tools; Receive/Transfer for warehouse). Built-ins have no `itemTypeCodes`, so a template's `Operations`
  list (already present in `SolutionTemplate`) is the right signal; it is not currently exposed on
  `GET /api/templates` per installed tenant (**Missing UX**, small backend addition).

## 6. Summary of IA recommendations

| ID | Recommendation | Type | Backend |
|---|---|---|---|
| IA-1 | Capability-gated sidebar and handheld home | Recommended improvement | none (optional capabilities endpoint) |
| IA-2 | Merge spatial views under *Presence*; merge reader uptime into Devices → Health | Recommended improvement | none |
| IA-3 | Client-side `can()` mirror: guard routes, hide mutation buttons for Viewer/Operator | Recommended improvement (P2 in GAP-ANALYSIS) | none |
| IA-4 | Kind-specific device form instead of Config JSON | Recommended improvement (P1 for reader onboarding usability, does not block a pilot run by the developers) | none |
| IA-5 | Move EPCIS console, Cluster, Warehouse export under Admin; reword API-flavoured empty states | Recommended improvement | none |
| IA-6 | Fix the dead `custodianPartyId` link; remove *Mark done* simulator button or label it as test-only | Recommended improvement | none |
| IA-7 | Remove hard-coded demo credentials from Login | Fixed (G-SEC1) | none |

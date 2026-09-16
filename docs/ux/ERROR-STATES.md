# Error and exception UX

How errors reach a user today, and who has to do what. "Auto-recovery" means the platform recovers
without anyone acting.

## 1. How errors are rendered (facts)

| Surface | Mechanism | Shape |
|---|---|---|
| API | `ErrorHandlingMiddleware`: `NotFoundException` → 404, `DomainException` → 400, `DbUpdateException` → 409 "Conflict: …"; controllers also return `{ error }` or `{ errors: [] }` | `{ "error": "<message>" }` |
| Web | `ErrorBox` renders `error.message` in a red box; `api()` on 401 clears the token and dispatches `rfid:unauthorized` → forced logout | raw server message, no code, no retry, no dismiss |
| Web (thirteen pages) | no `ErrorBox` at all (Alerts, Live, Events, Presence, Reports, Items, Tags, Operations, PrintQueue, Maintenance, Users, Portal; Cluster uses a raw div) | query failures are invisible; the page just stays empty |
| Handheld | `setMsg((e as Error).message)` in each screen; `ApiError.message` = server `error` or "`<status> <statusText>`" | one text line per screen, colour varies by screen |
| Handheld queue | transport errors → retry with back-off; 4xx → thrown to the screen (live) or moved to the rejected list (sync) | see [OFFLINE.md](OFFLINE.md) |
| Edge agent | logs; 4xx batches → `poison/` with `.reason`; heartbeat carries `lastError`, `queuePoison` | nothing rendered on the web (**Missing UX**) |

## 2. Error table

Columns: **Sees** (what the user sees today) · **Retry** (can the user retry safely) · **Intervention**
(who must act) · **Auto** (does the platform recover by itself) · **Admin diagnosis** (where an admin
finds out).

| Error | Where | Sees | Retry | Intervention | Auto | Admin diagnosis |
|---|---|---|---|---|---|---|
| **Unknown EPC** (tag not commissioned) | handheld operation submit; fixed reader | Handheld: line result `Unknown` "Tag not commissioned" *after* submit; nothing before. Fixed: nothing anywhere except Tags → *Unknown EPCs seen by readers* | yes (idempotent) | operator commissions the tag (Commission screen, EPC prefilled from Lookup) | no | Tags page unknown-reads panel (count, last seen, device) |
| **Duplicate tag in one operation** | handheld/web submit | line `Rejected` "Duplicate in operation" | yes | none needed; the first line applied | — | Operations → detail lines |
| **Item in wrong lifecycle state** | operation submit | line `Rejected` "Issue not allowed while X is 'Dirty'" (from lifecycle) or "No transition from 'A' to 'B'" | yes, after fixing | operator chooses the right operation, or a supervisor uses Process stage on the web | no | Operations → detail |
| **Operation not allowed for item type** | submit | line `Rejected` "Calibrate does not apply to Linen" | yes | operator | — | same |
| **Disposed item scanned** | submit / fixed read | line `Rejected` "X is disposed"; fixed read: recorded as a read but item stays Disposed (`Status` refreshed only for Missing) | — | supervisor decides (re-commission not supported: tags are released on dispose) | — | Item detail history |
| **Missing destination / party / container** | submit | whole request 400 "Transfer requires a destination location"; handheld disables Submit until valid so this is rarely seen; web form marks required fields | yes | operator | — | — |
| **Container cycle / depth** | Pack | line `Rejected` "Cannot pack X into Y: it already contains X (cycle)" or 400 "Container nesting deeper than N levels" | yes | operator | — | — |
| **Quantity negative / insufficient** | Adjust / Move quantity | line `Rejected` "Quantity cannot go negative", "Only 4 ea of X available at the source" | yes | operator | — | Stock → Movements |
| **Duplicate submission** (double tap, replayed queue) | submit | identical result to the first (server replays by `clientId`) | n/a | none | **yes** | Operations list shows one operation |
| **Handheld offline at submit** | handheld | "Offline – queued for sync (n pending)"; Home shows "n operation(s) waiting to sync" | automatic | none | **yes** (back-off 5 s → 10 min, batch of 50) | none today (**Missing UX**: web has no per-device queue view; heartbeats from handhelds do not report queue depth) |
| **Queued operation rejected on sync** (business error surfaced later) | handheld | nothing (rejected list has no UI) — **fixed in P1 shortlist**: Settings shows the rejected list with reason and *Clear* | not retried (correct) | operator/supervisor reviews and redoes the work | no | none on web; the operation was never created |
| **Queue exhausted (20 attempts)** | handheld | moved to rejected "Gave up after 20 attempts: …" (hidden until the P1 fix) | manual redo | operator | no | none |
| **Token expired – web** (12 h user JWT) | web | immediate logout to the login page, no message | sign in again | user | — | Audit log 401 rows |
| **Token expired – handheld** | handheld | before fix: bare "401 Unauthorized" on every screen; live submits not queued; sync retries a 401 as a transport error for ~3 h then drops to rejected. **After the P1 fix**: "Session expired – sign in again" banner with a Sign in button; queue is preserved and 401 batches are not counted as attempts | after re-login the queue drains | operator signs in | partial | — |
| **Wrong role for an action** | web | 403 body rendered raw ("Forbidden" or empty) after clicking a visible button; handheld: 403 message | no | admin grants the role, or the user stops | — | Audit log |
| **Location outside the user's sites** | web/handheld operations | 400 "Location is outside your sites" / "Requires Operator role on this site" | no | admin (Users → Sites) | — | Users → Sites |
| **Portal user hits the main API** | web | 403 "Portal accounts can only use the portal API" (portal users never see the main app, so only via a stale tab) | — | — | — | — |
| **Reader offline** | fixed | Devices → Health: `Degraded` after SLA, `Offline` after 2×SLA; alert if a rule/SLA raises one; Dashboard devices widget | Reconnect button (LLRP status modal, server-driven readers only) | technician | yes when it reconnects (LLRP supervisor reconnects; edge agent reconnects) | Devices → Health, heartbeats list |
| **Reader connected but no reads** | fixed | Live reads stays empty; LLRP status `tagsReceived` static | — | technician (antenna port, power, location mapping) | — | LLRP status modal, edge heartbeat `readers[].tags` (not rendered – **Missing UX**) |
| **Reader reads but antenna has no location** | fixed | reads are recorded, items get `LastSeen` with no location move; no warning | — | admin maps the antenna | — | none (**Missing UX**: a "antenna without location" warning on the device card) |
| **Reader on both edge and server** (`edgeManaged` unset while configured on the agent) | fixed | double ingestion; presence flaps | — | admin sets `edgeManaged` | — | Edge agent badge on device card; Anomalies → ItemFlapping |
| **Edge agent cannot reach server** | edge | nothing on the web until health degrades; agent logs; queue on disk grows | automatic | none until `MaxBatches`; then oldest → poison | **yes** | gateway device health (Offline); heartbeat metrics (not rendered) |
| **Edge batch rejected (4xx)** | edge | nothing on the web; `poison/<seq>.json` + `.reason` on the box | operator on the box | technician inspects `poison/` | no | gateway heartbeat `queuePoison` (not rendered) |
| **Late batch after newer reads** | edge/handheld | nothing; server records history and counts `late` | — | none | **yes** (state never regresses) | ingest result `late` (logs) |
| **Stocktake: scans on a non-open stocktake** | handheld/web | "Stocktake is not open" | no | supervisor opens a new one | — | Stocktakes list |
| **Stocktake: Apply before Reconcile** | web | "Reconcile the stocktake first" | after reconcile | supervisor | — | — |
| **Stocktake: Unexpected item** | reconcile view | line `Unexpected` with its expected location; *Apply result* moves it here | — | supervisor decides by applying or cancelling | — | Stocktake detail |
| **Stocktake: Unknown tag** | reconcile view | line `Unknown`; not applied | — | commission later | — | Stocktake detail, Tags unknown panel |
| **Commission: EPC already bound** | commission | 409/400 "EPC … already bound to X" | after choosing another tag | operator | — | Tags search |
| **Commission: identifier exists** | commission | "Identifier … already exists" | — | operator | — | Items search |
| **Serial pool exhausted / busy** | commission, encoding | "Pool 'X' has only n serials left" / "Could not allocate serials – pool is busy, retry" | yes | admin extends the pool | — | Encoding → Serial pools |
| **Printer unreachable / no host** | print | job `Failed` "printer has no host configured" or socket error; Item detail shows "Failed: … queued for retry" | *Retry* in Print queue | technician | queue processor retries | Print queue, printer label stock |
| **Label stock low** | print | alert (rule on `labelStock < labelStockMin`) | — | operator refills, admin *Set stock* | — | Print queue → printers |
| **Rule action failed** (webhook 5xx, notification failed) | background | Notifications → delivery log `Failed`; Integrations row `lastError`; alert still created | *Deliver now*, *Send test* | admin | outbox retries with back-off | Notifications log, Integrations |
| **Integration endpoint down** | background | Integrations row shows `lastError`, pending count grows | *Deliver now* | admin | **yes** (outbox) | Integrations page |
| **Template install partially exists** | web | success panel "n item types and n rules installed (n already present)"; nothing fails | idempotent | — | — | — |
| **Template package signature invalid** | import | "Package rejected: …" rendered in the (green) `.success` box | — | admin | — | — |
| **Ambiguous e-mail across tenants** | login | "This e-mail exists in more than one tenant; supply tenantCode" — but the login form has **no tenant code field** (**Missing UX**) | no | admin uses a unique e-mail | — | — |
| **Database conflict** (delete a type with items, location with children) | web | 409 "Conflict: …" or 409 "Item type has items" | no | admin moves/deletes dependants first | — | — |
| **Invalid JSON in lifecycle / attributes / config** | web | native `alert('… JSON is invalid')` | yes | admin | — | — |
| **Live hub disconnected** | web | sidebar dot turns grey (colour only) and Live page says *Disconnected* | automatic | none | **yes** (SignalR auto-reconnect) | — |
| **SSO callback failure** | web | back to login; token cleared; no message | retry SSO | admin checks OIDC config | — | API logs |

## 3. Patterns to fix (recommended, no backend work)

| ID | Recommendation | Priority |
|---|---|---|
| E-1 | One error component with: message, status (403 → "You don't have permission", 404 → "Not found", 409 → "Already exists / in use", 5xx/network → "Server unavailable – retry"), a Retry when the query can be re-run, and a dismiss | P2 |
| E-2 | Every list page renders query errors and an empty state (13 pages have neither) | P2 |
| E-3 | Handheld: one `Notice` component (tone ok/warn/crit) used by every screen instead of ad-hoc text colours | P2 |
| E-4 | Handheld: show the session-expired state globally and preserve the queue (P1, fixed) | P1 |
| E-5 | Handheld: show queued-but-rejected operations (P1, fixed) | P1 |
| E-6 | Web: confirmations before irreversible actions that lack them (Stocktake Cancel/Apply, Dispose, Import, Cluster release, Rotate token) (P1, fixed for Stocktake Cancel/Apply, Dispose and Import) | P1 |
| E-7 | Web: render gateway/edge heartbeat metrics (queue depth, poison, last error, readers connected) on the device card | P2 (Missing UX) |
| E-8 | Web: warn on the device card when an antenna has no location | P2 (Missing UX) |
| E-9 | Login: add the optional tenant code field the API already accepts | P3 (Missing UX; single-tenant pilots never hit it) |
| E-10 | Do not use the green `.success` box for failures (Templates import, Integrations delivery, Label designer hint) | P2 |

# Guided workflows and wizards

Two different things are called "wizard" in this product and they must stay separate:

1. **Guided workflows** (`Workflow` + `WorkflowStep`, run on the handheld) — a *repeated operational
   sequence* (wash cycle, tool return and check, CSSD reprocessing). Configured by an Admin on the web,
   executed by operators on the handheld, audited as ordinary operations.
2. **Admin wizards** — *one-off configuration* sequences (first deployment, reader onboarding, tenant
   setup). The GS1 Encoding wizard is the only one that exists today.

## 1. Guided workflows as built

### Definition (web: Configure → Workflows)

| Field | Meaning | Constraints (server-validated) |
|---|---|---|
| Code, Name, Description, Icon, Vertical | identity | code `^[A-Za-z][A-Za-z0-9_-]{1,40}$`, unique |
| Item type codes | which items the workflow is for | informational on the handheld today |
| Steps[] | ordered operations | `Commission` is not allowed as a step |
| Step: key, title, prompt | shown as the step header and instruction | key recorded on every operation (`operations.WorkflowStep`) |
| Step: operation | a built-in or template operation code | must exist and be enabled |
| Step: ask[] | inputs the operator is asked for | limited to `toLocation, party, targetState, quantity, dueBack, container` |
| Step: fixed{} | inputs pre-filled by the definition (toLocationId, partyId, targetState, containerItemId, reference) | hidden from the operator when fixed |
| Step: rescan | start this step with an empty tag set | default false: tags carry forward |
| Step: optional | operator may skip | shows *Skip →* |
| Step: onRejected | `stop` (default) or `continue` | `stop` excludes rejected tags from later steps |

The web editor is a modal with step rows (↑ ↓ ✕), ask-chips and the fixed inputs. Deleting a workflow
that has runs disables it instead (server returns `disabled: true`), which the UI does not explain.

### Execution (handheld: Workflows → pick → run)

Facts from `WorkflowRunScreen.tsx`:

| Behaviour | As built |
|---|---|
| Run identity | a `runId` is generated on the device; every step is `POST /api/operations` with `workflowRunId`, `workflowCode`, `workflowStep`, `clientId = runId:stepKey` |
| Carried-forward tags | step *n+1* uses `carried ∪ scanned` unless the step has `rescan`; then the set is cleared on entering the step |
| Forced rescan | `rescan=true` clears the set and shows "scan a fresh set of tags" |
| Optional steps | *Skip →* button visible before submitting; skipped steps produce no operation |
| Per-tag rejection | after submit the per-line results are listed; with `onRejected=stop` the rejected EPCs join a `stopped` set and are excluded from all later steps ("n tag(s) stopped at an earlier step are excluded") |
| Offline | `runOrQueue`: if the server is unreachable the step is queued and the screen says "Offline – step queued (n pending); it will sync automatically" — the run *continues* with the same tag set |
| Partial failure | the step completes (operation status Completed) with ok/rejected/unknown counts; the operator may go *Next step →* regardless |
| Progress | badge row "1. Load washer · 2. Unload clean" with done/current tones |
| Completion | summary list per step (ok/rejected/queued) and *Done* (goes back) |
| Abandoned run | leaving the screen loses the run; steps already submitted remain as operations. No "resume" — **Missing UX** |
| Resume | none. Runs are visible on the web (Workflows → Runs, grouped by run id) but cannot be continued from either surface — **Missing product capability** (needs a run record with current step; small backend addition) |
| Rejection at a stop step | operator sees the count; the excluded tags are not listed by name on the next step (only the count) — **Missing UX** (minor) |
| Business error for the whole step (missing destination, unknown operation) | `runOrQueue` throws for 4xx; message shown in red; step not recorded |
| Step ask `dueBack` / `quantity` / `container` | `quantity` is rendered; `dueBack` and `container` asks are accepted by the server but the handheld run screen renders **neither** (only fixed container is sent) — **Missing UX** |

### Wireframe — workflow run (as built)

```
┌──────────────────────────────────────────┐
│ ‹ Wash cycle                             │
│ [1. Load washer ✓] [2. Unload clean ●]   │
│ ┌──────────────────────────────────────┐ │
│ │ Unload clean                         │ │
│ │ Scan the clean linen as it leaves    │ │  ← step.prompt
│ │ Operation: Process stage · scan a    │ │
│ │ fresh set of tags                    │ │
│ └──────────────────────────────────────┘ │
│ [▶ Scan]  [Clear]                        │
│ 14 tag(s)                                │
│  E200 3412 …  Sheet 0412  Soiled→InWash  │  ← after submit: result per line
│  E200 3413 …  Sheet 0413  rejected: …    │
│ Target state  [Clean ▾]                  │  ← asks: targetState
│ Destination   [Clean store ▾]            │  ← asks: toLocation
│ ┌───────────────────┐ ┌────────────────┐ │
│ │ Submit Unload     │ │ Next step →    │ │
│ └───────────────────┘ └────────────────┘ │
│ ✓ Unload clean: 13 ok, 1 rejected        │
└──────────────────────────────────────────┘
```

### Assessment

The model is right: a workflow is data, steps are ordinary operations, offline works for free, the
audit trail is the operations table. The gaps are all in *presentation and continuity*:

| Gap | Label | Fix size |
|---|---|---|
| No resume / abandoned run recovery | Missing product capability | small backend (run record) + handheld list of "runs in progress" |
| `dueBack` and `container` asks not rendered on the handheld | Missing UX | small (handheld) |
| Rejected/excluded tags not listed by name on subsequent steps | Missing UX | small (handheld) |
| Workflow tile shown when no workflow exists | Recommended improvement | small (handheld) |
| Web Runs tab shows raw run ids and step keys | Recommended improvement | small (web) |
| Web editor gives no preview of what the operator will see | Optional future enhancement | medium |

## 2. Admin wizards as built

| Wizard | Exists? | Notes |
|---|---|---|
| GS1 Encoding (Scheme & prefix → Serials → Target → Preview & commit) | **Yes** (`Encoding.tsx`) | The only true wizard: 4 steps, back/next, preview with duplicate detection, commit disabled on duplicates, success panel with next actions. A good pattern to reuse |
| Solution template install | Partial | One click *Install*; result summary; no guidance on what to do next (locations, devices, users) |
| Tenant / first deployment | **No** | See [SETUP-FLOWS.md](SETUP-FLOWS.md) |
| Reader / gateway onboarding | **No** | A JSON config field; see [flows/READER-ONBOARDING.md](flows/READER-ONBOARDING.md) |
| Handheld provisioning | **No** | Issue token on web → type it on the handheld login. Works, but the token is 48 hex characters typed by hand (**Recommended improvement**: QR code in the token modal + camera scan on the login screen; the handheld already has a barcode scanner screen) |
| Stocktake setup | Partial | Modal with location/type/name; fine |
| Integration endpoint | Partial | One modal; a *Deliver now* test exists |
| Notification channel | Partial | One modal; *Send test* exists |

## 3. Which setup flows should become wizards

A wizard is justified when steps have prerequisites and a *verifiable* end state. Ranked by value for a
pilot:

| Flow | Why a wizard | Steps | Verification at the end | Priority |
|---|---|---|---|---|
| **Reader onboarding** | The Config JSON is the biggest usability cliff, and the end state ("reads arrive from antenna 2 at Dock 1") is testable | kind → connection (LLRP host / edge gateway / vendor push / MQTT topic) → antennas → location per antenna + direction → "wait for first read" | show first read with EPC, antenna, RSSI; health Online | P1 (Recommended improvement; no backend) |
| **Handheld provisioning** | Token typed by hand on a device; end state is "handheld heartbeat received" | create Handheld device → issue token → show QR → wait for `POST /api/auth/device` | LastSeen updates | P2 |
| **First deployment** | Nine dependent steps across nine pages; nothing tells a new admin the order | template → site → locations → parties → devices → users → verify | a checklist page showing each prerequisite as done/missing | P1 for pilot *onboarding*, not for pilot *operation*; a documented checklist (SETUP-FLOWS §2) is the minimum |
| Notification/escalation setup | Channel → test → policy → attach to rule | small | Send test | P3 |
| Item type authoring | Lifecycle table editor | medium | preview state machine | P3 (templates cover the pilot) |

None of these need backend changes; the checklist wizard needs only the existing list endpoints.

## 4. Shared rules for both kinds of wizard (recommended)

1. Every step names its prerequisite and links to where it is satisfied.
2. Every wizard ends with a *verification*, not a *Save*: a read arrived, a heartbeat arrived, the
   handheld signed in.
3. Nothing irreversible happens before the last step (the Encoding wizard follows this; template
   install does not, and has no confirmation).
4. On the handheld, a workflow step is always submit-then-advance; the operator never has to choose
   between "save" and "next".

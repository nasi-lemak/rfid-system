# UX consistency, accessibility and field usability review

Facts from `src/web/src/index.css`, `src/web/src/components/ui.tsx`, `src/mobile/src/ui/index.tsx` and
the pages. Recommendations are labelled; none require backend work.

## 1. Consistency

### 1.1 Terminology

| Concept | Web | Handheld | API | Recommendation |
|---|---|---|---|---|
| Moving an item | *Transfer* (operation), *Move* (effect), *Moved* (event), "Move picked lots…" (Stock) | *Transfer* | `Transfer`, `Move`, `Moved`, `MoveQuantity` | Use *Transfer* for the operation everywhere; "Move quantity" is fine as the name of that operation |
| Lifecycle advance | *Process stage* (name), `ProcessStage` (code shown in the Item detail quick buttons as `ProcessStage`) | *Process stage* | `ProcessStage` | Show names, never codes, in buttons (Item detail shows raw codes) |
| Hand over custody | *Issue* | *Issue* | `Issue` | consistent |
| Tag binding | *Commission* (handheld, Tags page), *Bind* (Item detail), *+ New item* (Items), "Create items and tags" (Encoding) | *Commission* / *Bind* button | `Commission` | *Commission* for new items, *Bind* for adding a tag to an existing item; rename the Encoding mode accordingly |
| Reader offline | Health `Offline`/`Degraded` | reader driver `disconnected`/`error` (the handheld's own reader) | `DeviceHealth` | Different things; keep, but label the handheld one "Reader (local)" |
| Stocktake end | *Reconcile* → *Apply result* (web) | *Finish & reconcile* (handheld) | `reconcile`, `apply` | Rename handheld button *Finish count* and say "a supervisor applies the result on the web" (it does, in the message) |
| Not-found-in-count | *Not yet seen* (open) → *Missing* (reconciled) | *missing* | `Pending`/`Missing` | fine |
| Item gone | *Disposed* (status) vs *Retired* (lifecycle state in templates) vs `Retired` (unused status enum) | — | | Do not show `ItemStatus.Retired` in filters |
| Party | *Party* (web), *Party* (handheld picker) | | `Party` | Templates call them customers/wards/employees; the operation form could use the definition's label (`Ask` names are fixed) — Optional |

### 1.2 Status colours

| Word | Web (`toneForStatus`) | Web (other places) | Handheld | Decision (recommended) |
|---|---|---|---|---|
| Rejected | warn | — | **crit** (`resultTone`) | **warn**: a rejected line is a business "no", not a fault |
| Missing | warn | stocktake stat **crit**, Items status badge warn | crit | **crit** on stocktake results (it is the exception the count exists to find), warn on item status |
| Unknown | info | — | info | info |
| Unexpected | warn | — | warn | warn |
| Open (alert/stocktake) | info | | | info |
| Offline | crit | | | crit |
| Degraded | warn | | | warn |

### 1.3 Buttons and actions

| Pattern | Web | Handheld | Recommendation |
|---|---|---|---|
| Primary action | `.primary` solid blue; one per panel mostly | `tone="primary"` | consistent |
| Destructive | `.danger` = red **text and border only**, no fill; `.sm` for most | `tone="danger"` solid red | Web destructive actions are visually lighter than primary; acceptable, but *Dispose* on Item detail was an unstyled quick button (fixed: confirmation) |
| Confirmation | `confirm()` on 18 deletes; absent on Stocktake Cancel/Apply, Dispose, Import, Cluster release, Rotate token, Alerts close, Anomaly confirm/dismiss | none anywhere before the P1 fix | Rule: **irreversible → confirm** (delete, dispose, apply, import, discard queue, rotate token, sign out with pending queue). Reversible → no confirm (ack, close, dismiss) |
| Success feedback | five different patterns (`.success` div, dismissible `.panel`, muted text, `<pre>`, none) | `setMsg` text | One `Notice` component per surface (E-3); default to a transient success line after every Save |
| Label naming | "+ New X" everywhere (good); H1 sometimes differs from nav label (Print queue & label audit, GS1 encoding, Predictive maintenance, Notifications & escalation, ERP import & reconciliation) | tiles = screen titles | Make H1 = nav label; put the long form in the subtitle |
| Modals | Escape and backdrop close; no focus trap | full-screen pickers | see §2 |

### 1.4 Language

| Fact | Impact |
|---|---|
| Six languages offered; dictionaries cover only the sidebar, login, Dashboard verbs, two portal strings | A user who picks *Bahasa Melayu* gets a Malay sidebar and an English application |
| `fmt.dt/fmt.d` use the browser locale, ignoring the selected language; `fmt.ago` hard-coded English | dates/relative times never follow the selector |
| Handheld: tiles and Settings translated; screen titles and messages not | same |
| `confirm()` texts are English literals | same |

Recommendation: either remove the language selector for the pilot (honest) or translate the operator
handheld fully first (it has ~200 strings) and keep the web English. Labelled **Recommended improvement**,
P2. Do not ship a selector that changes 5 % of the UI.

## 2. Accessibility (web)

| Check | Fact | Severity | Recommendation |
|---|---|---|---|
| Focus visibility | `:focus` styles only on `input, select, textarea`; none on `button`, `.btn`, `a`, `.chip`, `.tabs button`, `tr.clickable` | high | add `:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px }` to all interactive elements |
| Keyboard reachability | `.chip` are `<span onClick>`; `tr.clickable` are rows with `onClick`; Encoding pool chips, Items filter chips, status chips are not focusable | high | render chips as `<button type="button" class="chip">`; give clickable rows a link in the first cell or `tabIndex=0` + Enter handler |
| ARIA | 0 `aria-*` attributes in `src/web/src`; `Modal` has no `role="dialog"`, `aria-modal`, focus trap or focus restore; sidebar connection dot and `.live-dot` are colour-only with a `title` | high | `role="dialog" aria-modal="true" aria-labelledby`; trap focus; restore focus on close; add text ("Live" / "Offline") beside the dot |
| Colour-only status | badges, stats, health, severity, Stock delta all encode state only by colour | medium | badges already contain the word (ok); stats and dots need a text or icon |
| Contrast | badge text uses the hue on a 15 % tint of the same hue (for example `#d97a06` on a light orange wash) at 12 px 600 – below 4.5:1 in light mode; `--muted #6b7684` on `#f4f6f8` ≈ 4.6:1 at 12 px `.small` – borderline | medium | darken badge ink (`color-mix(in srgb, var(--warn) 70%, black)`) or use solid badges |
| Dark mode | `--primary/--ok/--warn/--crit/--info` not retuned; no toggle | low | retune or drop dark mode |
| Native dialogs | `confirm()`, `alert()`, `prompt()` | low | acceptable for a pilot; replace with the Modal later |
| Forms | labels wrap inputs (good); required marked with `*` in text (good); no `aria-invalid` or inline validation | low | — |
| Language attribute | `document.documentElement.lang` set by the selector (good) | — | — |

## 3. Accessibility and field usability (handheld)

| Check | Fact | Severity | Recommendation |
|---|---|---|---|
| Touch targets | `Button` 14 px vertical padding + 16 px text ≈ 48 px (good); `small` buttons ≈ 33 px; `Chips` 6 px padding + 13 px text ≈ 29 px | high for chips | Chips are the *operation picker* and *state picker*; make them ≥ 44 px tall with 12 px horizontal padding; keep `small` for secondary actions only |
| Glove use | no hardware trigger mapping beyond the reader drivers; tap targets above | medium | the operation flow already needs only Scan/Stop/Submit; make those three full-width |
| One-handed use | Submit at the bottom of a scroll view; pickers are full-screen lists | ok | — |
| Screen readers | 0 `accessibilityLabel`/`accessibilityRole` | medium | add roles to Button/Chips/Badge; low cost |
| Contrast | dark theme; `muted #8e9aa8` on `#0f151c` ≈ 6.5:1 (good); badge tint `color + '33'` with the colour as ink ≈ 4–5:1 | ok | — |
| Sunlight / outdoor | dark theme only; no high-contrast toggle | low | Optional: light theme for outdoor yards |
| Noise | `beep` setting exists (reader driver); no vibration on rejected line | medium | vibrate on `rejected > 0` after submit; small |
| Battery | continuous scanning is explicit Start/Stop; stocktake auto-flush every 1.5 s | ok | — |
| Cold storage / wet | no UI implication | — | — |
| Mistakes | no way to remove one tag from the set; no undo after submit (operations are immutable; a compensating operation is the only route) | medium | per-row remove (small); "Undo last submit" would need a compensating operation — Optional future enhancement |
| Very large counts | `useInventory` dedupes and flushes every 200 ms; list shows 60 rows + "n more"; Submit posts all | ok | show a count prominently (it does: "n tag(s)") |

## 4. Recommended consistency rules (short)

1. One word per concept: Transfer, Issue, Return, Commission, Bind, Count, Reconcile, Apply.
2. Names, never codes, on buttons.
3. Rejected = warn, Missing (in a count) = crit, Unknown = info, Offline = crit, everywhere.
4. Irreversible actions confirm; reversible ones do not.
5. Every screen has a loading, empty and error state; lists have paging or a hard limit stated.
6. Every interactive element is a real button or link, focus-visible, ≥ 44 px on the handheld.
7. H1 equals the navigation label.
8. Do not offer a language the product does not speak.

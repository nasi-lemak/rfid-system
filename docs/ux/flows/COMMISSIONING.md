# Flow — Commissioning (binding tags to items)

Commissioning creates an item and binds a tag to it (or binds a tag to an existing item). Four entry
points end at the same server path (`POST /api/items` or a `Commission` operation).

Step tags: `[USER]` · `[SYS]` · `[HW]` · `[AUTO]`.

## 1. Entry points (facts)

| Entry | Surface | Creates | Binds | Places | Batch |
|---|---|---|---|---|---|
| Handheld **Commission** | H | item (type, identifier, name, required attributes) | EPC read from the nearest tag, a barcode, or a GS1 serial allocated from a pool | at *Settings → Current location* (undefined if not set) | one item per submit; *Print label for last item* |
| Web **Items → + New item** | W | item | optional EPC in the modal | chosen location | one |
| Web **Tags → Commission** | W | item via the same modal with the EPC prefilled (table row or *Unknown EPCs seen by readers*) | yes | chosen | one |
| Web **Encoding wizard** | W | mode *Create items and tags*: n items of a type with GS1 EPCs from a pool; mode *Bind*: EPCs to existing items; mode *Tags*: EPCs only | yes | — | up to 100 000 |
| Web **ERP import** (Admin) | W | items from CSV/JSON with identifiers; EPC column binds | yes | creates locations optionally | bulk |
| Web **Item detail → Bind** | W | — | adds a tag to an existing item | — | one |

## 2. Flow — handheld commissioning (as built)

```
[USER]  Home → Commission
[USER]  choose item type (chips)                             ← required attributes render as fields
[USER]  either  📡 Read nearest tag  → [HW] reader returns the strongest EPC
        or      📷 Barcode instead   → camera; EPC = barcode value; identifier defaults to it
        or      pick a GS1 serial pool chip → [SYS] POST /api/encoding/pools/{id}/allocate → EPC reserved
                                               ("Serial 1042 reserved from pool – write it to the tag")
                                               ← writing the EPC to the tag is the operator's job with the
                                                 reader's encode function; not part of the screen (Missing UX)
[USER]  identifier (defaults to EPC), name, attributes → Bind
[SYS]   runOrQueue({ type: Commission, toLocationId: currentLocation, lines: [{ epc, newItem }] })
[SYS]   OperationProcessor: EPC must be unbound ("EPC … already bound to X"), identifier unique
        ("Identifier … already exists"), initial lifecycle state applied, item placed
[USER]  message "✓ Commissioned <name>" · optional 🖨 Print label for last item
[SYS]   offline: "Offline – queued for sync"; the item exists only after sync (Lookup will say Unknown until then)
```

## 3. Flow — bulk commissioning with GS1 encoding (as built)

```
[USER]  Encoding → Scheme & prefix (SGTIN-96 / SSCC / GRAI / GIAI; company prefix; pool)
[USER]  Serials: count, start (pool's next)
[USER]  Target: mode tags / bind to filtered items / create n items of type
[SYS]   POST /api/encoding/preview (debounced) → EPC list, duplicates check
[USER]  Preview & commit → Commit n EPCs (disabled when duplicates > 0)
[SYS]   POST /api/encoding/commit → batch record; tags (and items) created; pool advanced (never reused)
[USER]  "✓ Batch committed" → Encode another / View batches
[USER]  Print queue / Label designer: labels per item type → printer device (host/port) → [HW] ZPL over TCP 9100
[USER]  Operators stick labels and, if the label carries the EPC pre-encoded by the printer, the tag is live
```

## 4. Exceptions

| Situation | Behaviour | Label |
|---|---|---|
| EPC already bound | 409/400 "EPC … already bound to <item>"; handheld shows the message | works |
| Identifier exists | "Identifier … already exists" | works |
| Required attribute empty | handheld: Bind disabled until filled? — the screen validates `type` and `epc`; required attributes are **not** enforced client-side; server accepts missing attributes (schema is advisory) | **Recommended improvement**: enforce `required` on both surfaces (small) |
| No current location set | item created with no location; invisible to site-restricted users until moved | Recommended improvement: require location before Commission (small, handheld) |
| Offline | queued; Lookup shows Unknown until sync | works; message says so |
| Pool exhausted | "Pool 'X' has only n serials left" | works |
| Allocated serial never written to a tag | serial consumed forever (never reused by design) | acceptable; document for operators |
| Unknown EPC keeps appearing at readers | Tags → *Unknown EPCs seen by readers* → Commission with EPC prefilled | works (web only; handheld Lookup of an unknown tag offers *Commission* with the EPC – yes, `Commission: { epc }` route param) |
| Re-commission a disposed tag | Dispose retires tags; a new Commission with the same EPC succeeds only if the tag row is Retired and reusable — verify during pilot | verify |

## 5. Recommendation

Keep one modal (`NewItemModal`) on the web and one screen on the handheld; make *Tags → Commission* and
*Items → + New item* visibly the same action; put the Encoding wizard behind a capability gate (pool
exists). Enforce required attributes. No backend work.

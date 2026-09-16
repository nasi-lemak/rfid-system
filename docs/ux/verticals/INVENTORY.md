# Vertical journey — Warehouse and inventory (serialized and quantity)

Two templates cover this space and are usually installed together:

- `warehouse` — **serialized**: PALLET (Received → PutAway → Staged → Dispatched, container), CARTON
  (container), TRAILER. Rule *Dispatched without staging* (Warning).
- `inventory` — **quantity**: STOCK-LOT (`sku` required, expiry, reorder point 10), SPARE (`partNumber`
  required, reorder point 5). Rules *Below reorder point*, *Expiring within 30 days*, *Expired stock seen*.

Markers: **[H]** handheld · **[F]** fixed reader · **[A]** automatic · **[W]** web.

## 1. Serialized flow — pallets and cartons

```
[USER][H]  Receive at dock: scan pallet + cartons → dest "Dock 1" → Submit          Activate + Move; PALLET *→Received
[USER][H]  Pack: container = pallet tag → scan cartons → Submit                     cartons inside pallet
[USER][H]  Transfer (putaway): scan pallet → dest "Rack A-03" → Submit              pallet + children Moved; PALLET Received→PutAway
[USER][H]  Process stage: scan pallet → target "Staged" → dest "Staging"            PutAway→Staged
[USER][H]  Dispatch: scan pallet → dest "Customer X" (Location kind Customer) → Submit   Staged→Dispatched
[HW][F]    Dock door antenna (Out) reads the pallet                                  Moved (Out); rule: state is Dispatched → no alert
```

Exception: a pallet leaves through the dock door while **PutAway/Received** → rule *Dispatched without
staging* → Warning alert. Unpack at the customer is optional.

## 2. Quantity flow — lots and spares

Quantity items are one **row per lot per location**; operations carry a `quantity` on the line.

```
[USER][H]  Commission: tag on the lot → type STOCK-LOT → sku, lot, expiry → Bind      quantity 0 at current location
[USER][H]  Receive: scan lot → dest "Bin 12" → quantity 240 → Submit                  Activate + Move; quantity is set by Adjust? —
           NOTE: Receive has no AdjustQuantity effect; quantity arrives via **Adjust** (+240) or via ERP import.
[USER][H]  Adjust: scan lot → quantity +240 → Submit                                   QuantityChanged
[USER][H]  Count: scan lot → quantity 238 → Submit                                     Counted with countedQuantity (RecordSeen)
[USER][W]  Stock → Allocate: type STOCK-LOT, quantity 100 → FEFO pick list (lots by expiry)
[USER][W]  Move picked lots… → destination "Pick face" → Submit                        MoveQuantity: source row −, destination row + (created or topped up)
[USER][H]  Move quantity: scan lot → dest → quantity → Submit                           same effect from the floor
[AUTO][A]  Rule "Below reorder point" on QuantityChanged → Warning; "Expiring within 30 days" on Counted; "Expired stock seen" on Seen → Critical
```

## 3. Serialized vs quantity on each surface

| Aspect | Serialized (PALLET/CARTON) | Quantity (STOCK-LOT/SPARE) |
|---|---|---|
| Identity | one tag = one item | one tag = one lot row at one location |
| Handheld line | EPC → item | EPC → lot; a **quantity field** is required for Adjust/Move quantity, optional for Count |
| Stocktake | Found/Missing/Unexpected per tag | tag Found; **counted quantity is not part of a stocktake** (Count operation records it) |
| Web view | Items, Item detail | Stock (Summary, Balances, Movements, Allocate) + Items |
| Containers | Pack/Unpack, cascade moves | not containers |
| Rules | direction/state rules at doors | thresholds on quantity/expiry |
| Automation | dock door reads | none physical; rules only |

## 4. Exception paths

| Situation | What happens | Who sees what | Label |
|---|---|---|---|
| **Move more than available** | line Rejected "Only 40 ea of Lot 7 available at the source" | operator | works |
| **Move to the same location** | Rejected "Lot 7 is already at Bin 12" | operator | works |
| **Negative adjust below zero** | Rejected "Quantity cannot go negative" | operator | works |
| **Pick list on the web, picking on the floor** | the picker cannot see the allocation on the handheld; they must Move quantity lot by lot from memory or paper | — | **Missing product capability**: allocation → handheld task; P2 for warehouses, not needed for a first pilot |
| **Receive of a quantity lot** | Receive activates and moves; the quantity must be Adjusted separately (two operations) | operator confusion | **Recommended improvement**: template-defined `ReceiveStock` operation = Activate + Move + AdjustQuantity (content only, no code) |
| **Expired lot scanned anywhere** | Critical alert | web | works |
| **Pallet moved without its cartons** (cartons never Packed) | cartons stay at the old location | Presence/Items wrong | process issue; *Pack* must be part of receiving — the guided workflow feature can enforce it (Receive → Pack), Recommended improvement (content) |
| **Container nesting too deep** | 400 "Container nesting deeper than N levels – refusing to move" | operator | works |
| **Dock door double reads (in and out antennas)** | direction In/Out per antenna; 30 s debounce only for `None` | fine | works |
| **Stocktake of a rack with 500 tags** | handheld auto-flushes scans every 1.5 s; not offline-capable | operator | P2 (stocktake offline) |

## 5. Gaps specific to this vertical

| Gap | Label | Priority |
|---|---|---|
| Allocation/pick list not visible on the handheld | Missing product capability | P2 |
| Receive of quantity needs two operations | Recommended improvement (template content) | P2 |
| Counted quantity not reflected in stocktake results | Missing product capability | P3 |
| Stock page shown to tenants without quantity types | Recommended improvement (IA-1) | P2 |

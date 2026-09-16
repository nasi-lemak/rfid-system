# Solution Templates — mapping every vertical onto the platform

Legend for the **Ops** column: Rcv = Receive, Xfr = Transfer, Iss = Issue,
Ret = Return, Cnt = Count/Stocktake, Dsp = Dispatch, Insp = Inspect,
Mnt = Maintain, Dis = Dispose, Pck/Unp = Pack/Unpack (containers),
Stg = ProcessStage (lifecycle step), Com = Commission, Adj = Adjust.
**Portal** = fixed reader / gate / cabinet ingestion with zone in/out.

| System | Item types (flags) | Lifecycle (states) | Ops | Rules / special |
|---|---|---|---|---|
| Asset Management | Asset (serialized) | Active→InUse→InStorage→Disposed | Rcv Xfr Iss Ret Cnt Dis | Missing after stocktake → alert |
| Inventory Management | SKU (quantity, lot) | – | Rcv Cnt Xfr Iss Adj | Qty < reorder point → alert (replenish) |
| Warehouse Management | Pallet (container), Carton (container), SKU | Received→PutAway→Picked→Packed→Dispatched | Rcv Xfr(putaway) Pck Unp Dsp Cnt | Dock-door portals verify dispatch |
| Fixed Asset Tracking | FixedAsset (cost, purchasedAt) | Active→Disposed | Iss Xfr Cnt Dis | Export events to finance for depreciation |
| Tool Tracking | Tool | InCrib→CheckedOut→InCrib | Iss Ret Cnt | Checked-out > N hours → overdue alert; missing at close |
| Equipment Management | Equipment (inspection) | Available→Assigned→Maintenance | Iss Ret Mnt Insp | Inspection due → alert |
| IT Asset Management | ITAsset (attributes: hostname, OS) | Stock→Assigned→Repair→Retired | Iss Ret Mnt Cnt Dis | User assignment = custody |
| Linen Management | Linen (tracksCycles, maxCycles) | Clean→Issued→Soiled→InWash→Clean / Retired | Iss Ret Stg Cnt Dis | cycleCount ≥ max → retire alert; loss after N days unseen |
| Laundry Management | Textile batch (container), Linen | Intake→Washing→Drying→Sorted→Returned | Rcv Stg Pck Dsp | Customer = Party; batch = container item |
| Uniform Management | Uniform (attributes size, employee) | Stock→Issued→Laundry→Issued / Retired | Iss Ret Stg Dis | Replacement when cycles exhausted |
| PPE Management | PPE (expiry, inspection) | InService→Inspection→Failed/Retired | Iss Insp Ret Dis | Expiry/inspection due alerts |
| Medical Asset Tracking | MedicalDevice | Clean→InUse→Dirty→Cleaning→Clean | Xfr Stg Cnt Portal | Availability by location; cleaning status is state |
| Hospital Linen | Linen (cycles) | Clean→Issued→Contaminated→InWash→Clean | Iss Ret Stg | Contaminated linen in clean zone → alert |
| Medical Consumables | Consumable (quantity, expiry, lot) | – | Rcv Cnt Iss Adj | Expiry < 30d; below par level |
| Surgical Instrument Tracking | Tray (container), Instrument | Dirty→Decontam→Sterilised→InUse→Dirty | Stg Pck Unp Cnt | Tray completeness = container contents vs expected kit (Count on container) |
| Laboratory Sample Tracking | Sample (attributes) | Collected→Received→Processing→Stored→Disposed | Rcv Xfr Stg Dis | Every move is chain of custody event |
| Pharmaceutical Inventory | Medicine (quantity, lot, expiry) | – | Rcv Cnt Xfr Iss Adj | Expiry, batch recall (query by lot) |
| Document/File Tracking | File | Archived→CheckedOut→Archived | Iss Ret Cnt | Overdue; portal exit without checkout |
| Library Management | Book/Media | OnShelf→OnLoan→OnShelf | Iss Ret Cnt Portal | Exit gate while OnShelf → anti-theft alert |
| Evidence Management | Evidence (strict custody) | Collected→Stored→CheckedOut→Stored→Released/Destroyed | Rcv Iss Ret Xfr Dis | Every custody change immutable event; zone exit w/o checkout → critical |
| Museum Collection | Artefact (attributes) | InStorage→OnDisplay→OnLoan→InStorage | Xfr Cnt Dsp | Exhibition = location; loan = custody |
| Artwork Management | Artwork, Crate (container) | InStorage→Packed→InTransit→Installed | Pck Unp Dsp Xfr | Crate contents tracked via container |
| Jewellery / High-Value | Jewellery | InSafe→OnDisplay→Sold | Cnt Xfr Portal | Fast stocktake; missing-item alert |
| Retail Inventory | Merchandise (attributes: SKU, size, colour) | BackRoom→SalesFloor→Sold | Rcv Xfr Cnt | Replenishment by SKU counts on floor |
| Fashion/Apparel | Garment (size/colour) | same as retail | Rcv Xfr Cnt | Size/colour availability dashboard |
| Smart Fitting Room | Garment | + InFittingRoom | Portal | Fitting-room antenna → Seen events by zone |
| Retail Loss Prevention | Merchandise | + Sold | Portal | Exit antenna and state ≠ Sold → alert |
| Returnable Asset Mgmt | Crate/Tote/Pallet/Cylinder | InPool→AtCustomer→Returned | Dsp Ret Cnt | Custody = customer party; overdue return |
| Pallet Tracking | Pallet | InPool→AtCustomer | Dsp Ret Cnt | Days at customer > N → alert |
| Container Tracking | Container (container) | – | Pck Unp Xfr | Contents via ParentItemId |
| Reusable Packaging | Box/Tote/Crate | InPool→InUse→Damaged | Cnt Insp Ret | Damage → state |
| Manufacturing WIP | Part/Assembly | Stage1→Stage2→…→Complete | Stg Xfr Pck | Workstation = location; routing = lifecycle |
| Production Tracking | Product (attributes: workOrder) | per-route states | Stg Cnt | Work order = Reference on operations |
| Kanban / Replenishment | Bin (container), Part (quantity) | Full→Empty | Portal Stg | Bin seen at "Empty" zone → replenish alert |
| Raw Material Tracking | Roll/Material (quantity) | Received→InStore→Consumed | Rcv Xfr Adj | Consumption = Adjust |
| Finished Goods | Product | Complete→InWarehouse→Shipped | Xfr Dsp Cnt | Dock portal verifies shipment |
| Quality Control | Item under inspection | Pending→Passed / Quarantined→Released | Insp Stg | Quarantine zone; leaving quarantine while Quarantined → alert |
| Maintenance Management | Equipment (inspection interval) | InService→UnderMaintenance→InService | Insp Mnt | Due dates → alerts |
| MRO Inventory | SparePart (quantity) | – | Rcv Iss Cnt Adj | Min/max replenishment |
| Automotive Parts | Component | per-route | Stg Xfr Dsp | Traceability via events |
| Tyre Management | Tyre (cycles = fitments) | New→Fitted→Removed→Retread→Scrapped | Stg Insp Dis | Lifecycle & maintenance |
| Vehicle Yard Mgmt | Vehicle (is item; also mobile location) | Arrived→Parked→Dispatched | Portal Xfr Dsp | Gate antennas In/Out |
| Fleet Equipment | Equipment | Assigned→Inspected | Iss Insp Xfr | Vehicle = mobile location |
| Aviation Tool Control | Tool (FOD) | InCrib→CheckedOut→InCrib | Iss Ret Cnt Portal | Tool leaves hangar zone while CheckedOut → critical (FOD) |
| Aircraft Parts | Rotable (attributes: hours) | Serviceable→Installed→Unserviceable→Repair | Stg Iss Ret Mnt | Custody & maintenance lifecycle |
| Marine Equipment | Equipment | Ashore→OnVessel | Xfr Cnt | Vessel = location |
| Offshore Asset Mgmt | Tool/Container | Onshore→Offshore | Xfr Insp Pck | Inspection certificates as attributes |
| Oil & Gas Equipment | Pipe/Valve | InYard→Mobilised→Demobilised | Cnt Dsp Ret Mnt | Yard inventory by zone |
| Construction Assets | Tool/Equipment/Material | InYard→OnSite | Xfr Iss Cnt | Site = location; loss alert |
| Rental Equipment | RentalAsset | Available→Rented→Returned→Maintenance | Dsp Ret Mnt | Overdue (due date attribute) → alert |
| Event Equipment | AVEquipment, Case (container) | InStore→Packed→AtVenue→Returned | Pck Dsp Ret Unp | Case completeness |
| Sports Equipment | Equipment/Uniform | InStore→Issued | Iss Ret Cnt | – |
| School Asset Mgmt | Device/Book | InStock→Assigned | Iss Ret Cnt | Student/staff = Party |
| Hotel Asset Mgmt | Appliance/Furniture | InRoom→Maintenance | Xfr Mnt Cnt | Room = location |
| Hotel Linen | Linen (cycles) | Clean→InRoom→Soiled→InWash | Iss Ret Stg Cnt | Shrinkage report (unseen N days) |
| Hotel Minibar | Consumable (quantity) | – | Cnt Adj | Consumption = Adjust; replenish |
| Casino Asset Tracking | Chip/Equipment (HF tags) | – | Cnt Xfr Portal | Technology = HfNfc |
| Food Tray Management | Tray | Kitchen→Ward→Return | Stg Portal | Circulation counts |
| Food Production | Batch container | per-stage | Stg | Traceability |
| Cold-Chain Container | Container (attributes: temp) | InTransit | Xfr Portal | Sensor readings in event Data |
| Keg Management | Keg | Empty→Filled→AtDistributor→AtCustomer→Returned | Stg Dsp Ret | Custody = party; days out |
| Gas Cylinder Mgmt | Cylinder (inspection) | Empty→Filled→AtCustomer→Returned | Stg Dsp Ret Insp | Inspection due |
| Waste Bin Mgmt | Bin (owner attr) | – | Portal Xfr | Collection event = Seen at truck antenna |
| Waste Tracking | WasteContainer | Collected→Treated→Disposed | Stg Dis | Chain of custody |
| Postal/Parcel Sorting | Bag/Cage/Parcel | – | Portal Xfr | Routing by zone reads |
| Baggage Tracking | Bag | CheckedIn→Loaded→Transferred→Arrived | Stg Portal | Portal per stage |
| Cross-Dock | Pallet/Carton | Arrived→Staged→Dispatched | Rcv Xfr Dsp Portal | – |
| Shipment Verification | Goods | Picked→Verified→Shipped | Cnt(on container) Dsp | Dispatch vs expected list → Unexpected/Missing |
| Loading Dock Mgmt | Pallet/Container | – | Portal Dsp | Door antennas Out direction |
| Yard Management | Trailer/Container | InYard→Departed | Portal Xfr | Yard position = zone |
| Cold Storage Inventory | Stock (quantity, expiry) | – | Cnt Portal | Shelf readers |
| Data Centre Assets | Server/Switch (attributes: rack U) | Installed→Spare→Decommissioned | Cnt Xfr Dis | Rack = location |
| Cable/Reel Mgmt | Reel (quantity = length) | – | Rcv Adj Xfr | Length consumption = Adjust |
| Parts Kitting | Kit (container), Part | Kitting→Complete | Pck Cnt | Count on container vs expected BOM |
| Order Fulfilment Verification | Order (container), Goods | Picked→Verified | Cnt Dsp | Expected list from order |
| Smart Cabinet | Supplies/Tools/Drugs | InCabinet→Removed | Portal (cabinet device) | Removal/return detection via presence diff |
| Smart Shelf | Items | OnShelf→Removed | Portal (shelf device) | Presence → replenishment |
| Smart Locker | Equipment | InLocker→Issued | Portal + Iss/Ret | User-controlled issue via Party |
| Room/Zone Presence | Any | – | Portal | Seen/Moved events with zone in/out |
| Personnel Tracking | Person badge (Party linked item, Active/BLE) | – | Portal | Technology Active/Ble; zone presence |
| Visitor Management | Visitor badge | Issued→Returned | Iss Ret Portal | Authorised zones rule |
| Emergency Muster | Person badge | – | Portal + Cnt at muster point | Count at muster location vs expected on site |
| Access Control | Employee/Vehicle badge | – | Portal | Gate antenna + allowed-zones rule |
| Race Timing | Bib | – | Portal (checkpoints) | Seen events with timestamps → laps |
| Livestock Management | Animal (LF/UHF ear tag, attributes) | Alive→Sold/Deceased | Xfr Insp Stg | Health records in events |
| Agricultural Assets | Equipment/Crate/Bin | – | Cnt Xfr | – |
| Tree/Plant Tracking | Plant (attributes: species) | Seedling→Growing→Sold | Stg Cnt Xfr | Nursery bed = location |
| Research Animal Mgmt | Animal, Cage (container) | per-protocol | Pck Unp Stg | Experiment records in Data |

## Built-in templates (23) and the systems they cover

| Template code | Covers |
|---|---|
| `asset-management` | Asset Management, Fixed Asset Tracking, IT Asset Management, Equipment Management, Smart Cabinet |
| `inventory` | Inventory Management, Medical Consumables, Pharmaceutical Inventory, MRO Inventory, Cold Storage Inventory, Smart Shelf |
| `warehouse` | Warehouse Management, Logistics Cross-Dock, Shipment Verification, Loading Dock, Yard Management, Finished Goods, Pallet Tracking |
| `tool-tracking` | Tool Tracking, Aviation Tool Control (FOD), Parts Kitting |
| `linen-laundry` | Linen Management, Laundry Management, Uniform Management, Hospital Linen |
| `ppe-inspection` | PPE Management, Gas Cylinder Management, inspection-driven equipment |
| `medical-assets` | Medical Asset Tracking, Surgical Instrument Tracking |
| `chain-of-custody` | Evidence Management, Laboratory Sample Tracking, Document/File Tracking |
| `library` | Library Management |
| `retail` | Retail Inventory, Fashion/Apparel, Smart Fitting Room, Retail Loss Prevention, Jewellery / High-Value |
| `returnable-assets` | Returnable Asset Management, Pallet Tracking, Reusable Packaging, Keg Management, Rental Equipment |
| `manufacturing` | Manufacturing WIP, Production Tracking, Kanban / Replenishment, Raw Material, Quality Control, Parts Kitting, Automotive Parts |
| `people-presence` | Personnel Tracking, Visitor Management, Emergency Muster, Access Control, Race Timing, Room/Zone Presence |
| `livestock-agri` | Livestock Management, Agricultural Asset Tracking, Tree/Plant Tracking, Research Animal Management |
| `museum-artwork` | Museum Collection, Artwork Management |
| `hospitality` | Hotel Asset Management, Hotel Minibar Inventory (+ `linen-laundry` for Hotel Linen) |
| `food-coldchain` | Food Tray Management, Food Production Tracking, Cold-Chain Container Tracking |
| `fleet-aviation` | Vehicle Yard Management, Fleet Equipment, Tyre Management, Aircraft Parts Tracking |
| `waste-management` | Waste Bin Management, Waste Tracking |
| `postal-baggage` | Postal/Parcel Sorting, Baggage Tracking, Container Tracking (ULDs, cages) |
| `datacentre-cables` | Data Centre Asset Tracking, Cable/Reel Management |
| `field-industrial` | Construction Asset Tracking, Oil & Gas Equipment, Offshore Asset Management, Marine Equipment, Maintenance Management |
| `education-events-sports` | School Asset Management, Event Equipment Management, Sports Equipment Management, Order Fulfilment Verification (case completeness) |

Casino chips (HF tags) use `inventory`/`retail` with `Technology = HfNfc`; Smart Locker is `asset-management` with a `Locker` location and a cabinet reader.

## Demo scenarios (seed data)

Every template ships with a **demo scenario** (`src/backend/Rfid.Infrastructure/Persistence/Demo/DemoScenarios.cs`):
a realistic site with a location hierarchy, parties, tagged items (real GS1 SGTIN-96 EPCs, one
company prefix per scenario), fixed readers/portals/cabinets with antenna→zone mapping, and a few days
of history. History is **replayed through the real `OperationProcessor` and `ReadIngestionService`**,
so lifecycle transitions, chain-of-custody events, rule evaluation and alerts are produced exactly as
they would be in production — e.g. a checked-out torque wrench read by the hangar exit gate raises the
*FOD* critical alert; an `OnShelf` book at the library gate raises *anti-theft*; a `Quarantined`
gearbox seen at an assembly station raises *left quarantine*.

| Scenario site | Template |
|---|---|
| Northgate Head Office | asset-management |
| City Hospital Pharmacy & Stores | inventory |
| Midlands Distribution Centre | warehouse |
| Hangar 3 – Line Maintenance | tool-tracking |
| St Mary's Hospital Linen Service | linen-laundry |
| Industrial Gases & Safety Depot | ppe-inspection |
| General Hospital – Equipment Library & CSSD | medical-assets |
| County Police Evidence Store & Forensic Lab | chain-of-custody |
| City Central Library | library |
| Flagship Store – High Street | retail |
| Ridgeway Brewery & Plant Hire | returnable-assets |
| Plant 2 – Gearbox Line | manufacturing |
| Riverside Campus & Riverside 10K | people-presence |
| Hillside Farm, Nursery & Research Unit | livestock-agri |
| National Museum of Design | museum-artwork |
| Grand Hotel | hospitality |
| Central Production Kitchen & Cold Logistics | food-coldchain |
| Northern Depot & Part-145 MRO | fleet-aviation |
| Metro Waste Services Depot | waste-management |
| Airport T2 Baggage Hall & Mail Hub | postal-baggage |
| DC-East | datacentre-cables |
| North Sea Supply Base | field-industrial |
| Riverside Academy & Events | education-events-sports |

How to load them:
- **At startup** – `Seed:Scenarios` in `appsettings.json` (or env `Seed__Scenarios`): `all` (default),
  `none`, or a comma list such as `linen-laundry,medical-assets`. Runs once, when the demo tenant is created.
- **Per tenant, on demand** – `POST /api/templates/{code}/demo` (Admin) or the **Load demo data** button
  on the *Solution templates* page. Idempotent: a scenario whose site already exists is skipped.
- Demo device tokens: `demo-handheld-token` (Northgate handheld), `demo-portal-token` (MDC dock door 1),
  `demo-gate-token` (Northgate entrance gate).

Writing a new scenario is declarative — see the `Scenario` record in `DemoModel.cs`. The unit test
`DemoSeedTests` replays every scenario and fails on any rejected history step, so lifecycle mistakes in
seed data are caught at build time.


## Template-defined operations

Since v2.0 a template can define **operations**, not only list the built-ins it uses. An
operation definition is a named composition of built-in effects (`Move`, `SetCustodian`,
`SetDueBack`, `SetState`, `IncrementCycle`, `RecordInspection`, `Pack`, `AdjustQuantity`,
`SetAttribute`, …) with requirements, an event type and the item types it applies to. Lifecycle
transitions are keyed by the operation code, so the state machine and the operation share one
vocabulary:

| Template | Operation | Composition | Lifecycle |
|---|---|---|---|
| `medical-assets` | `Decontaminate` | `SetState`, `Move{optional}` | `Dirty → Decontaminated` |
| `medical-assets` | `Sterilise` | `SetState`, `Move{optional}`, `SetAttribute{lastSterilisedAt: {now}}` | `Decontaminated → Sterilised` (+1 cycle) |
| `tool-tracking` | `Calibrate` | `RecordInspection`, `SetAttribute{calibrated: true}`, `SetAttribute{calibratedAt: {now}}` | — |

Installing a template installs its operations for the tenant (idempotently; existing tenants are
synced on startup). They appear in the web and handheld operation pickers with fields derived
from their definition, and history records the operation code (`definitionCode`). Tenants can add
their own through `POST /api/operations/definitions`. The effect vocabulary is closed: a vertical
that needs a new effect is a platform change (one effect, one test), never a script.

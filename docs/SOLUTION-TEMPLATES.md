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

Templates provisioned in v1 (`SolutionTemplates` seed): Asset Management,
Inventory, Warehouse, Tool Tracking, Linen/Laundry, PPE, Medical Assets,
Surgical Instruments, Evidence, Library, Retail (incl. loss prevention),
Returnable Assets, Manufacturing WIP, Rental, Keg/Cylinder, Personnel/Muster.
Others are configuration-only variants of these and can be added as JSON.

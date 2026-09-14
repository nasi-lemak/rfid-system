using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Domain.Lifecycle;

namespace Rfid.Application.Templates;

/// <summary>
/// Built-in solution templates. Each one is pure configuration over the same primitives, which is how a
/// single platform covers asset, inventory, linen, medical, evidence, retail, manufacturing, … verticals.
/// </summary>
public static class SolutionTemplateCatalog
{
    private static LifecycleDefinition Lc(string initial, string[] states, params (string from, string to, string on, bool cycle)[] tr) => new()
    {
        Initial = initial, States = states.ToList(),
        Transitions = tr.Select(t => new LifecycleTransition { From = t.from, To = t.to, On = t.on, IncrementCycle = t.cycle }).ToList(),
    };
    private static (string, string, string, bool) T(string from, string to, OperationType on, bool cycle = false) => (from, to, on.ToString(), cycle);
    private static RuleCondition C(string field, string op, object? value) => new() { Field = field, Op = op, Value = value };
    private static Rule Alert(string name, ItemEventType trigger, Severity sev, string message, params RuleCondition[] conds) => new()
    {
        Name = name, Trigger = trigger, Severity = sev, Action = RuleAction.CreateAlert, Conditions = conds.ToList(), Params = new() { ["message"] = message },
    };
    private static AttributeDefinition A(string name, string type = "string", bool required = false, params string[] options) => new()
    {
        Name = name, Type = type, Required = required, Options = options.Length == 0 ? null : options.ToList(),
    };
    private static readonly OperationType[] AllOps = Enum.GetValues<OperationType>();

    public static readonly List<SolutionTemplate> All = new()
    {
        new SolutionTemplate
        {
            Code = "asset-management", Name = "Asset Management", Vertical = "Assets",
            Description = "Equipment, furniture, IT and fixed assets: location, custody, stocktake, transfer, disposal, depreciation fields.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Asset", Code = "ASSET", AttributeSchema = { A("manufacturer"), A("model"), A("costCentre"), A("warrantyUntil", "date") },
                        Lifecycle = Lc("InStorage", new[] { "InStorage", "InUse", "UnderMaintenance", "Disposed" },
                            T("*", "InUse", OperationType.Issue), T("*", "InStorage", OperationType.Return), T("*", "UnderMaintenance", OperationType.Maintain), T("*", "Disposed", OperationType.Dispose)) },
                    new ItemType { Name = "IT Asset", Code = "IT-ASSET", AttributeSchema = { A("hostname"), A("os"), A("serialNumber"), A("assignedUser") },
                        Lifecycle = Lc("Stock", new[] { "Stock", "Assigned", "Repair", "Retired" },
                            T("*", "Assigned", OperationType.Issue), T("*", "Stock", OperationType.Return), T("*", "Repair", OperationType.Maintain), T("*", "Retired", OperationType.Dispose)) },
                },
                Rules = { Alert("Asset missing after stocktake", ItemEventType.Counted, Severity.Warning, "{item.name} ({item.identifier}) was not found in stocktake", C("data.result", "eq", "Missing")) },
                Operations = { OperationType.Receive, OperationType.Transfer, OperationType.Issue, OperationType.Return, OperationType.Count, OperationType.Maintain, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "inventory", Name = "Inventory & Consumables", Vertical = "Inventory",
            Description = "SKU / lot based stock incl. medical consumables, pharma, MRO spares: receive, count, move, issue, replenish, expiry.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Stock Lot", Code = "STOCK-LOT", Category = ItemCategory.Quantity, Unit = "ea", TracksExpiry = true, ReorderPoint = 10, AttributeSchema = { A("sku", required: true), A("supplier") } },
                    new ItemType { Name = "Spare Part", Code = "SPARE", Category = ItemCategory.Quantity, Unit = "ea", ReorderPoint = 5, AttributeSchema = { A("partNumber", required: true), A("machine") } },
                },
                Rules =
                {
                    Alert("Below reorder point", ItemEventType.QuantityChanged, Severity.Warning, "{item.name} is at {item.quantity} (reorder point {item.reorderPoint})", C("item.quantity", "lt", 10)),
                    Alert("Expiring within 30 days", ItemEventType.Counted, Severity.Warning, "{item.name} lot {item.lotNumber} expires {item.expiryDate}", C("item.daysUntilExpiry", "lt", 30)),
                    Alert("Expired stock seen", ItemEventType.Seen, Severity.Critical, "Expired {item.name} lot {item.lotNumber} detected", C("item.daysUntilExpiry", "lt", 0)),
                },
                Operations = { OperationType.Receive, OperationType.Count, OperationType.Transfer, OperationType.Issue, OperationType.Adjust, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "warehouse", Name = "Warehouse & Logistics", Vertical = "Logistics",
            Description = "Pallets, cartons, cross-dock, shipment verification, dock doors and yard: receive, putaway, pick, pack, dispatch.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Pallet", Code = "PALLET", IsContainer = true, Lifecycle = Lc("Received", new[] { "Received", "PutAway", "Staged", "Dispatched" },
                        T("*", "Received", OperationType.Receive), T("Received", "PutAway", OperationType.Transfer), T("*", "Staged", OperationType.ProcessStage), T("*", "Dispatched", OperationType.Dispatch)) },
                    new ItemType { Name = "Carton", Code = "CARTON", IsContainer = true },
                    new ItemType { Name = "Trailer", Code = "TRAILER", AttributeSchema = { A("carrier"), A("plate") } },
                },
                Rules =
                {
                    Alert("Dispatched without staging", ItemEventType.Moved, Severity.Warning, "{item.name} left via dock door while '{item.state}'", C("data.direction", "eq", "Out"), C("toLocation.kind", "ne", "Dock"), C("item.state", "nin", "Staged,Dispatched"), C("itemType.code", "eq", "PALLET")),
                },
                LocationKinds = { LocationKind.Dock, LocationKind.Yard, LocationKind.Gate, LocationKind.Rack, LocationKind.Bin },
                Operations = { OperationType.Receive, OperationType.Transfer, OperationType.Pack, OperationType.Unpack, OperationType.ProcessStage, OperationType.Dispatch, OperationType.Count, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "tool-tracking", Name = "Tool Crib & Tool Control", Vertical = "Tools",
            Description = "Power/hand/aviation tools: checkout/in, tool-room inventory, overdue and FOD (tool leaving zone while checked out).",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Tool", Code = "TOOL", RequiresInspection = true, InspectionIntervalDays = 180, AttributeSchema = { A("calibrated", "bool"), A("kit") },
                        Lifecycle = Lc("InCrib", new[] { "InCrib", "CheckedOut", "UnderRepair", "Retired" },
                            T("InCrib", "CheckedOut", OperationType.Issue), T("CheckedOut", "InCrib", OperationType.Return), T("*", "UnderRepair", OperationType.Maintain), T("UnderRepair", "InCrib", OperationType.Return), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Tool Kit", Code = "TOOL-KIT", IsContainer = true },
                },
                Rules =
                {
                    Alert("Tool overdue", ItemEventType.Seen, Severity.Warning, "{item.name} is overdue for return", C("item.overdue", "eq", true)),
                    Alert("FOD: tool left hangar while checked out", ItemEventType.Moved, Severity.Critical, "{item.name} passed exit gate while checked out", C("data.direction", "eq", "Out"), C("item.state", "eq", "CheckedOut")),
                    Alert("Tool missing at close", ItemEventType.Counted, Severity.Critical, "{item.name} missing from crib", C("data.result", "eq", "Missing")),
                },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.Count, OperationType.Inspect, OperationType.Maintain, OperationType.Pack, OperationType.Unpack, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "linen-laundry", Name = "Linen, Laundry & Uniforms", Vertical = "Textiles",
            Description = "Hotel/hospital linen, uniforms, textile batches: wash cycles, issue/return, contamination status, shrinkage and retirement.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Linen", Code = "LINEN", TracksCycles = true, MaxCycles = 200, AttributeSchema = { A("size"), A("colour"), A("owner") },
                        Lifecycle = Lc("Clean", new[] { "Clean", "Issued", "Soiled", "Contaminated", "InWash", "Retired" },
                            T("Clean", "Issued", OperationType.Issue), T("Issued", "Soiled", OperationType.Return), T("Soiled", "InWash", OperationType.ProcessStage, true),
                            T("Contaminated", "InWash", OperationType.ProcessStage, true), T("InWash", "Clean", OperationType.ProcessStage), T("Issued", "Contaminated", OperationType.ProcessStage), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Uniform", Code = "UNIFORM", TracksCycles = true, MaxCycles = 150, AttributeSchema = { A("size", required: true), A("employee"), A("garmentType") },
                        Lifecycle = Lc("Stock", new[] { "Stock", "Issued", "InLaundry", "Retired" },
                            T("Stock", "Issued", OperationType.Issue), T("Issued", "InLaundry", OperationType.Return), T("InLaundry", "Stock", OperationType.ProcessStage, true), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Laundry Cage", Code = "LAUNDRY-CAGE", IsContainer = true },
                },
                Rules =
                {
                    Alert("Linen reached max wash cycles", ItemEventType.StateChanged, Severity.Warning, "{item.name} has completed {item.cycleCount} washes – retire", C("item.cyclesRemaining", "lte", 0)),
                    Alert("Contaminated linen in clean zone", ItemEventType.Moved, Severity.Critical, "Contaminated {item.name} detected in {toLocation.name}", C("item.state", "eq", "Contaminated"), C("toLocation.clean", "eq", true)),
                },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.ProcessStage, OperationType.Count, OperationType.Pack, OperationType.Unpack, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "ppe-inspection", Name = "PPE & Inspected Equipment", Vertical = "Safety",
            Description = "Helmets, harnesses, gas cylinders, lifting gear: issue, periodic inspection, expiry, failure and retirement.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "PPE", Code = "PPE", TracksExpiry = true, RequiresInspection = true, InspectionIntervalDays = 180, AttributeSchema = { A("standard"), A("size") },
                        Lifecycle = Lc("InService", new[] { "InService", "Issued", "Failed", "Retired" },
                            T("InService", "Issued", OperationType.Issue), T("Issued", "InService", OperationType.Return), T("*", "InService", OperationType.Inspect), T("*", "Failed", OperationType.Inspect), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Gas Cylinder", Code = "CYLINDER", RequiresInspection = true, InspectionIntervalDays = 1825, AttributeSchema = { A("gas"), A("capacityL", "number") },
                        Lifecycle = Lc("Empty", new[] { "Empty", "Filled", "AtCustomer", "Returned", "Condemned" },
                            T("*", "Filled", OperationType.ProcessStage), T("Filled", "AtCustomer", OperationType.Dispatch), T("AtCustomer", "Empty", OperationType.Return), T("*", "Condemned", OperationType.Dispose)) },
                },
                Rules =
                {
                    Alert("Inspection due", ItemEventType.Seen, Severity.Warning, "{item.name} inspection due", C("item.daysUntilInspection", "lt", 0)),
                    Alert("PPE expired", ItemEventType.CustodyChanged, Severity.Critical, "Expired {item.name} issued", C("item.daysUntilExpiry", "lt", 0)),
                    Alert("Failed PPE issued", ItemEventType.CustodyChanged, Severity.Critical, "{item.name} failed inspection and was issued", C("item.state", "eq", "Failed")),
                },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.Inspect, OperationType.Dispatch, OperationType.ProcessStage, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "medical-assets", Name = "Medical Equipment & Surgical Instruments", Vertical = "Healthcare",
            Description = "Pumps, wheelchairs, monitors, instrument trays: locate, availability, cleaning/sterilisation status, tray completeness.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Medical Device", Code = "MED-DEVICE", RequiresInspection = true, InspectionIntervalDays = 365, AttributeSchema = { A("deviceClass"), A("ward") },
                        Lifecycle = Lc("Clean", new[] { "Clean", "InUse", "Dirty", "Cleaning", "UnderMaintenance" },
                            T("Clean", "InUse", OperationType.Issue), T("InUse", "Dirty", OperationType.Return), T("Dirty", "Cleaning", OperationType.ProcessStage), T("Cleaning", "Clean", OperationType.ProcessStage), T("*", "UnderMaintenance", OperationType.Maintain), T("UnderMaintenance", "Clean", OperationType.Return)) },
                    new ItemType { Name = "Instrument Tray", Code = "TRAY", IsContainer = true,
                        Lifecycle = Lc("Sterilised", new[] { "Dirty", "Decontaminated", "Sterilised", "InUse" },
                            T("Sterilised", "InUse", OperationType.Issue), T("InUse", "Dirty", OperationType.Return), T("Dirty", "Decontaminated", OperationType.ProcessStage), T("Decontaminated", "Sterilised", OperationType.ProcessStage, true)) },
                    new ItemType { Name = "Surgical Instrument", Code = "INSTRUMENT", TracksCycles = true, MaxCycles = 500 },
                },
                Rules =
                {
                    Alert("Dirty device in clean area", ItemEventType.Moved, Severity.Critical, "{item.name} is '{item.state}' but entered {toLocation.name}", C("item.state", "in", "Dirty,Cleaning"), C("toLocation.clean", "eq", true)),
                    Alert("Tray issued unsterilised", ItemEventType.CustodyChanged, Severity.Critical, "Tray {item.name} issued while '{item.state}'", C("itemType.code", "eq", "TRAY"), C("item.state", "ne", "InUse")),
                },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.ProcessStage, OperationType.Transfer, OperationType.Count, OperationType.Pack, OperationType.Unpack, OperationType.Maintain, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "chain-of-custody", Name = "Evidence, Samples & Documents", Vertical = "Custody",
            Description = "Police evidence, lab samples, physical files, archives: every custody change is an immutable event; zone exit without checkout is critical.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Evidence", Code = "EVIDENCE", AttributeSchema = { A("caseNumber", required: true), A("collectedBy"), A("sealNumber") },
                        Lifecycle = Lc("Collected", new[] { "Collected", "Stored", "CheckedOut", "Released", "Destroyed" },
                            T("Collected", "Stored", OperationType.Receive), T("Stored", "CheckedOut", OperationType.Issue), T("CheckedOut", "Stored", OperationType.Return), T("*", "Released", OperationType.Dispatch), T("*", "Destroyed", OperationType.Dispose)) },
                    new ItemType { Name = "Lab Sample", Code = "SAMPLE", TracksExpiry = true, AttributeSchema = { A("patientRef"), A("sampleType"), A("temperature") },
                        Lifecycle = Lc("Collected", new[] { "Collected", "Received", "Processing", "Stored", "Disposed" },
                            T("Collected", "Received", OperationType.Receive), T("*", "Processing", OperationType.ProcessStage), T("*", "Stored", OperationType.ProcessStage), T("*", "Disposed", OperationType.Dispose)) },
                    new ItemType { Name = "File", Code = "FILE", AttributeSchema = { A("fileNumber", required: true), A("department") },
                        Lifecycle = Lc("Archived", new[] { "Archived", "CheckedOut" }, T("Archived", "CheckedOut", OperationType.Issue), T("CheckedOut", "Archived", OperationType.Return)) },
                },
                Rules =
                {
                    Alert("Item left storage without checkout", ItemEventType.Moved, Severity.Critical, "{item.name} passed {fromLocation.name} exit while '{item.state}'", C("data.direction", "eq", "Out"), C("item.state", "in", "Stored,Archived,Collected")),
                    Alert("Checked-out item overdue", ItemEventType.Seen, Severity.Warning, "{item.name} overdue for return", C("item.overdue", "eq", true)),
                },
                Operations = { OperationType.Receive, OperationType.Issue, OperationType.Return, OperationType.Transfer, OperationType.ProcessStage, OperationType.Dispatch, OperationType.Dispose, OperationType.Count, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "library", Name = "Library & Media", Vertical = "Library",
            Description = "Books and media: self-checkout, return, shelf inventory, anti-theft exit gate.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Book", Code = "BOOK", AttributeSchema = { A("isbn"), A("author"), A("title", required: true), A("shelfMark") },
                        Lifecycle = Lc("OnShelf", new[] { "OnShelf", "OnLoan", "Lost", "Withdrawn" }, T("OnShelf", "OnLoan", OperationType.Issue), T("*", "OnShelf", OperationType.Return), T("*", "Withdrawn", OperationType.Dispose)) },
                },
                Rules =
                {
                    Alert("Anti-theft: item at exit gate not on loan", ItemEventType.Moved, Severity.Critical, "{item.name} passed the exit gate while '{item.state}'", C("data.direction", "eq", "Out"), C("item.state", "eq", "OnShelf")),
                    Alert("Loan overdue", ItemEventType.Seen, Severity.Info, "{item.name} loan overdue", C("item.overdue", "eq", true)),
                },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.Count, OperationType.Transfer, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "retail", Name = "Retail, Apparel & Loss Prevention", Vertical = "Retail",
            Description = "Item-level merchandise incl. jewellery: back-room/floor inventory, size-colour availability, fitting-room presence, unpaid exit alerts.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Merchandise", Code = "SKU-ITEM", AttributeSchema = { A("sku", required: true), A("size"), A("colour"), A("price", "number"), A("gtin") },
                        Lifecycle = Lc("BackRoom", new[] { "BackRoom", "SalesFloor", "FittingRoom", "Sold", "Returned" },
                            T("BackRoom", "SalesFloor", OperationType.Transfer), T("SalesFloor", "BackRoom", OperationType.Transfer), T("*", "Sold", OperationType.Dispatch), T("Sold", "Returned", OperationType.Return), T("Returned", "SalesFloor", OperationType.Transfer)) },
                    new ItemType { Name = "High-Value Item", Code = "HV-ITEM", AttributeSchema = { A("sku"), A("carat"), A("valuation", "number") },
                        Lifecycle = Lc("InSafe", new[] { "InSafe", "OnDisplay", "Sold" }, T("InSafe", "OnDisplay", OperationType.Transfer), T("OnDisplay", "InSafe", OperationType.Transfer), T("*", "Sold", OperationType.Dispatch)) },
                },
                Rules =
                {
                    Alert("Loss prevention: unpaid item at exit", ItemEventType.Moved, Severity.Critical, "Unpaid {item.name} ({item.identifier}) passed exit", C("data.direction", "eq", "Out"), C("item.state", "ne", "Sold")),
                    Alert("High-value item missing", ItemEventType.Counted, Severity.Critical, "{item.name} missing from stocktake", C("data.result", "eq", "Missing"), C("itemType.code", "eq", "HV-ITEM")),
                },
                LocationKinds = { LocationKind.Zone, LocationKind.Gate, LocationKind.Room },
                Operations = { OperationType.Receive, OperationType.Transfer, OperationType.Count, OperationType.Dispatch, OperationType.Return, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "returnable-assets", Name = "Returnable Assets, Kegs & Rental", Vertical = "Returnables",
            Description = "Crates, totes, pallets, kegs, cylinders, rental and event equipment: dispatch to customer, custody, overdue and return.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Returnable Container", Code = "RTI", IsContainer = true, AttributeSchema = { A("owner"), A("deposit", "number") },
                        Lifecycle = Lc("InPool", new[] { "InPool", "AtCustomer", "Damaged", "Scrapped" }, T("InPool", "AtCustomer", OperationType.Dispatch), T("AtCustomer", "InPool", OperationType.Return), T("*", "Damaged", OperationType.Inspect), T("*", "InPool", OperationType.Maintain), T("*", "Scrapped", OperationType.Dispose)) },
                    new ItemType { Name = "Keg", Code = "KEG", AttributeSchema = { A("beer"), A("litres", "number") },
                        Lifecycle = Lc("Empty", new[] { "Empty", "Filled", "AtDistributor", "AtCustomer", "Returned" },
                            T("Empty", "Filled", OperationType.ProcessStage, true), T("Filled", "AtDistributor", OperationType.Dispatch), T("AtDistributor", "AtCustomer", OperationType.Dispatch), T("*", "Empty", OperationType.Return)) },
                    new ItemType { Name = "Rental Equipment", Code = "RENTAL", RequiresInspection = true, InspectionIntervalDays = 90, AttributeSchema = { A("dailyRate", "number"), A("category") },
                        Lifecycle = Lc("Available", new[] { "Available", "Rented", "Returned", "Maintenance" }, T("Available", "Rented", OperationType.Dispatch), T("Rented", "Returned", OperationType.Return), T("Returned", "Available", OperationType.Inspect), T("*", "Maintenance", OperationType.Maintain), T("Maintenance", "Available", OperationType.Return)) },
                },
                Rules =
                {
                    Alert("Returnable overdue at customer", ItemEventType.Seen, Severity.Warning, "{item.name} is overdue from customer", C("item.overdue", "eq", true)),
                    Alert("Rental overdue", ItemEventType.Seen, Severity.Warning, "{item.name} rental overdue", C("item.overdue", "eq", true), C("itemType.code", "eq", "RENTAL")),
                },
                PartyKinds = { PartyKind.Customer },
                Operations = { OperationType.Dispatch, OperationType.Return, OperationType.Count, OperationType.Inspect, OperationType.Maintain, OperationType.ProcessStage, OperationType.Pack, OperationType.Unpack, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "manufacturing", Name = "Manufacturing WIP, Kanban & QC", Vertical = "Manufacturing",
            Description = "Parts, assemblies, kits and bins moving through workstations: production stages, kanban replenishment, quarantine and release.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Work-in-Progress Unit", Code = "WIP", AttributeSchema = { A("workOrder", required: true), A("routing") },
                        Lifecycle = Lc("Queued", new[] { "Queued", "Machining", "Assembly", "Testing", "Complete", "Quarantined" },
                            T("Queued", "Machining", OperationType.ProcessStage), T("Machining", "Assembly", OperationType.ProcessStage), T("Assembly", "Testing", OperationType.ProcessStage), T("Testing", "Complete", OperationType.Inspect), T("*", "Quarantined", OperationType.Inspect), T("Quarantined", "Testing", OperationType.ProcessStage)) },
                    new ItemType { Name = "Kanban Bin", Code = "KANBAN-BIN", IsContainer = true, Category = ItemCategory.Quantity, Unit = "ea", ReorderPoint = 20, AttributeSchema = { A("partNumber", required: true), A("station") },
                        Lifecycle = Lc("Full", new[] { "Full", "InUse", "Empty" }, T("Full", "InUse", OperationType.Transfer), T("*", "Empty", OperationType.ProcessStage), T("Empty", "Full", OperationType.Receive)) },
                    new ItemType { Name = "Kit", Code = "KIT", IsContainer = true, AttributeSchema = { A("bom") } },
                    new ItemType { Name = "Raw Material", Code = "RAW", Category = ItemCategory.Quantity, Unit = "kg", AttributeSchema = { A("material"), A("supplierLot") } },
                },
                Rules =
                {
                    Alert("Kanban: empty bin at replenishment point", ItemEventType.Moved, Severity.Info, "Bin {item.identifier} ({item.attributes.partNumber}) needs replenishment", C("itemType.code", "eq", "KANBAN-BIN"), C("toLocation.kanban", "eq", true)),
                    Alert("Quarantined unit left quarantine", ItemEventType.Moved, Severity.Critical, "{item.name} is quarantined but moved to {toLocation.name}", C("item.state", "eq", "Quarantined"), C("toLocation.quarantine", "ne", true)),
                },
                Operations = { OperationType.ProcessStage, OperationType.Transfer, OperationType.Inspect, OperationType.Pack, OperationType.Unpack, OperationType.Receive, OperationType.Adjust, OperationType.Count, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "people-presence", Name = "Personnel, Visitors, Muster & Race Timing", Vertical = "People",
            Description = "Badges and bibs (active/BLE/UHF): zone presence, authorised zones, muster counts and checkpoint timing.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Staff Badge", Code = "BADGE", AttributeSchema = { A("person", required: true), A("authorisedZones") } },
                    new ItemType { Name = "Visitor Badge", Code = "VISITOR", AttributeSchema = { A("host"), A("company") },
                        Lifecycle = Lc("Available", new[] { "Available", "Issued" }, T("Available", "Issued", OperationType.Issue), T("Issued", "Available", OperationType.Return)) },
                    new ItemType { Name = "Race Bib", Code = "BIB", AttributeSchema = { A("athlete"), A("category") } },
                },
                Rules =
                {
                    Alert("Visitor in restricted zone", ItemEventType.Moved, Severity.Critical, "Visitor badge {item.identifier} entered restricted {toLocation.name}", C("itemType.code", "eq", "VISITOR"), C("toLocation.restricted", "eq", true)),
                },
                LocationKinds = { LocationKind.Zone, LocationKind.Gate, LocationKind.MusterPoint, LocationKind.Checkpoint },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.Count, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "livestock-agri", Name = "Livestock, Plants & Research Animals", Vertical = "Agriculture",
            Description = "Animals (LF/UHF ear tags), plants, cages and produce bins: identity, health/experiment records, movement and lifecycle.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Animal", Code = "ANIMAL", AttributeSchema = { A("species", required: true), A("breed"), A("sex", "select", false, "M", "F"), A("dob", "date") },
                        Lifecycle = Lc("Alive", new[] { "Alive", "Treatment", "Sold", "Deceased" }, T("*", "Treatment", OperationType.Maintain), T("Treatment", "Alive", OperationType.Return), T("*", "Sold", OperationType.Dispatch), T("*", "Deceased", OperationType.Dispose)) },
                    new ItemType { Name = "Plant", Code = "PLANT", AttributeSchema = { A("species"), A("batch") },
                        Lifecycle = Lc("Seedling", new[] { "Seedling", "Growing", "ReadyForSale", "Sold" }, T("Seedling", "Growing", OperationType.ProcessStage), T("Growing", "ReadyForSale", OperationType.Inspect), T("*", "Sold", OperationType.Dispatch)) },
                    new ItemType { Name = "Cage", Code = "CAGE", IsContainer = true, AttributeSchema = { A("protocol") } },
                },
                Operations = { OperationType.Transfer, OperationType.Inspect, OperationType.Maintain, OperationType.ProcessStage, OperationType.Dispatch, OperationType.Pack, OperationType.Unpack, OperationType.Count, OperationType.Commission },
            }
        },
    };
}

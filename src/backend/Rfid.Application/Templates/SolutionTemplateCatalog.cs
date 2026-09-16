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
    /// <summary>Transition keyed by a template-defined operation code (see <see cref="Op"/>).</summary>
    private static (string, string, string, bool) T(string from, string to, string on, bool cycle = false) => (from, to, on, cycle);
    private static OperationEffect Fx(string kind, params (string k, object? v)[] p) => new(kind, p.ToDictionary(x => x.k, x => x.v));
    /// <summary>A vertical operation: a named composition of built-in effects, installed as configuration when the template is applied.</summary>
    private static OperationDefinition Op(string code, string name, string description, OperationType baseType, ItemEventType? ev, OperationRequirements? req, string[]? itemTypes, params OperationEffect[] effects) => new()
    {
        Code = code, Name = name, Description = description, BaseType = baseType, EventType = ev, Requires = req ?? new(), Effects = effects.ToList(), ItemTypeCodes = itemTypes?.ToList() ?? new(), Enabled = true,
    };
    /// <summary>Same, with EPCIS vocabulary the operation stamps on its events (CBV step, or a domain step such as "sterilizing").</summary>
    private static OperationDefinition Op(string code, string name, string description, OperationType baseType, ItemEventType? ev, OperationRequirements? req, string[]? itemTypes, (string bizStep, string disposition) cbv, params OperationEffect[] effects)
    { var d = Op(code, name, description, baseType, ev, req, itemTypes, effects); d.EventData["bizStep"] = cbv.bizStep; d.EventData["disposition"] = cbv.disposition; return d; }
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
                OperationDefinitions =
                {
                    Op("Calibrate", "Calibrate", "Record a calibration: inspection result plus the 'calibrated' flag", OperationType.Inspect, ItemEventType.Inspected, null, new[] { "TOOL" }, ("inspecting", "conformant"),
                        Fx(OperationEffectKinds.RecordInspection), Fx(OperationEffectKinds.SetAttribute, ("key", "calibrated"), ("value", true)), Fx(OperationEffectKinds.SetAttribute, ("key", "calibratedAt"), ("value", "{now}"))),
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
                    Alert("PPE expired", ItemEventType.CustodyChanged, Severity.Critical, "Expired {item.name} issued", C("item.hasCustodian", "eq", true), C("item.daysUntilExpiry", "lt", 0)),
                    Alert("Failed PPE issued", ItemEventType.CustodyChanged, Severity.Critical, "{item.name} failed inspection and was issued", C("item.hasCustodian", "eq", true), C("item.state", "eq", "Failed")),
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
                            T("Sterilised", "InUse", OperationType.Issue), T("InUse", "Dirty", OperationType.Return),
                            T("Dirty", "Decontaminated", "Decontaminate"), T("Decontaminated", "Sterilised", "Sterilise", true),
                            // generic stage advance stays available for tenants that drive the flow from a workflow screen
                            T("Dirty", "Decontaminated", OperationType.ProcessStage), T("Decontaminated", "Sterilised", OperationType.ProcessStage, true)) },
                    new ItemType { Name = "Surgical Instrument", Code = "INSTRUMENT", TracksCycles = true, MaxCycles = 500 },
                },
                OperationDefinitions =
                {
                    Op("Decontaminate", "Decontaminate", "Washer/disinfector complete: Dirty → Decontaminated", OperationType.ProcessStage, ItemEventType.StateChanged, null, new[] { "TRAY", "INSTRUMENT" }, ("decontaminating", "in_progress"),
                        Fx(OperationEffectKinds.SetState), Fx(OperationEffectKinds.Move, ("optional", true))),
                    Op("Sterilise", "Sterilise", "Autoclave cycle complete: Decontaminated → Sterilised, cycle count +1, sterile-store placement", OperationType.ProcessStage, ItemEventType.StateChanged, null, new[] { "TRAY", "INSTRUMENT" }, ("sterilizing", "sterile"),
                        Fx(OperationEffectKinds.SetState), Fx(OperationEffectKinds.Move, ("optional", true)), Fx(OperationEffectKinds.SetAttribute, ("key", "lastSterilisedAt"), ("value", "{now}"))),
                },
                Rules =
                {
                    Alert("Dirty device in clean area", ItemEventType.Moved, Severity.Critical, "{item.name} is '{item.state}' but entered {toLocation.name}", C("item.state", "in", "Dirty,Cleaning"), C("toLocation.clean", "eq", true)),
                    Alert("Tray issued unsterilised", ItemEventType.CustodyChanged, Severity.Critical, "Tray {item.name} issued while '{item.state}'", C("itemType.code", "eq", "TRAY"), C("item.hasCustodian", "eq", true), C("item.state", "ne", "InUse")),
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
        new SolutionTemplate
        {
            Code = "museum-artwork", Name = "Museum Collections & Artwork", Vertical = "Culture",
            Description = "Artefacts, paintings, exhibits, crates: storage, display, loans, packing and transport with full movement history.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Artefact", Code = "ARTEFACT", AttributeSchema = { A("accessionNo", required: true), A("period"), A("material"), A("insuredValue", "number") },
                        Lifecycle = Lc("InStorage", new[] { "InStorage", "OnDisplay", "OnLoan", "Conservation", "Deaccessioned" },
                            T("*", "OnDisplay", OperationType.Transfer), T("OnDisplay", "InStorage", OperationType.Return), T("*", "OnLoan", OperationType.Dispatch), T("OnLoan", "InStorage", OperationType.Return), T("*", "Conservation", OperationType.Maintain), T("Conservation", "InStorage", OperationType.Return), T("*", "Deaccessioned", OperationType.Dispose)) },
                    new ItemType { Name = "Artwork", Code = "ARTWORK", AttributeSchema = { A("artist"), A("title", required: true), A("year"), A("insuredValue", "number") },
                        Lifecycle = Lc("InStorage", new[] { "InStorage", "Packed", "InTransit", "Installed" },
                            T("InStorage", "Packed", OperationType.Pack), T("Packed", "InTransit", OperationType.Dispatch), T("*", "Installed", OperationType.Unpack), T("*", "InStorage", OperationType.Return)) },
                    new ItemType { Name = "Art Crate", Code = "ART-CRATE", IsContainer = true, AttributeSchema = { A("dimensions"), A("climateControlled", "bool") } },
                },
                Rules =
                {
                    Alert("Artefact left building without loan", ItemEventType.Moved, Severity.Critical, "{item.name} passed exit while '{item.state}'", C("data.direction", "eq", "Out"), C("item.state", "in", "InStorage,OnDisplay,Conservation")),
                    Alert("High-value artefact missing from gallery count", ItemEventType.Counted, Severity.Critical, "{item.name} ({item.attributes.accessionNo}) not found", C("data.result", "eq", "Missing")),
                },
                Operations = { OperationType.Transfer, OperationType.Return, OperationType.Dispatch, OperationType.Pack, OperationType.Unpack, OperationType.Maintain, OperationType.Count, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "hospitality", Name = "Hotel Assets & Minibar", Vertical = "Hospitality",
            Description = "Room assets (TVs, appliances, furniture), minibar stock and shrinkage: room assignment, transfer, maintenance, consumption and replenishment. Combine with Linen & Laundry.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Room Asset", Code = "HOTEL-ASSET", AttributeSchema = { A("category", "select", false, "TV", "Minibar fridge", "Safe", "Hairdryer", "Iron", "Furniture", "Artwork"), A("brand"), A("room") },
                        Lifecycle = Lc("InRoom", new[] { "InRoom", "InStore", "Maintenance", "Retired" }, T("*", "InRoom", OperationType.Transfer), T("*", "InStore", OperationType.Return), T("*", "Maintenance", OperationType.Maintain), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Minibar Stock", Code = "MINIBAR", Category = ItemCategory.Quantity, Unit = "ea", ReorderPoint = 2, TracksExpiry = true, AttributeSchema = { A("sku", required: true), A("price", "number") } },
                },
                Rules = { Alert("Minibar needs replenishment", ItemEventType.QuantityChanged, Severity.Info, "{item.name} in {toLocation.name} is down to {item.quantity}", C("item.quantity", "lte", 2)) },
                LocationKinds = { LocationKind.Floor, LocationKind.Room },
                Operations = { OperationType.Transfer, OperationType.Return, OperationType.Maintain, OperationType.Count, OperationType.Adjust, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "food-coldchain", Name = "Food Trays, Food Production & Cold Chain", Vertical = "Food",
            Description = "Meal trays circulating kitchen ↔ wards, production batches by stage, and cold-chain containers with temperature excursions.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Meal Tray", Code = "FOOD-TRAY", TracksCycles = true, MaxCycles = 2000,
                        Lifecycle = Lc("Kitchen", new[] { "Kitchen", "Delivered", "Returned", "Washing", "Retired" }, T("Kitchen", "Delivered", OperationType.Dispatch), T("Delivered", "Returned", OperationType.Return), T("Returned", "Washing", OperationType.ProcessStage, true), T("Washing", "Kitchen", OperationType.ProcessStage), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Production Batch", Code = "FOOD-BATCH", IsContainer = true, TracksExpiry = true, AttributeSchema = { A("product", required: true), A("allergens") },
                        Lifecycle = Lc("Prepared", new[] { "Prepared", "Cooking", "Chilling", "Packed", "Shipped", "Recalled" }, T("Prepared", "Cooking", OperationType.ProcessStage), T("Cooking", "Chilling", OperationType.ProcessStage), T("Chilling", "Packed", OperationType.ProcessStage), T("Packed", "Shipped", OperationType.Dispatch), T("*", "Recalled", OperationType.ProcessStage)) },
                    new ItemType { Name = "Cold-Chain Container", Code = "COLD-CONTAINER", IsContainer = true, AttributeSchema = { A("minTempC", "number"), A("maxTempC", "number"), A("lastTempC", "number"), A("tempExcursion", "bool") },
                        Lifecycle = Lc("Loaded", new[] { "Loaded", "InTransit", "Delivered", "Returned" }, T("Loaded", "InTransit", OperationType.Dispatch), T("InTransit", "Delivered", OperationType.Receive), T("*", "Returned", OperationType.Return), T("Returned", "Loaded", OperationType.Pack)) },
                },
                Rules =
                {
                    Alert("Cold-chain temperature excursion", ItemEventType.Moved, Severity.Critical, "Container {item.identifier} arrived with a temperature excursion ({item.attributes.lastTempC} °C)", C("data.dispatch", "notexists", null), C("item.attributes.tempExcursion", "eq", true)),
                    Alert("Expired batch shipped", ItemEventType.Moved, Severity.Critical, "Batch {item.name} shipped past expiry", C("data.dispatch", "eq", true), C("item.daysUntilExpiry", "lt", 0)),
                },
                Operations = { OperationType.Dispatch, OperationType.Return, OperationType.ProcessStage, OperationType.Receive, OperationType.Pack, OperationType.Unpack, OperationType.Count, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "fleet-aviation", Name = "Vehicles, Fleet, Tyres & Aircraft Parts", Vertical = "Mobility",
            Description = "Yard-managed vehicles and trailers, fleet equipment, tyre lifecycle and aircraft rotables with maintenance custody.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Vehicle", Code = "VEHICLE", IsContainer = true, AttributeSchema = { A("plate", required: true), A("make"), A("model"), A("vin") },
                        Lifecycle = Lc("Arrived", new[] { "Arrived", "Parked", "InWorkshop", "Dispatched" }, T("*", "Parked", OperationType.Transfer), T("*", "InWorkshop", OperationType.Maintain), T("InWorkshop", "Parked", OperationType.Return), T("*", "Dispatched", OperationType.Dispatch), T("Dispatched", "Arrived", OperationType.Receive)) },
                    new ItemType { Name = "Tyre", Code = "TYRE", TracksCycles = true, MaxCycles = 6, AttributeSchema = { A("size"), A("dot"), A("treadMm", "number") },
                        Lifecycle = Lc("New", new[] { "New", "Fitted", "Removed", "Retread", "Scrapped" }, T("*", "Fitted", OperationType.Pack, true), T("Fitted", "Removed", OperationType.Unpack), T("Removed", "Retread", OperationType.Maintain), T("Retread", "New", OperationType.Return), T("*", "Scrapped", OperationType.Dispose)) },
                    new ItemType { Name = "Aircraft Rotable", Code = "ROTABLE", RequiresInspection = true, InspectionIntervalDays = 365, AttributeSchema = { A("partNumber", required: true), A("serialNumber"), A("hoursSinceOverhaul", "number") },
                        Lifecycle = Lc("Serviceable", new[] { "Serviceable", "Installed", "Unserviceable", "InRepair", "Quarantined" }, T("Serviceable", "Installed", OperationType.Pack), T("Installed", "Unserviceable", OperationType.Unpack), T("Unserviceable", "InRepair", OperationType.Maintain), T("InRepair", "Serviceable", OperationType.Inspect), T("*", "Quarantined", OperationType.Inspect)) },
                    new ItemType { Name = "Aircraft", Code = "AIRCRAFT", IsContainer = true, AttributeSchema = { A("registration", required: true), A("type"), A("operator") } },
                    new ItemType { Name = "Fleet Equipment", Code = "FLEET-EQUIP", RequiresInspection = true, InspectionIntervalDays = 90, AttributeSchema = { A("type"), A("vehicle") } },
                },
                Rules =
                {
                    Alert("Vehicle left yard without dispatch", ItemEventType.Moved, Severity.Warning, "{item.name} ({item.attributes.plate}) passed the gate while '{item.state}'", C("data.direction", "eq", "Out"), C("itemType.code", "eq", "VEHICLE"), C("item.state", "ne", "Dispatched")),
                    Alert("Unserviceable part on aircraft", ItemEventType.Packed, Severity.Critical, "{item.name} fitted while '{item.state}'", C("itemType.code", "eq", "ROTABLE"), C("item.state", "in", "Unserviceable,Quarantined")),
                },
                LocationKinds = { LocationKind.Yard, LocationKind.Gate, LocationKind.Vehicle },
                Operations = { OperationType.Receive, OperationType.Transfer, OperationType.Dispatch, OperationType.Maintain, OperationType.Return, OperationType.Pack, OperationType.Unpack, OperationType.Inspect, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "waste-management", Name = "Waste Bins & Waste Tracking", Vertical = "Waste",
            Description = "Bin ownership and collection events read by truck-mounted antennas; hazardous waste containers with collection → treatment → disposal chain of custody.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Waste Bin", Code = "WASTE-BIN", AttributeSchema = { A("owner"), A("sizeL", "number"), A("stream", "select", false, "General", "Recycling", "Organic", "Glass") },
                        Lifecycle = Lc("Deployed", new[] { "Deployed", "Collected", "Damaged", "Withdrawn" }, T("*", "Collected", OperationType.Count), T("Collected", "Deployed", OperationType.Return), T("*", "Damaged", OperationType.Inspect), T("*", "Withdrawn", OperationType.Dispose)) },
                    new ItemType { Name = "Waste Container", Code = "WASTE-CONTAINER", AttributeSchema = { A("wasteCode", required: true), A("hazardous", "bool"), A("consignmentNote") },
                        Lifecycle = Lc("Filled", new[] { "Filled", "Collected", "InTransit", "Treated", "Disposed" }, T("Filled", "Collected", OperationType.Issue), T("Collected", "InTransit", OperationType.Dispatch), T("InTransit", "Treated", OperationType.ProcessStage), T("Treated", "Disposed", OperationType.Dispose)) },
                },
                Rules = { Alert("Hazardous container disposed without treatment", ItemEventType.Disposed, Severity.Critical, "{item.name} disposed from state '{event.fromState}'", C("item.attributes.hazardous", "eq", true), C("event.fromState", "ne", "Treated")) },
                Operations = { OperationType.Count, OperationType.Return, OperationType.Issue, OperationType.Dispatch, OperationType.ProcessStage, OperationType.Inspect, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "postal-baggage", Name = "Postal Sorting & Baggage Tracking", Vertical = "Transport",
            Description = "Mail bags, cages and parcels routed by portal reads; airport bags through check-in, loading, transfer and arrival.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Bag (airline)", Code = "BAG", AttributeSchema = { A("pnr"), A("flight"), A("passenger"), A("destination") },
                        Lifecycle = Lc("CheckedIn", new[] { "CheckedIn", "Sorted", "Loaded", "Transferred", "Arrived", "Delivered", "Mishandled" },
                            T("CheckedIn", "Sorted", OperationType.ProcessStage), T("Sorted", "Loaded", OperationType.Pack), T("Loaded", "Transferred", OperationType.ProcessStage), T("*", "Arrived", OperationType.Receive), T("Arrived", "Delivered", OperationType.Issue), T("*", "Mishandled", OperationType.ProcessStage)) },
                    new ItemType { Name = "Mail Cage", Code = "MAIL-CAGE", IsContainer = true, AttributeSchema = { A("route") } },
                    new ItemType { Name = "Parcel", Code = "PARCEL", AttributeSchema = { A("trackingNo", required: true), A("destination"), A("service") },
                        Lifecycle = Lc("Accepted", new[] { "Accepted", "InSort", "OutForDelivery", "Delivered", "Returned" }, T("Accepted", "InSort", OperationType.Receive), T("InSort", "OutForDelivery", OperationType.Dispatch), T("OutForDelivery", "Delivered", OperationType.Issue), T("*", "Returned", OperationType.Return)) },
                    new ItemType { Name = "Unit Load Device", Code = "ULD", IsContainer = true, AttributeSchema = { A("uldCode"), A("flight") } },
                },
                Rules = { Alert("Bag mishandled", ItemEventType.StateChanged, Severity.Critical, "Bag {item.identifier} ({item.attributes.passenger}, {item.attributes.flight}) flagged as mishandled", C("event.toState", "eq", "Mishandled")) },
                LocationKinds = { LocationKind.Dock, LocationKind.Gate, LocationKind.Zone },
                Operations = { OperationType.Receive, OperationType.ProcessStage, OperationType.Pack, OperationType.Unpack, OperationType.Dispatch, OperationType.Issue, OperationType.Return, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "datacentre-cables", Name = "Data Centre Assets & Cable Reels", Vertical = "IT Infrastructure",
            Description = "Servers and network gear audited by rack position; cable drums tracked by remaining length, custody and location.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Server / Network Device", Code = "DC-ASSET", AttributeSchema = { A("hostname"), A("rackU"), A("model"), A("owner") },
                        Lifecycle = Lc("Spare", new[] { "Spare", "Installed", "Decommissioned", "Wiped" }, T("*", "Installed", OperationType.Transfer), T("Installed", "Spare", OperationType.Return), T("*", "Decommissioned", OperationType.ProcessStage), T("Decommissioned", "Wiped", OperationType.Dispose)) },
                    new ItemType { Name = "Cable Reel", Code = "CABLE-REEL", Category = ItemCategory.Quantity, Unit = "m", ReorderPoint = 50, AttributeSchema = { A("cableType", required: true), A("gauge") } },
                },
                Rules =
                {
                    Alert("Server left data hall", ItemEventType.Moved, Severity.Critical, "{item.name} ({item.attributes.hostname}) left the data hall while '{item.state}'", C("data.direction", "eq", "Out"), C("itemType.code", "eq", "DC-ASSET"), C("item.state", "eq", "Installed")),
                    Alert("Cable reel nearly empty", ItemEventType.QuantityChanged, Severity.Info, "{item.name} has {item.quantity} m left", C("item.quantity", "lt", 50)),
                },
                LocationKinds = { LocationKind.Rack, LocationKind.Room },
                Operations = { OperationType.Transfer, OperationType.Return, OperationType.Count, OperationType.Adjust, OperationType.ProcessStage, OperationType.Dispose, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "field-industrial", Name = "Construction, Oil & Gas, Offshore & Marine", Vertical = "Industrial",
            Description = "Yard-to-site transfers, mobilisation/demobilisation, onshore ↔ offshore container movements, vessel equipment and inspection certificates.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "Site Equipment", Code = "SITE-EQUIP", RequiresInspection = true, InspectionIntervalDays = 180, AttributeSchema = { A("category"), A("certificateNo"), A("owner") },
                        Lifecycle = Lc("InYard", new[] { "InYard", "OnSite", "Mobilised", "Demobilised", "UnderRepair", "Scrapped" }, T("*", "OnSite", OperationType.Transfer), T("*", "Mobilised", OperationType.Dispatch), T("Mobilised", "Demobilised", OperationType.Return), T("*", "InYard", OperationType.Receive), T("*", "UnderRepair", OperationType.Maintain), T("*", "Scrapped", OperationType.Dispose)) },
                    new ItemType { Name = "Pipe / Valve", Code = "OG-COMPONENT", AttributeSchema = { A("heatNumber"), A("spec"), A("sizeInch", "number") },
                        Lifecycle = Lc("InYard", new[] { "InYard", "Mobilised", "Installed", "Returned" }, T("InYard", "Mobilised", OperationType.Dispatch), T("Mobilised", "Installed", OperationType.ProcessStage), T("*", "Returned", OperationType.Return)) },
                    new ItemType { Name = "Offshore Container (CCU)", Code = "CCU", IsContainer = true, RequiresInspection = true, InspectionIntervalDays = 365, AttributeSchema = { A("ccuNumber", required: true), A("tareKg", "number"), A("dnvCert") },
                        Lifecycle = Lc("Onshore", new[] { "Onshore", "Offshore", "InTransit" }, T("Onshore", "InTransit", OperationType.Dispatch), T("InTransit", "Offshore", OperationType.Receive), T("Offshore", "InTransit", OperationType.Dispatch), T("InTransit", "Onshore", OperationType.Receive)) },
                    new ItemType { Name = "Vessel Equipment", Code = "VESSEL-EQUIP", RequiresInspection = true, InspectionIntervalDays = 365, AttributeSchema = { A("solasCategory"), A("vessel") },
                        Lifecycle = Lc("Ashore", new[] { "Ashore", "OnVessel", "Servicing" }, T("Ashore", "OnVessel", OperationType.Transfer), T("OnVessel", "Ashore", OperationType.Return), T("*", "Servicing", OperationType.Maintain), T("Servicing", "Ashore", OperationType.Return)) },
                },
                Rules =
                {
                    Alert("Equipment mobilised with expired inspection", ItemEventType.Moved, Severity.Critical, "{item.name} sent offshore with inspection overdue", C("data.dispatch", "eq", true), C("item.daysUntilInspection", "lt", 0)),
                    Alert("Site equipment missing at count", ItemEventType.Counted, Severity.Warning, "{item.name} missing from site count", C("data.result", "eq", "Missing")),
                },
                LocationKinds = { LocationKind.Yard, LocationKind.Vessel, LocationKind.Site, LocationKind.Dock },
                Operations = { OperationType.Transfer, OperationType.Dispatch, OperationType.Receive, OperationType.Return, OperationType.Pack, OperationType.Unpack, OperationType.Inspect, OperationType.Maintain, OperationType.Count, OperationType.ProcessStage, OperationType.Commission },
            }
        },
        new SolutionTemplate
        {
            Code = "education-events-sports", Name = "School, Event & Sports Equipment", Vertical = "Education & Events",
            Description = "Student/staff device assignment and audits, AV/staging cases packed for venues, sports equipment issue and return.",
            Definition = new()
            {
                ItemTypes =
                {
                    new ItemType { Name = "School Device", Code = "SCHOOL-DEVICE", AttributeSchema = { A("assetTag"), A("model"), A("yearGroup") },
                        Lifecycle = Lc("InStock", new[] { "InStock", "Assigned", "Repair", "Retired" }, T("InStock", "Assigned", OperationType.Issue), T("*", "InStock", OperationType.Return), T("*", "Repair", OperationType.Maintain), T("*", "Retired", OperationType.Dispose)) },
                    new ItemType { Name = "Event Case", Code = "EVENT-CASE", IsContainer = true, AttributeSchema = { A("contentsList"), A("weightKg", "number") },
                        Lifecycle = Lc("InStore", new[] { "InStore", "Packed", "AtVenue", "Returned" }, T("InStore", "Packed", OperationType.ProcessStage), T("Packed", "AtVenue", OperationType.Dispatch), T("AtVenue", "Returned", OperationType.Return), T("Returned", "InStore", OperationType.Count)) },
                    new ItemType { Name = "AV Equipment", Code = "AV-EQUIP", AttributeSchema = { A("category", "select", false, "Speaker", "Mixer", "Light", "Cable", "Microphone", "Screen"), A("serial") } },
                    new ItemType { Name = "Sports Equipment", Code = "SPORTS-EQUIP", AttributeSchema = { A("sport"), A("size") },
                        Lifecycle = Lc("InStore", new[] { "InStore", "Issued", "Damaged" }, T("InStore", "Issued", OperationType.Issue), T("Issued", "InStore", OperationType.Return), T("*", "Damaged", OperationType.Inspect)) },
                },
                Rules =
                {
                    Alert("Case returned incomplete", ItemEventType.Counted, Severity.Warning, "Event case {item.name}: contents missing", C("data.result", "eq", "Missing")),
                    Alert("Device not returned by student", ItemEventType.Seen, Severity.Info, "{item.name} overdue", C("item.overdue", "eq", true)),
                },
                PartyKinds = { PartyKind.Person, PartyKind.Department, PartyKind.Customer },
                Operations = { OperationType.Issue, OperationType.Return, OperationType.Pack, OperationType.Unpack, OperationType.ProcessStage, OperationType.Dispatch, OperationType.Count, OperationType.Maintain, OperationType.Commission },
            }
        },
    };
}

using Rfid.Domain;

namespace Rfid.Infrastructure.Persistence.Demo;

/// <summary>
/// One realistic demo dataset per solution template. Together they exercise every system in the
/// requirements table (assets, inventory, warehouse, tools/FOD, linen, PPE, medical, evidence, library,
/// retail, returnables/kegs/rental, manufacturing/kanban/QC, people/muster/race, livestock, museums,
/// hotels, food/cold chain, fleet/aviation, waste, postal/baggage, data centre, field industrial,
/// education/events/sports).
/// </summary>
public static class DemoScenarios
{
    private static Dictionary<string, object?> Attr(params (string k, object? v)[] kv) => kv.ToDictionary(x => x.k, x => x.v);
    private static LocDef L(string name, LocationKind kind, string? parent = null, Dictionary<string, object?>? attrs = null, bool mobile = false, string? code = null) => new(name, kind, parent, code, attrs, mobile);
    private static PartyDef P(string name, PartyKind kind = PartyKind.Employee, string? code = null) => new(name, kind, code);
    private static ItemDef I(string type, string id, string name, string? loc = null, string? state = null, string? custodian = null, string? parent = null, decimal qty = 1, string? lot = null, int? expiryDays = null, int cycles = 0, Dictionary<string, object?>? attrs = null, int? inspectedDaysAgo = null, int? dueBackDays = null, int seenHoursAgo = 6, decimal? cost = null)
        => new(type, id, name, loc, state, custodian, parent, qty, lot, expiryDays, cycles, attrs, inspectedDaysAgo, dueBackDays, seenHoursAgo, cost);
    private static AntennaDef Ant(int port, string loc, AntennaDirection dir = AntennaDirection.None) => new(port, loc, dir);
    private static AntennaDef Anchor(int port, string loc, double x, double y) => new(port, loc, AntennaDirection.None, x, y);
    private static DeviceDef D(string name, DeviceKind kind, string model, string? token = null, params AntennaDef[] antennas) => new(name, kind, model, antennas, token);
    private static StepDef S(OperationType type, double daysAgo, string[] items, string? to = null, string? party = null, string? state = null, string? container = null, decimal? qty = null, string? reference = null, int? dueBackDays = null)
        => new(type, items, daysAgo, to, party, state, container, qty, reference, dueBackDays);
    private static ReadDef R(string device, int port, double hoursAgo, params string[] items) => new(device, port, items, hoursAgo);
    private static string[] Ids(params string[] ids) => ids;
    private const AntennaDirection In = AntennaDirection.In, Out = AntennaDirection.Out;

    public static readonly List<Scenario> All = new()
    {
        // ───────────────────────── Assets / IT / fixed assets / smart cabinet ─────────────────────────
        new Scenario
        {
            Template = "asset-management", Site = "Northgate Head Office", SiteCode = "HQ", CompanyPrefix = "0614141",
            Locations =
            {
                L("Tower A", LocationKind.Building), L("Floor 1", LocationKind.Floor, "Tower A"), L("Floor 2", LocationKind.Floor, "Tower A"),
                L("IT Store", LocationKind.Room, "Floor 1"), L("Server Room", LocationKind.Room, "Floor 1", Attr(("restricted", true))), L("Finance Office", LocationKind.Room, "Floor 2"), L("Meeting Room 2.1", LocationKind.Room, "Floor 2"),
                L("IT Smart Cabinet", LocationKind.Cabinet, "IT Store"), L("Main Entrance", LocationKind.Gate, "Tower A"),
            },
            Parties = { P("Jane Lee", PartyKind.Employee, "EMP001"), P("Sam Patel", PartyKind.Employee, "EMP002"), P("Priya Nair", PartyKind.Employee, "EMP003"), P("Finance", PartyKind.Department, "FIN"), P("IT Services", PartyKind.Department, "IT") },
            Items =
            {
                I("IT-ASSET", "LT-0001", "Dell Latitude 5540", "IT Store", "Stock", attrs: Attr(("hostname", "LT-0001"), ("os", "Windows 11"), ("serialNumber", "5CG3391")), cost: 1450),
                I("IT-ASSET", "LT-0002", "MacBook Pro 14", "Finance Office", "Assigned", "Jane Lee", attrs: Attr(("hostname", "MBP-JLEE"), ("os", "macOS 15")), cost: 2399),
                I("IT-ASSET", "LT-0003", "ThinkPad X1 Carbon", "IT Store", "Stock", attrs: Attr(("hostname", "LT-0003"), ("os", "Windows 11")), cost: 1899),
                I("IT-ASSET", "LT-0004", "Surface Laptop 6", "IT Store", "Repair", attrs: Attr(("hostname", "LT-0004"), ("os", "Windows 11")), cost: 1299),
                I("IT-ASSET", "MON-0001", "Dell U2723QE Monitor", "IT Store", "Stock", cost: 620), I("IT-ASSET", "MON-0002", "Dell U2723QE Monitor", "Finance Office", "Assigned", "Jane Lee", cost: 620),
                I("IT-ASSET", "SRV-0001", "HPE DL380 Gen11", "Server Room", "Assigned", "IT Services", attrs: Attr(("hostname", "APP01"), ("os", "Ubuntu 24.04")), cost: 9800),
                I("IT-ASSET", "SRV-0002", "HPE DL380 Gen11", "Server Room", "Assigned", "IT Services", attrs: Attr(("hostname", "DB01"), ("os", "Ubuntu 24.04")), cost: 9800),
                I("IT-ASSET", "PH-0001", "iPhone 16", "IT Smart Cabinet", "Stock", cost: 899), I("IT-ASSET", "PH-0002", "iPhone 16", "IT Smart Cabinet", "Stock", cost: 899),
                I("ASSET", "PRJ-0001", "Epson EB-L200 Projector", "Meeting Room 2.1", "InUse", "Finance", attrs: Attr(("manufacturer", "Epson"), ("model", "EB-L200"), ("costCentre", "CC-200")), cost: 1100),
                I("ASSET", "CHR-0001", "Herman Miller Aeron", "Finance Office", "InUse", "Finance", attrs: Attr(("manufacturer", "Herman Miller"), ("costCentre", "CC-200")), cost: 1350),
                I("ASSET", "CHR-0002", "Herman Miller Aeron", "IT Store", "InStorage", cost: 1350),
                I("ASSET", "DESK-0001", "Sit-stand desk 160", "Finance Office", "InUse", "Finance", cost: 780),
                I("ASSET", "UPS-0001", "APC Smart-UPS 3000", "Server Room", "InUse", "IT Services", attrs: Attr(("warrantyUntil", "2027-03-31")), cost: 2200),
            },
            Devices =
            {
                D("Handheld RFD40 #1", DeviceKind.Handheld, "Zebra RFD40", "demo-handheld-token"),
                D("Main Entrance Gate", DeviceKind.Gate, "Impinj R700", "demo-gate-token", Ant(1, "Main Entrance", Out), Ant(2, "Main Entrance", In)),
                D("IT Smart Cabinet Reader", DeviceKind.Cabinet, "Zebra FX7500", null, Ant(1, "IT Smart Cabinet")),
            },
            History =
            {
                S(OperationType.Issue, 20, Ids("LT-0002", "MON-0002"), party: "Jane Lee", to: "Finance Office", reference: "TICKET-4411"),
                S(OperationType.Issue, 12, Ids("LT-0003"), party: "Sam Patel", to: "Floor 2", reference: "TICKET-4502"),
                S(OperationType.Return, 3, Ids("LT-0003"), to: "IT Store", reference: "TICKET-4502"),
                S(OperationType.Maintain, 2, Ids("LT-0004"), reference: "RMA-88213"),
                S(OperationType.Count, 1, Ids("LT-0001", "LT-0003", "MON-0001", "CHR-0002", "PH-0001", "PH-0002"), to: "IT Store", reference: "Weekly IT store count"),
            },
            Reads = { R("IT Smart Cabinet Reader", 1, 5, "PH-0001", "PH-0002"), R("Main Entrance Gate", 2, 2, "LT-0002") },
        },

        // ───────────────────────── Inventory / consumables / pharma / MRO ─────────────────────────
        new Scenario
        {
            Template = "inventory", Site = "City Hospital Pharmacy & Stores", SiteCode = "PHARM", CompanyPrefix = "0614142",
            Locations =
            {
                L("Pharmacy Store", LocationKind.Room), L("Shelf A1", LocationKind.Shelf, "Pharmacy Store"), L("Shelf A2", LocationKind.Shelf, "Pharmacy Store"), L("Fridge 1", LocationKind.Cabinet, "Pharmacy Store", Attr(("cold", true), ("targetTempC", 4))),
                L("Ward 3 Cupboard", LocationKind.Cabinet), L("MRO Store", LocationKind.Room), L("Bin M-01", LocationKind.Bin, "MRO Store"), L("Bin M-02", LocationKind.Bin, "MRO Store"), L("Goods-in", LocationKind.Dock),
            },
            Parties = { P("Ward 3", PartyKind.Department), P("Estates Maintenance", PartyKind.Department), P("MedSupply Ltd", PartyKind.Supplier) },
            Items =
            {
                I("STOCK-LOT", "LOT-2409-A", "Nitrile gloves M", "Shelf A1", qty: 240, lot: "2409-A", expiryDays: 540, attrs: Attr(("sku", "GLV-M"), ("supplier", "MedSupply Ltd"))),
                I("STOCK-LOT", "LOT-2409-B", "Nitrile gloves L", "Shelf A1", qty: 60, lot: "2409-B", expiryDays: 540, attrs: Attr(("sku", "GLV-L"))),
                I("STOCK-LOT", "LOT-2401-B", "Saline 0.9% 500 ml", "Shelf A2", qty: 8, lot: "2401-B", expiryDays: 12, attrs: Attr(("sku", "SAL-500"))),
                I("STOCK-LOT", "LOT-2311-C", "Amoxicillin 500 mg caps", "Shelf A2", qty: 30, lot: "2311-C", expiryDays: -5, attrs: Attr(("sku", "AMX-500"))),
                I("STOCK-LOT", "LOT-2408-V", "Influenza vaccine", "Fridge 1", qty: 45, lot: "2408-V", expiryDays: 90, attrs: Attr(("sku", "FLU-VAX"))),
                I("STOCK-LOT", "LOT-2407-I", "Insulin glargine", "Fridge 1", qty: 12, lot: "2407-I", expiryDays: 200, attrs: Attr(("sku", "INS-GLA"))),
                I("STOCK-LOT", "LOT-2409-W", "Wound dressing 10x10", "Ward 3 Cupboard", qty: 6, lot: "2409-W", expiryDays: 700, attrs: Attr(("sku", "DRS-10"))),
                I("SPARE", "SP-BEAR-6205", "Bearing 6205-2RS", "Bin M-01", qty: 14, attrs: Attr(("partNumber", "6205-2RS"), ("machine", "AHU-3"))),
                I("SPARE", "SP-FILT-HEPA", "HEPA filter H13 610x610", "Bin M-02", qty: 3, attrs: Attr(("partNumber", "H13-610"), ("machine", "AHU-3"))),
                I("SPARE", "SP-BELT-A42", "V-belt A42", "Bin M-01", qty: 9, attrs: Attr(("partNumber", "A42"))),
            },
            Devices = { D("Pharmacy Handheld", DeviceKind.Handheld, "Chainway C72"), D("Smart Shelf A1", DeviceKind.Shelf, "Keonn AdvanReader", null, Ant(1, "Shelf A1"), Ant(2, "Shelf A2")), D("Vaccine Fridge Reader", DeviceKind.Cabinet, "Zebra FX7500", null, Ant(1, "Fridge 1")) },
            History =
            {
                S(OperationType.Receive, 15, Ids("LOT-2409-A", "LOT-2409-B"), to: "Goods-in", reference: "PO-10231"),
                S(OperationType.Transfer, 14.8, Ids("LOT-2409-A", "LOT-2409-B"), to: "Shelf A1", reference: "PO-10231 put-away"),
                S(OperationType.Adjust, 6, Ids("LOT-2409-B"), qty: -40, reference: "Ward issue"),
                S(OperationType.Adjust, 2, Ids("LOT-2401-B"), qty: -4, reference: "Ward 3 issue"),
                S(OperationType.Adjust, 1, Ids("SP-FILT-HEPA"), qty: -2, reference: "WO-5521 AHU-3 filter change"),
                S(OperationType.Count, 0.5, Ids("LOT-2409-A", "LOT-2409-B", "LOT-2401-B", "LOT-2311-C"), to: "Pharmacy Store", reference: "Monthly count"),
            },
            Reads = { R("Smart Shelf A1", 1, 4, "LOT-2409-A", "LOT-2409-B"), R("Smart Shelf A1", 2, 4, "LOT-2401-B", "LOT-2311-C"), R("Vaccine Fridge Reader", 1, 3, "LOT-2408-V", "LOT-2407-I") },
        },

        // ───────────────────────── Warehouse / cross-dock / dock doors / yard / cold store ─────────────────────────
        new Scenario
        {
            Template = "warehouse", Site = "Midlands Distribution Centre", SiteCode = "MDC", CompanyPrefix = "0614143",
            Locations =
            {
                L("Receiving", LocationKind.Zone), L("Dock Door 1", LocationKind.Dock), L("Dock Door 2", LocationKind.Dock), L("Rack A", LocationKind.Rack), L("Rack B", LocationKind.Rack),
                L("Bin A-01-1", LocationKind.Bin, "Rack A"), L("Bin A-01-2", LocationKind.Bin, "Rack A"), L("Bin B-02-1", LocationKind.Bin, "Rack B"),
                L("Staging Lane 1", LocationKind.Zone), L("Cross-dock", LocationKind.Zone), L("Cold Store", LocationKind.Room, null, Attr(("cold", true))), L("Yard", LocationKind.Yard), L("Trailer TR-101", LocationKind.Vehicle, "Yard", mobile: true),
                L("Customer: ACME Ltd", LocationKind.Customer), L("Customer: Northwind Stores", LocationKind.Customer),
            },
            Parties = { P("ACME Ltd", PartyKind.Customer, "ACME"), P("Northwind Stores", PartyKind.Customer, "NWS"), P("Swift Haulage", PartyKind.Supplier) },
            Items =
            {
                I("PALLET", "PLT-0001", "Pallet 0001", "Receiving", "Received"), I("CARTON", "CTN-0001", "Carton 0001 (widgets)", "Receiving", parent: "PLT-0001"), I("CARTON", "CTN-0002", "Carton 0002 (widgets)", "Receiving", parent: "PLT-0001"), I("CARTON", "CTN-0003", "Carton 0003 (gadgets)", "Receiving", parent: "PLT-0001"),
                I("PALLET", "PLT-0002", "Pallet 0002", "Bin A-01-1", "PutAway"), I("CARTON", "CTN-0004", "Carton 0004", "Bin A-01-1", parent: "PLT-0002"), I("CARTON", "CTN-0005", "Carton 0005", "Bin A-01-1", parent: "PLT-0002"),
                I("PALLET", "PLT-0003", "Pallet 0003", "Receiving", "Received"), I("PALLET", "PLT-0004", "Pallet 0004 (frozen)", "Cold Store", "PutAway"),
                I("PALLET", "PLT-0005", "Pallet 0005", "Staging Lane 1", "Staged"), I("CARTON", "CTN-0006", "Carton 0006", "Staging Lane 1", parent: "PLT-0005"),
                I("PALLET", "PLT-0006", "Pallet 0006 (cross-dock)", "Cross-dock", "Received"),
                I("TRAILER", "TR-101", "Trailer TR-101", "Yard", attrs: Attr(("carrier", "Swift Haulage"), ("plate", "AB12 CDE"))),
            },
            Devices =
            {
                D("Dock Door 1 Portal", DeviceKind.Portal, "Zebra FX9600", "demo-portal-token", Ant(1, "Dock Door 1", In), Ant(2, "Dock Door 1", Out)),
                D("Dock Door 2 Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Dock Door 2", In), Ant(2, "Dock Door 2", Out)),
                D("Yard Gate", DeviceKind.Gate, "Impinj R700", null, Ant(1, "Yard", In), Ant(2, "Yard", Out)),
                D("Warehouse Handheld", DeviceKind.Handheld, "Zebra MC3390R"),
            },
            History =
            {
                S(OperationType.Receive, 6, Ids("PLT-0001", "PLT-0006"), to: "Receiving", reference: "ASN-77120"),
                S(OperationType.Pack, 5.9, Ids("CTN-0001", "CTN-0002", "CTN-0003"), container: "PLT-0001"),
                S(OperationType.Transfer, 5, Ids("PLT-0003"), to: "Bin B-02-1", reference: "Put-away"),
                S(OperationType.ProcessStage, 1, Ids("PLT-0005"), state: "Staged", to: "Staging Lane 1", reference: "ORD-5001"),
                S(OperationType.Dispatch, 0.6, Ids("PLT-0005"), to: "Customer: ACME Ltd", party: "ACME Ltd", reference: "ORD-5001"),
            },
            Reads = { R("Dock Door 1 Portal", 2, 14, "PLT-0005"), R("Dock Door 2 Portal", 2, 3, "PLT-0002") },
        },

        // ───────────────────────── Tool crib / aviation tool control (FOD) / calibration ─────────────────────────
        new Scenario
        {
            Template = "tool-tracking", Site = "Hangar 3 – Line Maintenance", SiteCode = "HGR3", CompanyPrefix = "0614144",
            Locations = { L("Tool Crib", LocationKind.Room), L("Bay 1", LocationKind.Zone), L("Bay 2", LocationKind.Zone), L("Calibration Lab", LocationKind.Room), L("Hangar Exit", LocationKind.Gate), L("Tool Cabinet TC-1", LocationKind.Cabinet, "Tool Crib") },
            Parties = { P("Ahmed Khan", PartyKind.Employee, "TECH-01"), P("Lucy Brown", PartyKind.Employee, "TECH-02"), P("Marco Rossi", PartyKind.Employee, "TECH-03"), P("Line Maintenance", PartyKind.Department) },
            Items =
            {
                I("TOOL", "TL-0001", "Makita 18V drill", "Tool Crib", "InCrib", inspectedDaysAgo: 30, attrs: Attr(("calibrated", false))),
                I("TOOL", "TL-0002", "Torque wrench 3/8\" 10–50 Nm", "Bay 1", "CheckedOut", "Marco Rossi", inspectedDaysAgo: 40, dueBackDays: -1, attrs: Attr(("calibrated", true))),
                I("TOOL", "TL-0003", "Fluke 87V multimeter", "Tool Crib", "InCrib", inspectedDaysAgo: 200, attrs: Attr(("calibrated", true))),
                I("TOOL", "TL-0004", "Torque wrench 1/2\" 40–200 Nm", "Tool Crib", "InCrib", inspectedDaysAgo: 10, attrs: Attr(("calibrated", true))),
                I("TOOL", "TL-0005", "Safety wire pliers", "Tool Crib", "InCrib", parent: "KIT-0001"), I("TOOL", "TL-0006", "Inspection mirror", "Tool Crib", "InCrib", parent: "KIT-0001"), I("TOOL", "TL-0007", "Rivet gun", "Tool Crib", "InCrib", parent: "KIT-0001"),
                I("TOOL", "TL-0008", "Borescope", "Calibration Lab", "UnderRepair", inspectedDaysAgo: 370),
                I("TOOL", "TL-0009", "Hydraulic crimper", "Tool Cabinet TC-1", "InCrib", inspectedDaysAgo: 5),
                I("TOOL-KIT", "KIT-0001", "Sheet metal kit A", "Tool Crib", attrs: Attr(("kit", "SM-A"))),
            },
            Devices = { D("Crib Handheld", DeviceKind.Handheld, "Zebra RFD40"), D("Hangar Exit Gate", DeviceKind.Gate, "Impinj R700", null, Ant(1, "Hangar Exit", Out), Ant(2, "Hangar Exit", In)), D("Tool Cabinet TC-1", DeviceKind.Cabinet, "Zebra FX7500", null, Ant(1, "Tool Cabinet TC-1")) },
            History =
            {
                S(OperationType.Issue, 3, Ids("TL-0001"), party: "Ahmed Khan", to: "Bay 2", dueBackDays: 0, reference: "WO-A320-118"),
                S(OperationType.Return, 2.6, Ids("TL-0001"), to: "Tool Crib", reference: "WO-A320-118"),
                S(OperationType.Issue, 1.2, Ids("TL-0004"), party: "Lucy Brown", to: "Bay 1", dueBackDays: 1, reference: "WO-B737-042"),
                S(OperationType.Return, 0.9, Ids("TL-0004"), to: "Tool Crib", reference: "WO-B737-042"),
                S(OperationType.Inspect, 0.8, Ids("TL-0004"), state: "InCrib", reference: "Calibration cert C-2291"),
                S(OperationType.Count, 0.3, Ids("TL-0001", "TL-0003", "TL-0004", "TL-0005", "TL-0006", "TL-0007", "KIT-0001"), to: "Tool Crib", reference: "End of shift count"),
            },
            Reads = { R("Tool Cabinet TC-1", 1, 6, "TL-0009"), R("Hangar Exit Gate", 1, 1.5, "TL-0002") },
        },

        // ───────────────────────── Linen / laundry / uniforms / hospital linen ─────────────────────────
        new Scenario
        {
            Template = "linen-laundry", Site = "St Mary's Hospital Linen Service", SiteCode = "LINEN", CompanyPrefix = "0614145",
            Locations =
            {
                L("Laundry", LocationKind.Building), L("Soiled Holding", LocationKind.Room, "Laundry"), L("Wash Hall", LocationKind.Room, "Laundry"), L("Laundry Tunnel", LocationKind.Zone, "Laundry"), L("Clean Linen Store", LocationKind.Room, "Laundry", Attr(("clean", true))),
                L("Ward 3", LocationKind.Room), L("Ward 4", LocationKind.Room), L("Theatre Changing", LocationKind.Room), L("Uniform Store", LocationKind.Room),
            },
            Parties = { P("Housekeeping", PartyKind.Department), P("Ward 3 Nursing", PartyKind.Department), P("Maria Gomez", PartyKind.Employee, "HK-01"), P("Dr Chen", PartyKind.Employee, "DR-14") },
            Items =
            {
                I("LINEN", "SHT-0001", "Bed sheet king", "Clean Linen Store", "Clean", cycles: 120, attrs: Attr(("size", "King"), ("colour", "White"))),
                I("LINEN", "SHT-0002", "Bed sheet king", "Clean Linen Store", "Clean", cycles: 199, attrs: Attr(("size", "King"), ("colour", "White"))),
                I("LINEN", "SHT-0003", "Bed sheet single", "Ward 3", "Issued", "Ward 3 Nursing", cycles: 45, attrs: Attr(("size", "Single"))),
                I("LINEN", "SHT-0004", "Bed sheet single", "Soiled Holding", "Soiled", cycles: 88, attrs: Attr(("size", "Single"))),
                I("LINEN", "GWN-0001", "Patient gown", "Ward 4", "Issued", "Housekeeping", cycles: 30), I("LINEN", "GWN-0002", "Patient gown", "Wash Hall", "InWash", cycles: 61), I("LINEN", "GWN-0003", "Patient gown", "Soiled Holding", "Contaminated", cycles: 12),
                I("LINEN", "TWL-0001", "Bath towel", "Clean Linen Store", "Clean", cycles: 150), I("LINEN", "TWL-0002", "Bath towel", "Clean Linen Store", "Clean", cycles: 151), I("LINEN", "BLK-0001", "Blanket", "Clean Linen Store", "Clean", cycles: 20),
                I("UNIFORM", "UNI-0001", "Scrub top M", "Uniform Store", "Stock", cycles: 40, attrs: Attr(("size", "M"), ("garmentType", "Scrub top"))),
                I("UNIFORM", "UNI-0002", "Scrub top L", "Theatre Changing", "Issued", "Dr Chen", cycles: 72, attrs: Attr(("size", "L"), ("employee", "Dr Chen"))),
                I("UNIFORM", "UNI-0003", "Scrub trousers L", "Theatre Changing", "Issued", "Dr Chen", cycles: 70, attrs: Attr(("size", "L"), ("employee", "Dr Chen"))),
                I("LAUNDRY-CAGE", "CAGE-01", "Laundry cage 01", "Soiled Holding"),
            },
            Devices = { D("Laundry Tunnel Reader", DeviceKind.Tunnel, "Impinj R700 tunnel", null, Ant(1, "Laundry Tunnel")), D("Clean Store Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Clean Linen Store", In), Ant(2, "Clean Linen Store", Out)), D("Linen Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Issue, 4, Ids("SHT-0001"), party: "Ward 3 Nursing", to: "Ward 3"),
                S(OperationType.Return, 2.5, Ids("SHT-0001"), to: "Soiled Holding"),
                S(OperationType.Pack, 2.4, Ids("SHT-0001", "SHT-0004"), container: "CAGE-01"),
                S(OperationType.ProcessStage, 2, Ids("SHT-0001", "SHT-0004"), state: "InWash", to: "Wash Hall", reference: "Wash batch 2041"),
                S(OperationType.ProcessStage, 1.8, Ids("SHT-0001"), state: "Clean", to: "Clean Linen Store", reference: "Wash batch 2041"),
                S(OperationType.Issue, 1.5, Ids("SHT-0002"), party: "Ward 3 Nursing", to: "Ward 3"),
                S(OperationType.Return, 1.0, Ids("SHT-0002"), to: "Soiled Holding"),
                S(OperationType.ProcessStage, 0.8, Ids("SHT-0002"), state: "InWash", to: "Wash Hall", reference: "Wash batch 2042"),
                S(OperationType.ProcessStage, 0.6, Ids("SHT-0002"), state: "Clean", to: "Clean Linen Store", reference: "Wash batch 2042"),
                S(OperationType.Issue, 1.2, Ids("UNI-0001"), party: "Maria Gomez", to: "Theatre Changing"),
                S(OperationType.Return, 0.4, Ids("UNI-0001"), to: "Soiled Holding"),
            },
            Reads = { R("Laundry Tunnel Reader", 1, 40, "GWN-0002"), R("Clean Store Portal", 1, 2, "GWN-0003") },
        },

        // ───────────────────────── PPE / inspections / gas cylinders ─────────────────────────
        new Scenario
        {
            Template = "ppe-inspection", Site = "Industrial Gases & Safety Depot", SiteCode = "GAS", CompanyPrefix = "0614146",
            Locations = { L("PPE Store", LocationKind.Room), L("Inspection Bay", LocationKind.Room), L("Filling Bay", LocationKind.Zone), L("Full Cylinder Yard", LocationKind.Yard), L("Empty Cylinder Yard", LocationKind.Yard), L("Depot Gate", LocationKind.Gate), L("Customer: BuildRight Construction", LocationKind.Customer), L("Customer: Riverside Hospital", LocationKind.Customer) },
            Parties = { P("Tom Evans", PartyKind.Employee, "OP-01"), P("Aisha Bello", PartyKind.Employee, "OP-02"), P("BuildRight Construction", PartyKind.Customer), P("Riverside Hospital", PartyKind.Customer) },
            Items =
            {
                I("PPE", "HAR-0001", "Fall-arrest harness", "PPE Store", "InService", expiryDays: 400, inspectedDaysAgo: 30, attrs: Attr(("standard", "EN 361"), ("size", "M"))),
                I("PPE", "HAR-0002", "Fall-arrest harness", "PPE Store", "InService", expiryDays: -20, inspectedDaysAgo: 100, attrs: Attr(("standard", "EN 361"), ("size", "L"))),
                I("PPE", "HAR-0003", "Fall-arrest harness", "PPE Store", "Failed", expiryDays: 300, inspectedDaysAgo: 2, attrs: Attr(("standard", "EN 361"), ("size", "M"))),
                I("PPE", "HLM-0001", "Safety helmet", "PPE Store", "Issued", "Tom Evans", expiryDays: 900, inspectedDaysAgo: 200, attrs: Attr(("standard", "EN 397"))),
                I("PPE", "HLM-0002", "Safety helmet", "PPE Store", "InService", expiryDays: 900, inspectedDaysAgo: 10),
                I("PPE", "RES-0001", "Half-mask respirator", "PPE Store", "InService", expiryDays: 60, inspectedDaysAgo: 190, attrs: Attr(("standard", "EN 140"))),
                I("CYLINDER", "CYL-0001", "Oxygen cylinder 50 L", "Full Cylinder Yard", "Filled", inspectedDaysAgo: 400, attrs: Attr(("gas", "O2"), ("capacityL", 50))),
                I("CYLINDER", "CYL-0002", "Oxygen cylinder 50 L", "Customer: Riverside Hospital", "AtCustomer", "Riverside Hospital", inspectedDaysAgo: 900, dueBackDays: 20, attrs: Attr(("gas", "O2"), ("capacityL", 50))),
                I("CYLINDER", "CYL-0003", "Argon cylinder 50 L", "Customer: BuildRight Construction", "AtCustomer", "BuildRight Construction", inspectedDaysAgo: 1900, dueBackDays: -6, attrs: Attr(("gas", "Ar"), ("capacityL", 50))),
                I("CYLINDER", "CYL-0004", "Acetylene cylinder 40 L", "Customer: BuildRight Construction", "AtCustomer", "BuildRight Construction", inspectedDaysAgo: 300, attrs: Attr(("gas", "C2H2"), ("capacityL", 40))),
                I("CYLINDER", "CYL-0005", "Argon cylinder 50 L", "Filling Bay", "Empty", inspectedDaysAgo: 120, attrs: Attr(("gas", "Ar"), ("capacityL", 50))),
            },
            Devices = { D("Depot Gate Reader", DeviceKind.Gate, "Impinj R700", null, Ant(1, "Depot Gate", Out), Ant(2, "Depot Gate", In)), D("Depot Handheld", DeviceKind.Handheld, "Chainway C72") },
            History =
            {
                S(OperationType.Inspect, 2, Ids("HAR-0003"), state: "Failed", reference: "Frayed webbing"),
                S(OperationType.Inspect, 1.5, Ids("HLM-0002", "HAR-0001"), state: "InService", reference: "6-monthly"),
                S(OperationType.Issue, 1.4, Ids("HAR-0002"), party: "Aisha Bello", reference: "Site: Tower crane"),
                S(OperationType.Return, 1.3, Ids("HAR-0002"), to: "PPE Store"),
                S(OperationType.ProcessStage, 1, Ids("CYL-0005"), state: "Filled", to: "Full Cylinder Yard", reference: "Fill batch F-311"),
                S(OperationType.Dispatch, 0.7, Ids("CYL-0005"), to: "Customer: BuildRight Construction", party: "BuildRight Construction", dueBackDays: 30, reference: "DN-8812"),
                S(OperationType.Return, 0.5, Ids("CYL-0004"), to: "Empty Cylinder Yard", reference: "DN-8790 return"),
            },
            Reads = { R("Depot Gate Reader", 2, 5, "CYL-0004") },
        },

        // ───────────────────────── Medical devices / surgical trays / CSSD ─────────────────────────
        new Scenario
        {
            Template = "medical-assets", Site = "General Hospital – Equipment Library & CSSD", SiteCode = "MED", CompanyPrefix = "0614147",
            Locations = { L("Equipment Library", LocationKind.Room), L("Ward 7", LocationKind.Room), L("ICU", LocationKind.Room, null, Attr(("clean", true))), L("ICU Entrance", LocationKind.Gate, "ICU"), L("Decontamination", LocationKind.Room), L("Sterile Store", LocationKind.Room, null, Attr(("clean", true))), L("Theatre 2", LocationKind.Room), L("Biomed Workshop", LocationKind.Room) },
            Parties = { P("Ward 7 Nursing", PartyKind.Department), P("ICU", PartyKind.Department), P("Theatre 2 Team", PartyKind.Department), P("Nurse Okafor", PartyKind.Employee, "RN-22") },
            Items =
            {
                I("MED-DEVICE", "PMP-0001", "Infusion pump", "Equipment Library", "Clean", inspectedDaysAgo: 100, attrs: Attr(("deviceClass", "IIb"))), I("MED-DEVICE", "PMP-0002", "Infusion pump", "Ward 7", "InUse", "Ward 7 Nursing", inspectedDaysAgo: 200, attrs: Attr(("deviceClass", "IIb"))),
                I("MED-DEVICE", "PMP-0003", "Infusion pump", "Decontamination", "Dirty", inspectedDaysAgo: 50), I("MED-DEVICE", "PMP-0004", "Syringe driver", "Equipment Library", "Clean", inspectedDaysAgo: 380),
                I("MED-DEVICE", "WCH-0001", "Wheelchair", "Ward 7", "InUse", "Ward 7 Nursing"), I("MED-DEVICE", "WCH-0002", "Wheelchair", "Equipment Library", "Clean"),
                I("MED-DEVICE", "PMON-0001", "Patient monitor", "ICU", "InUse", "ICU", inspectedDaysAgo: 30), I("MED-DEVICE", "PMON-0002", "Patient monitor", "Biomed Workshop", "UnderMaintenance", inspectedDaysAgo: 400), I("MED-DEVICE", "MAT-0001", "Pressure-relief mattress", "Decontamination", "Cleaning"),
                I("TRAY", "TRAY-ORTHO-1", "Orthopaedic tray 1", "Sterile Store", "Sterilised", cycles: 210), I("INSTRUMENT", "INS-0001", "Bone curette", "Sterile Store", parent: "TRAY-ORTHO-1", cycles: 210), I("INSTRUMENT", "INS-0002", "Osteotome 10 mm", "Sterile Store", parent: "TRAY-ORTHO-1", cycles: 210), I("INSTRUMENT", "INS-0003", "Mallet", "Sterile Store", parent: "TRAY-ORTHO-1", cycles: 495),
                I("TRAY", "TRAY-GEN-2", "General surgery tray 2", "Sterile Store", "Sterilised", cycles: 87), I("INSTRUMENT", "INS-0004", "Scissors Mayo", "Sterile Store", parent: "TRAY-GEN-2", cycles: 88), I("INSTRUMENT", "INS-0005", "Forceps Adson", "Sterile Store", parent: "TRAY-GEN-2", cycles: 88),
            },
            Devices = { D("ICU Entrance Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "ICU Entrance", In), Ant(2, "ICU Entrance", Out)), D("Equipment Library Reader", DeviceKind.Fixed, "Impinj R700", null, Ant(1, "Equipment Library")), D("CSSD Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Issue, 3, Ids("PMP-0001"), party: "Ward 7 Nursing", to: "Ward 7"),
                S(OperationType.Return, 1.5, Ids("PMP-0001"), to: "Decontamination"),
                S(OperationType.ProcessStage, 1.2, Ids("PMP-0001"), state: "Cleaning"), S(OperationType.ProcessStage, 1.0, Ids("PMP-0001"), state: "Clean", to: "Equipment Library"),
                S(OperationType.Issue, 2.5, Ids("TRAY-GEN-2"), party: "Theatre 2 Team", to: "Theatre 2", reference: "Case 2211"),
                S(OperationType.Return, 2.2, Ids("TRAY-GEN-2"), to: "Decontamination", reference: "Case 2211"),
                S(OperationType.ProcessStage, 0.9, Ids("TRAY-GEN-2"), state: "Decontaminated"), S(OperationType.ProcessStage, 0.7, Ids("TRAY-GEN-2"), state: "Sterilised", to: "Sterile Store", reference: "Autoclave load 118"),
                S(OperationType.Maintain, 2, Ids("PMON-0002"), to: "Biomed Workshop", reference: "PPM overdue"),
            },
            Reads = { R("Equipment Library Reader", 1, 8, "PMP-0001", "PMP-0004", "WCH-0002"), R("ICU Entrance Portal", 1, 2, "PMP-0003") },
        },

        // ───────────────────────── Evidence / lab samples / documents & files ─────────────────────────
        new Scenario
        {
            Template = "chain-of-custody", Site = "County Police Evidence Store & Forensic Lab", SiteCode = "EVID", CompanyPrefix = "0614148",
            Locations = { L("Evidence Intake", LocationKind.Room), L("Evidence Vault", LocationKind.Room, null, Attr(("restricted", true))), L("Vault Exit", LocationKind.Gate, "Evidence Vault"), L("Forensic Lab", LocationKind.Room), L("Sample Fridge", LocationKind.Cabinet, "Forensic Lab", Attr(("cold", true))), L("Records Archive", LocationKind.Room), L("Court Liaison", LocationKind.External) },
            Parties = { P("DC Harper", PartyKind.Employee, "PC-1187"), P("Sgt Williams", PartyKind.Employee, "PS-0421"), P("Dr Osei (forensics)", PartyKind.Employee, "FS-09"), P("Crown Court", PartyKind.Other) },
            Items =
            {
                I("EVIDENCE", "EV-24-0311-01", "Mobile phone (Samsung)", "Evidence Intake", "Collected", attrs: Attr(("caseNumber", "CR/24/0311"), ("collectedBy", "DC Harper"), ("sealNumber", "S-22981"))),
                I("EVIDENCE", "EV-24-0311-02", "Kitchen knife", "Evidence Intake", "Collected", attrs: Attr(("caseNumber", "CR/24/0311"), ("sealNumber", "S-22982"))),
                I("EVIDENCE", "EV-24-0402-01", "Laptop (Dell)", "Forensic Lab", "CheckedOut", "Dr Osei (forensics)", dueBackDays: 3, attrs: Attr(("caseNumber", "CR/24/0402"), ("sealNumber", "S-23010"))),
                I("EVIDENCE", "EV-24-0198-01", "Cash (sealed bag)", "Evidence Vault", "Stored", attrs: Attr(("caseNumber", "CR/24/0198"), ("sealNumber", "S-21877"))),
                I("EVIDENCE", "EV-23-1120-01", "Clothing (jacket)", "Evidence Intake", "Collected", attrs: Attr(("caseNumber", "CR/23/1120"))),
                I("SAMPLE", "SMP-0001", "Blood sample", "Sample Fridge", "Stored", expiryDays: 30, attrs: Attr(("patientRef", "P-4471"), ("sampleType", "Blood"))),
                I("SAMPLE", "SMP-0002", "DNA swab", "Evidence Intake", "Collected", expiryDays: 180, attrs: Attr(("sampleType", "Swab"))),
                I("SAMPLE", "SMP-0003", "Urine sample", "Evidence Intake", "Collected", expiryDays: 7),
                I("FILE", "FILE-0001", "Case file CR/24/0311", "Records Archive", "Archived", attrs: Attr(("fileNumber", "CR/24/0311"), ("department", "CID"))),
                I("FILE", "FILE-0002", "Case file CR/23/0877", "Records Archive", "Archived", attrs: Attr(("fileNumber", "CR/23/0877"), ("department", "CID"))),
            },
            Devices = { D("Vault Exit Reader", DeviceKind.Gate, "Impinj R700", null, Ant(1, "Vault Exit", Out), Ant(2, "Vault Exit", In)), D("Evidence Handheld", DeviceKind.Handheld, "Zebra RFD40"), D("Sample Fridge Reader", DeviceKind.Cabinet, "Zebra FX7500", null, Ant(1, "Sample Fridge")) },
            History =
            {
                S(OperationType.Receive, 30, Ids("EV-24-0311-01", "EV-24-0311-02"), to: "Evidence Vault", reference: "CR/24/0311 booking-in"),
                S(OperationType.Issue, 8, Ids("EV-24-0311-01"), party: "Dr Osei (forensics)", to: "Forensic Lab", dueBackDays: 5, reference: "Forensic examination"),
                S(OperationType.Return, 4, Ids("EV-24-0311-01"), to: "Evidence Vault", reference: "Examination complete"),
                S(OperationType.Receive, 2, Ids("SMP-0002"), to: "Forensic Lab"), S(OperationType.ProcessStage, 1.5, Ids("SMP-0002"), state: "Processing"),
                S(OperationType.Issue, 20, Ids("FILE-0002"), party: "Sgt Williams", to: "Court Liaison", dueBackDays: 16, reference: "Crown Court hearing"),
                S(OperationType.Count, 0.5, Ids("EV-24-0311-01", "EV-24-0311-02", "EV-24-0198-01"), to: "Evidence Vault", reference: "Vault audit"),
            },
            Reads = { R("Sample Fridge Reader", 1, 3, "SMP-0001"), R("Vault Exit Reader", 1, 1, "EV-24-0311-02") },
        },

        // ───────────────────────── Library ─────────────────────────
        new Scenario
        {
            Template = "library", Site = "City Central Library", SiteCode = "LIB", CompanyPrefix = "0614149",
            Locations = { L("Ground Floor", LocationKind.Floor), L("Fiction", LocationKind.Zone, "Ground Floor"), L("Non-fiction", LocationKind.Zone, "Ground Floor"), L("Children's", LocationKind.Zone, "Ground Floor"), L("Returns Bin", LocationKind.Bin, "Ground Floor"), L("Exit Gate", LocationKind.Gate, "Ground Floor"), L("Self-service Kiosk", LocationKind.Zone, "Ground Floor"), L("On loan", LocationKind.External) },
            Parties = { P("Amelia Clarke", PartyKind.Person, "M-10021"), P("Ravi Shah", PartyKind.Person, "M-10388"), P("Grace Ndlovu", PartyKind.Person, "M-11002") },
            Items =
            {
                I("BOOK", "BK-0001", "Dune", "Fiction", "OnShelf", attrs: Attr(("isbn", "9780441172719"), ("author", "Frank Herbert"), ("title", "Dune"), ("shelfMark", "F HER"))),
                I("BOOK", "BK-0002", "The Left Hand of Darkness", "Fiction", "OnShelf", attrs: Attr(("author", "Ursula K. Le Guin"), ("title", "The Left Hand of Darkness"), ("shelfMark", "F LEG"))),
                I("BOOK", "BK-0003", "Sapiens", "Non-fiction", "OnShelf", attrs: Attr(("author", "Yuval Noah Harari"), ("title", "Sapiens"), ("shelfMark", "909 HAR"))),
                I("BOOK", "BK-0004", "A Brief History of Time", "Non-fiction", "OnShelf", attrs: Attr(("author", "Stephen Hawking"), ("title", "A Brief History of Time"), ("shelfMark", "523.1 HAW"))),
                I("BOOK", "BK-0005", "The Gruffalo", "Children's", "OnShelf", attrs: Attr(("author", "Julia Donaldson"), ("title", "The Gruffalo"))),
                I("BOOK", "BK-0006", "Matilda", "Children's", "OnShelf", attrs: Attr(("author", "Roald Dahl"), ("title", "Matilda"))),
                I("BOOK", "BK-0007", "Educated", "Returns Bin", "OnShelf", attrs: Attr(("author", "Tara Westover"), ("title", "Educated"))),
                I("BOOK", "BK-0008", "Project Hail Mary", "Fiction", "OnShelf", attrs: Attr(("author", "Andy Weir"), ("title", "Project Hail Mary"))),
                I("BOOK", "DVD-0001", "Spirited Away (DVD)", "Children's", "OnShelf", attrs: Attr(("title", "Spirited Away"))),
            },
            Devices = { D("Exit Gate", DeviceKind.Gate, "Bibliotheca gate", null, Ant(1, "Exit Gate", Out), Ant(2, "Exit Gate", In)), D("Self-service Kiosk", DeviceKind.Fixed, "Bibliotheca selfCheck", null, Ant(1, "Self-service Kiosk")), D("Shelf-reading Wand", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Issue, 24, Ids("BK-0003"), party: "Ravi Shah", to: "On loan", dueBackDays: 21, reference: "Self-service"),
                S(OperationType.Issue, 9, Ids("BK-0006"), party: "Grace Ndlovu", to: "On loan", dueBackDays: 21, reference: "Self-service"),
                S(OperationType.Issue, 15, Ids("BK-0007"), party: "Amelia Clarke", to: "On loan", dueBackDays: 21), S(OperationType.Return, 0.5, Ids("BK-0007"), to: "Returns Bin"),
                S(OperationType.Count, 0.2, Ids("BK-0001", "BK-0002", "BK-0008"), to: "Fiction", reference: "Shelf reading"),
            },
            Reads = { R("Self-service Kiosk", 1, 12, "BK-0007"), R("Exit Gate", 1, 1, "BK-0004") },
        },

        // ───────────────────────── Retail / apparel / fitting room / loss prevention / jewellery ─────────────────────────
        new Scenario
        {
            Template = "retail", Site = "Flagship Store – High Street", SiteCode = "STORE1", CompanyPrefix = "0614150",
            Locations = { L("Back Room", LocationKind.Room), L("Sales Floor", LocationKind.Zone), L("Womenswear", LocationKind.Zone, "Sales Floor"), L("Menswear", LocationKind.Zone, "Sales Floor"), L("Fitting Room 1", LocationKind.Room, "Sales Floor"), L("Jewellery Counter", LocationKind.Zone, "Sales Floor"), L("Safe", LocationKind.Cabinet, "Back Room"), L("POS", LocationKind.Zone, "Sales Floor"), L("Store Exit", LocationKind.Gate), L("Sold to customer", LocationKind.External) },
            Parties = { P("Walk-in customer", PartyKind.Customer, "WALKIN"), P("Store Manager", PartyKind.Employee) },
            Items =
            {
                I("SKU-ITEM", "GAR-0001", "Linen shirt – white / M", "Menswear", "SalesFloor", attrs: Attr(("sku", "SH-LIN-WHT"), ("size", "M"), ("colour", "White"), ("price", 49))),
                I("SKU-ITEM", "GAR-0002", "Linen shirt – white / L", "Menswear", "SalesFloor", attrs: Attr(("sku", "SH-LIN-WHT"), ("size", "L"), ("colour", "White"), ("price", 49))),
                I("SKU-ITEM", "GAR-0003", "Linen shirt – white / XL", "Back Room", "BackRoom", attrs: Attr(("sku", "SH-LIN-WHT"), ("size", "XL"), ("colour", "White"), ("price", 49))),
                I("SKU-ITEM", "GAR-0004", "Denim jacket – blue / S", "Womenswear", "SalesFloor", attrs: Attr(("sku", "JK-DEN-BLU"), ("size", "S"), ("colour", "Blue"), ("price", 89))),
                I("SKU-ITEM", "GAR-0005", "Denim jacket – blue / M", "Fitting Room 1", "FittingRoom", attrs: Attr(("sku", "JK-DEN-BLU"), ("size", "M"), ("colour", "Blue"), ("price", 89))),
                I("SKU-ITEM", "GAR-0006", "Denim jacket – blue / L", "Back Room", "BackRoom", attrs: Attr(("sku", "JK-DEN-BLU"), ("size", "L"), ("colour", "Blue"), ("price", 89))),
                I("SKU-ITEM", "GAR-0007", "Wool coat – camel / M", "Womenswear", "SalesFloor", attrs: Attr(("sku", "CT-WOL-CAM"), ("size", "M"), ("colour", "Camel"), ("price", 199))),
                I("SKU-ITEM", "GAR-0008", "Wool coat – camel / M", "Sold to customer", "Sold", attrs: Attr(("sku", "CT-WOL-CAM"), ("size", "M"), ("colour", "Camel"), ("price", 199))),
                I("SKU-ITEM", "SHO-0001", "Trainers – 42", "Menswear", "SalesFloor", attrs: Attr(("sku", "TR-RUN-42"), ("size", "42"), ("price", 120))),
                I("SKU-ITEM", "SHO-0002", "Trainers – 43", "Back Room", "BackRoom", attrs: Attr(("sku", "TR-RUN-43"), ("size", "43"), ("price", 120))),
                I("HV-ITEM", "JW-0001", "Automatic watch – steel", "Jewellery Counter", "OnDisplay", attrs: Attr(("sku", "W-AUTO-ST"), ("valuation", 2400))),
                I("HV-ITEM", "JW-0002", "Diamond ring 0.5 ct", "Safe", "InSafe", attrs: Attr(("sku", "R-DIA-05"), ("carat", "0.5"), ("valuation", 3900))),
                I("HV-ITEM", "JW-0003", "Gold bracelet", "Jewellery Counter", "OnDisplay", attrs: Attr(("sku", "B-GLD-18"), ("valuation", 1250))),
            },
            Devices =
            {
                D("Store Exit Gate", DeviceKind.Gate, "Nedap iD Gate", null, Ant(1, "Store Exit", Out), Ant(2, "Store Exit", In)),
                D("Fitting Room 1 Reader", DeviceKind.Fixed, "Impinj R700", null, Ant(1, "Fitting Room 1")),
                D("Jewellery Smart Counter", DeviceKind.Shelf, "Keonn AdvanReader", null, Ant(1, "Jewellery Counter")),
                D("Store Handheld", DeviceKind.Handheld, "Zebra RFD40"),
            },
            History =
            {
                S(OperationType.Receive, 7, Ids("GAR-0003", "GAR-0006", "SHO-0002"), to: "Back Room", reference: "Delivery 88-1002"),
                S(OperationType.Transfer, 5, Ids("GAR-0001", "GAR-0002", "SHO-0001"), to: "Menswear", reference: "Replenishment"),
                S(OperationType.Dispatch, 1, Ids("GAR-0008"), to: "Sold to customer", party: "Walk-in customer", reference: "Receipt 000451"),
                S(OperationType.Transfer, 0.3, Ids("JW-0001"), to: "Jewellery Counter", reference: "Morning display"),
                S(OperationType.Count, 0.2, Ids("GAR-0001", "GAR-0002", "GAR-0004", "GAR-0007", "SHO-0001", "JW-0001", "JW-0003"), to: "Sales Floor", reference: "Opening count"),
            },
            Reads = { R("Fitting Room 1 Reader", 1, 3, "GAR-0005"), R("Jewellery Smart Counter", 1, 2, "JW-0001", "JW-0003"), R("Store Exit Gate", 1, 1, "GAR-0004"), R("Store Exit Gate", 1, 22, "GAR-0008") },
        },

        // ───────────────────────── Returnables / kegs / gas cylinders / rental / events ─────────────────────────
        new Scenario
        {
            Template = "returnable-assets", Site = "Ridgeway Brewery & Plant Hire", SiteCode = "RIDGE", CompanyPrefix = "0614151",
            Locations = { L("Keg Filling Hall", LocationKind.Zone), L("Full Keg Store", LocationKind.Room), L("Empty Keg Yard", LocationKind.Yard), L("Crate Pool Yard", LocationKind.Yard), L("Hire Depot", LocationKind.Room), L("Workshop", LocationKind.Room), L("Despatch Bay", LocationKind.Dock), L("Customer: The Crown", LocationKind.Customer), L("Customer: Metro Distribution", LocationKind.Customer), L("Customer: Acme Events", LocationKind.Customer) },
            Parties = { P("The Crown (pub)", PartyKind.Customer, "CROWN"), P("Metro Distribution", PartyKind.Customer, "METRO"), P("Acme Events", PartyKind.Customer, "ACMEEV") },
            Items =
            {
                I("KEG", "KEG-0001", "Keg 50 L", "Full Keg Store", "Filled", cycles: 41, attrs: Attr(("beer", "Ridgeway Pale"), ("litres", 50))),
                I("KEG", "KEG-0002", "Keg 50 L", "Customer: The Crown", "AtCustomer", "The Crown (pub)", cycles: 37, dueBackDays: -9, attrs: Attr(("beer", "Ridgeway Stout"), ("litres", 50))),
                I("KEG", "KEG-0003", "Keg 30 L", "Customer: Metro Distribution", "AtDistributor", "Metro Distribution", cycles: 12, dueBackDays: 20, attrs: Attr(("beer", "Ridgeway Pale"), ("litres", 30))),
                I("KEG", "KEG-0004", "Keg 50 L", "Empty Keg Yard", "Empty", cycles: 60), I("KEG", "KEG-0005", "Keg 50 L", "Empty Keg Yard", "Empty", cycles: 19), I("KEG", "KEG-0006", "Keg 30 L", "Keg Filling Hall", "Empty", cycles: 8),
                I("RTI", "CRT-0001", "Plastic crate 600x400", "Crate Pool Yard", "InPool", attrs: Attr(("owner", "Ridgeway"), ("deposit", 12))), I("RTI", "CRT-0002", "Plastic crate 600x400", "Customer: Metro Distribution", "AtCustomer", "Metro Distribution", dueBackDays: 10, attrs: Attr(("owner", "Ridgeway"), ("deposit", 12))),
                I("RTI", "CRT-0003", "Plastic crate 600x400", "Workshop", "Damaged", attrs: Attr(("owner", "Ridgeway"))), I("RTI", "PAL-0001", "Euro pallet", "Crate Pool Yard", "InPool"),
                I("RENTAL", "HIRE-0001", "Pressure washer 200 bar", "Hire Depot", "Available", inspectedDaysAgo: 20, attrs: Attr(("dailyRate", 45), ("category", "Cleaning"))),
                I("RENTAL", "HIRE-0002", "Generator 6 kVA", "Customer: Acme Events", "Rented", "Acme Events", inspectedDaysAgo: 60, dueBackDays: -2, attrs: Attr(("dailyRate", 80), ("category", "Power"))),
                I("RENTAL", "HIRE-0003", "Mini digger 1.5 t", "Workshop", "Maintenance", inspectedDaysAgo: 95, attrs: Attr(("dailyRate", 180), ("category", "Plant"))),
            },
            Devices = { D("Despatch Bay Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Despatch Bay", Out), Ant(2, "Despatch Bay", In)), D("Yard Handheld", DeviceKind.Handheld, "Chainway C72") },
            History =
            {
                S(OperationType.ProcessStage, 5, Ids("KEG-0004"), state: "Filled", to: "Full Keg Store", reference: "Brew 2024-31"),
                S(OperationType.Dispatch, 4, Ids("KEG-0004"), to: "Customer: Metro Distribution", party: "Metro Distribution", dueBackDays: 45, reference: "DN-3301"),
                S(OperationType.Dispatch, 3, Ids("KEG-0004"), to: "Customer: The Crown", party: "The Crown (pub)", dueBackDays: 30, reference: "DN-3312"),
                S(OperationType.Return, 0.5, Ids("KEG-0004"), to: "Empty Keg Yard", reference: "Collection run 12"),
                S(OperationType.Dispatch, 6, Ids("HIRE-0001"), to: "Customer: Acme Events", party: "Acme Events", dueBackDays: 3, reference: "HIRE-7710"),
                S(OperationType.Return, 2, Ids("HIRE-0001"), to: "Hire Depot", reference: "HIRE-7710 off-hire"), S(OperationType.Inspect, 1.9, Ids("HIRE-0001"), state: "Available", reference: "Post-hire check"),
                S(OperationType.Inspect, 1, Ids("CRT-0003"), state: "Damaged", reference: "Cracked base"),
            },
            Reads = { R("Despatch Bay Portal", 2, 11, "KEG-0004"), R("Despatch Bay Portal", 2, 1, "KEG-0002") },
        },

        // ───────────────────────── Manufacturing WIP / kanban / raw material / QC / kitting ─────────────────────────
        new Scenario
        {
            Template = "manufacturing", Site = "Plant 2 – Gearbox Line", SiteCode = "PLANT2", CompanyPrefix = "0614152",
            Locations = { L("Raw Material Store", LocationKind.Room), L("Machining Cell 1", LocationKind.Zone), L("Assembly Station A", LocationKind.Zone), L("Test Bench", LocationKind.Zone), L("Finished Goods", LocationKind.Room), L("Quarantine Cage", LocationKind.Cabinet, null, Attr(("quarantine", true))), L("Kanban Return Point", LocationKind.Zone, null, Attr(("kanban", true))), L("Line-side Rack", LocationKind.Rack, "Assembly Station A"), L("Kitting Area", LocationKind.Zone) },
            Parties = { P("Shift A", PartyKind.Department), P("Quality", PartyKind.Department) },
            Items =
            {
                I("WIP", "WO-7781-01", "Gearbox housing #1", "Machining Cell 1", "Machining", attrs: Attr(("workOrder", "WO-7781"), ("routing", "R-GB-01"))),
                I("WIP", "WO-7781-02", "Gearbox housing #2", "Raw Material Store", "Queued", attrs: Attr(("workOrder", "WO-7781"), ("routing", "R-GB-01"))),
                I("WIP", "WO-7781-03", "Gearbox housing #3", "Raw Material Store", "Queued", attrs: Attr(("workOrder", "WO-7781"))),
                I("WIP", "WO-7780-05", "Gearbox assembly #5", "Finished Goods", "Complete", attrs: Attr(("workOrder", "WO-7780"))),
                I("WIP", "WO-7780-06", "Gearbox assembly #6", "Quarantine Cage", "Quarantined", attrs: Attr(("workOrder", "WO-7780"), ("ncr", "NCR-118"))),
                I("WIP", "WO-7782-01", "Gearbox housing #1", "Raw Material Store", "Queued", attrs: Attr(("workOrder", "WO-7782"))),
                I("KANBAN-BIN", "BIN-M8-01", "Kanban bin M8 bolts", "Line-side Rack", "InUse", qty: 120, attrs: Attr(("partNumber", "BLT-M8-40"), ("station", "Assembly A"))),
                I("KANBAN-BIN", "BIN-M8-02", "Kanban bin M8 bolts", "Kanban Return Point", "Empty", qty: 0, attrs: Attr(("partNumber", "BLT-M8-40"), ("station", "Assembly A"))),
                I("KANBAN-BIN", "BIN-BRG-01", "Kanban bin bearings", "Line-side Rack", "Full", qty: 40, attrs: Attr(("partNumber", "BRG-6204"), ("station", "Assembly A"))),
                I("KIT", "KIT-WO-7781", "Kit for WO-7781", "Kitting Area", attrs: Attr(("bom", "BOM-GB-01"))),
                I("RAW", "COIL-0001", "Steel coil S355", "Raw Material Store", qty: 1850, attrs: Attr(("material", "S355"), ("supplierLot", "SL-2201"))),
                I("RAW", "COIL-0002", "Aluminium billet 6082", "Raw Material Store", qty: 420, attrs: Attr(("material", "6082"), ("supplierLot", "SL-2210"))),
            },
            Devices = { D("Kanban Return Point Reader", DeviceKind.Fixed, "Impinj R700", null, Ant(1, "Kanban Return Point")), D("Assembly A Station Reader", DeviceKind.Fixed, "Zebra FX7500", null, Ant(1, "Assembly Station A")), D("Line Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.ProcessStage, 3, Ids("WO-7781-02"), state: "Machining", to: "Machining Cell 1", reference: "WO-7781"),
                S(OperationType.ProcessStage, 2, Ids("WO-7781-02"), state: "Assembly", to: "Assembly Station A", reference: "WO-7781"),
                S(OperationType.ProcessStage, 2.5, Ids("WO-7781-03"), state: "Machining", to: "Machining Cell 1"), S(OperationType.ProcessStage, 1.5, Ids("WO-7781-03"), state: "Assembly", to: "Assembly Station A"), S(OperationType.ProcessStage, 0.8, Ids("WO-7781-03"), state: "Testing", to: "Test Bench"),
                S(OperationType.Inspect, 1, Ids("WO-7780-06"), state: "Quarantined", to: "Quarantine Cage", reference: "NCR-118 backlash out of tolerance"),
                S(OperationType.Adjust, 0.9, Ids("BIN-M8-01"), qty: -30, reference: "Consumption WO-7781"),
                S(OperationType.Adjust, 0.7, Ids("COIL-0001"), qty: -150, reference: "Blanking WO-7782"),
            },
            Reads = { R("Kanban Return Point Reader", 1, 2, "BIN-M8-02"), R("Assembly A Station Reader", 1, 3, "WO-7781-02", "BIN-M8-01", "BIN-BRG-01") },
        },

        // ───────────────────────── Personnel / visitors / muster / access / race timing ─────────────────────────
        new Scenario
        {
            Template = "people-presence", Site = "Riverside Campus & Riverside 10K", SiteCode = "CAMPUS", CompanyPrefix = "0614153",
            Locations = { L("Reception", LocationKind.Zone, null, Attr(("presenceTimeoutSec", 43200))), L("Turnstile", LocationKind.Gate, null, Attr(("presenceTimeoutSec", 43200))), L("Office Zone", LocationKind.Zone, null, Attr(("presenceTimeoutSec", 43200), ("widthM", 24), ("heightM", 12))), L("Lab Zone", LocationKind.Zone, null, Attr(("restricted", true), ("presenceTimeoutSec", 43200))), L("Muster Point A", LocationKind.MusterPoint, null, Attr(("presenceTimeoutSec", 43200))), L("Riverside 10K", LocationKind.Area), L("Race Start", LocationKind.Checkpoint, "Riverside 10K", Attr(("order", 1))), L("5 km Split", LocationKind.Checkpoint, "Riverside 10K", Attr(("order", 2))), L("Finish", LocationKind.Checkpoint, "Riverside 10K", Attr(("order", 3))), L("Off site", LocationKind.External) },
            Parties = { P("Security", PartyKind.Department), P("Olivia Grant", PartyKind.Employee, "E-201"), P("Ben Carter", PartyKind.Employee, "E-202"), P("Visitor: Ana Silva", PartyKind.Person) },
            Items =
            {
                I("BADGE", "BDG-0201", "Staff badge – Olivia Grant", "Office Zone", attrs: Attr(("person", "Olivia Grant"), ("authorisedZones", "Office,Lab"))),
                I("BADGE", "BDG-0202", "Staff badge – Ben Carter", "Lab Zone", attrs: Attr(("person", "Ben Carter"), ("authorisedZones", "Office,Lab"))),
                I("BADGE", "BDG-0203", "Staff badge – Chloe Adams", "Off site", attrs: Attr(("person", "Chloe Adams"), ("authorisedZones", "Office"))),
                I("VISITOR", "VIS-0001", "Visitor badge 01", "Reception", "Available", attrs: Attr(("host", ""), ("company", ""))),
                I("VISITOR", "VIS-0002", "Visitor badge 02", "Reception", "Available", attrs: Attr(("host", "Olivia Grant"), ("company", "Silva Consulting"))),
                I("VISITOR", "VIS-0003", "Visitor badge 03", "Reception", "Available"),
                I("BIB", "BIB-0101", "Bib 101", "Race Start", attrs: Attr(("athlete", "J. Kipchoge"), ("category", "M35"))), I("BIB", "BIB-0102", "Bib 102", "Race Start", attrs: Attr(("athlete", "S. Hassan"), ("category", "F30"))), I("BIB", "BIB-0103", "Bib 103", "Race Start", attrs: Attr(("athlete", "M. Farah"), ("category", "M40"))),
            },
            Devices =
            {
                D("Turnstile Reader", DeviceKind.Gate, "Nedap uPASS", null, Ant(1, "Turnstile", In), Ant(2, "Turnstile", Out)),
                D("Lab Zone Reader", DeviceKind.Portal, "Impinj R700", null, Ant(1, "Lab Zone", In), Ant(2, "Lab Zone", Out)),
                D("Muster Point A Reader", DeviceKind.Fixed, "Zebra FX9600", null, Ant(1, "Muster Point A")),
                D("Office BLE Gateways", DeviceKind.Fixed, "Kontakt.io Portal Beam ×4", null, Anchor(1, "Office Zone", 0, 0), Anchor(2, "Office Zone", 24, 0), Anchor(3, "Office Zone", 0, 12), Anchor(4, "Office Zone", 24, 12)),
                D("Start Mat", DeviceKind.Fixed, "Timing mat", null, Ant(1, "Race Start")), D("5 km Mat", DeviceKind.Fixed, "Timing mat", null, Ant(1, "5 km Split")), D("Finish Mat", DeviceKind.Fixed, "Timing mat", null, Ant(1, "Finish")),
            },
            History = { S(OperationType.Issue, 0.2, Ids("VIS-0002"), party: "Visitor: Ana Silva", to: "Office Zone", dueBackDays: 1, reference: "Host: Olivia Grant"), S(OperationType.Count, 1, Ids("BDG-0201", "BDG-0202"), to: "Muster Point A", reference: "Fire drill") },
            Reads =
            {
                R("Turnstile Reader", 1, 9, "BDG-0201", "BDG-0202"), R("Turnstile Reader", 1, 7.5, "VIS-0002"), R("Lab Zone Reader", 1, 6, "BDG-0202"), R("Lab Zone Reader", 1, 0.5, "VIS-0002"),
                new ReadDef("Office BLE Gateways", 1, Ids("BDG-0201"), 0.3, null, new() { [1] = -63.9, [2] = -72.8, [3] = -67.0, [4] = -73.5 }),
                new ReadDef("Office BLE Gateways", 1, Ids("VIS-0002"), 0.2, null, new() { [1] = -73.9, [2] = -62.6, [3] = -74.3, [4] = -66.0 }),
                R("Start Mat", 1, 3.0, "BIB-0101", "BIB-0102", "BIB-0103"), R("5 km Mat", 1, 2.72, "BIB-0101"), R("5 km Mat", 1, 2.70, "BIB-0102"), R("5 km Mat", 1, 2.66, "BIB-0103"), R("Finish Mat", 1, 2.42, "BIB-0101"), R("Finish Mat", 1, 2.39, "BIB-0102"), R("Finish Mat", 1, 2.33, "BIB-0103"),
            },
        },

        // ───────────────────────── Livestock / agriculture / nursery / research animals ─────────────────────────
        new Scenario
        {
            Template = "livestock-agri", Site = "Hillside Farm, Nursery & Research Unit", SiteCode = "FARM", CompanyPrefix = "0614154",
            Locations = { L("Barn 1", LocationKind.Building), L("Pasture North", LocationKind.Area), L("Pasture South", LocationKind.Area), L("Milking Parlour", LocationKind.Zone, "Barn 1"), L("Isolation Pen", LocationKind.Zone, "Barn 1"), L("Livestock Market", LocationKind.External), L("Nursery Bed 1", LocationKind.Area), L("Greenhouse 2", LocationKind.Building), L("Sales Yard", LocationKind.Area), L("Vivarium Room 1", LocationKind.Room), L("Rack V1", LocationKind.Rack, "Vivarium Room 1"), L("Produce Store", LocationKind.Room) },
            Parties = { P("Farm Manager", PartyKind.Employee), P("Vet Dr Lang", PartyKind.Other), P("County Livestock Market", PartyKind.Customer), P("Protocol P-2024-07", PartyKind.Department) },
            Items =
            {
                I("ANIMAL", "UK-0001-001", "Cow – Daisy", "Pasture North", "Alive", attrs: Attr(("species", "Cattle"), ("breed", "Holstein"), ("sex", "F"), ("dob", "2021-03-12"))),
                I("ANIMAL", "UK-0001-002", "Cow – Buttercup", "Milking Parlour", "Alive", attrs: Attr(("species", "Cattle"), ("breed", "Holstein"), ("sex", "F"), ("dob", "2020-06-01"))),
                I("ANIMAL", "UK-0001-003", "Heifer – Clover", "Isolation Pen", "Treatment", attrs: Attr(("species", "Cattle"), ("breed", "Jersey"), ("sex", "F"), ("dob", "2023-01-20"))),
                I("ANIMAL", "UK-0001-004", "Bull – Duke", "Pasture South", "Alive", attrs: Attr(("species", "Cattle"), ("breed", "Angus"), ("sex", "M"))),
                I("ANIMAL", "UK-0002-010", "Ewe 010", "Pasture South", "Alive", attrs: Attr(("species", "Sheep"), ("breed", "Texel"), ("sex", "F"))), I("ANIMAL", "UK-0002-011", "Ewe 011", "Pasture South", "Alive", attrs: Attr(("species", "Sheep"), ("sex", "F"))),
                I("PLANT", "PLT-ACER-001", "Acer palmatum 3 L", "Greenhouse 2", "Seedling", attrs: Attr(("species", "Acer palmatum"), ("batch", "B-2409"))), I("PLANT", "PLT-ACER-002", "Acer palmatum 3 L", "Greenhouse 2", "Seedling", attrs: Attr(("species", "Acer palmatum"), ("batch", "B-2409"))),
                I("PLANT", "PLT-OLEA-001", "Olea europaea 20 L", "Nursery Bed 1", "Growing", attrs: Attr(("species", "Olea europaea"))),
                I("CAGE", "CAGE-V1-01", "Cage V1-01", "Rack V1", attrs: Attr(("protocol", "P-2024-07"))), I("ANIMAL", "MUS-0001", "Mouse 0001", "Rack V1", "Alive", parent: "CAGE-V1-01", attrs: Attr(("species", "Mouse"), ("sex", "M"))), I("ANIMAL", "MUS-0002", "Mouse 0002", "Rack V1", "Alive", parent: "CAGE-V1-01", attrs: Attr(("species", "Mouse"), ("sex", "M"))),
                I("CAGE", "CAGE-V1-02", "Cage V1-02", "Rack V1", attrs: Attr(("protocol", "P-2024-07"))),
            },
            Devices = { D("Milking Parlour Reader", DeviceKind.Fixed, "Allflex panel reader (LF)", null, Ant(1, "Milking Parlour")), D("Farm Handheld", DeviceKind.Handheld, "Agrident AWR300 (LF/UHF)"), D("Vivarium Rack Reader", DeviceKind.Shelf, "Zebra FX7500", null, Ant(1, "Rack V1")) },
            History =
            {
                S(OperationType.Transfer, 10, Ids("UK-0001-001", "UK-0001-004"), to: "Pasture North", reference: "Rotation"),
                S(OperationType.Transfer, 4, Ids("UK-0001-004"), to: "Pasture South"),
                S(OperationType.Maintain, 2, Ids("UK-0001-003"), to: "Isolation Pen", reference: "Vet visit – lameness"),
                S(OperationType.Inspect, 1, Ids("PLT-OLEA-001"), state: "ReadyForSale", to: "Sales Yard"),
                S(OperationType.ProcessStage, 3, Ids("PLT-ACER-001"), state: "Growing", to: "Nursery Bed 1"),
                S(OperationType.Pack, 5, Ids("MUS-0001", "MUS-0002"), container: "CAGE-V1-01", reference: "P-2024-07"),
            },
            Reads = { R("Milking Parlour Reader", 1, 30, "UK-0001-001"), R("Milking Parlour Reader", 1, 6, "UK-0001-002"), R("Vivarium Rack Reader", 1, 1, "CAGE-V1-01", "CAGE-V1-02") },
        },

        // ───────────────────────── Museum / artwork ─────────────────────────
        new Scenario
        {
            Template = "museum-artwork", Site = "National Museum of Design", SiteCode = "MUSEUM", CompanyPrefix = "0614155",
            Locations = { L("Gallery 1", LocationKind.Room), L("Gallery 2", LocationKind.Room), L("Storeroom B", LocationKind.Room, null, Attr(("climate", "18C/50%"))), L("Conservation Lab", LocationKind.Room), L("Loading Bay", LocationKind.Dock), L("Partner: Museo del Diseño", LocationKind.External) },
            Parties = { P("Registrar", PartyKind.Employee), P("Conservation", PartyKind.Department), P("Museo del Diseño", PartyKind.Other) },
            Items =
            {
                I("ARTEFACT", "ACC-1998.12", "Bauhaus chair (Breuer)", "Gallery 1", "OnDisplay", attrs: Attr(("accessionNo", "1998.12"), ("period", "1925"), ("material", "Steel, canvas"), ("insuredValue", 85000))),
                I("ARTEFACT", "ACC-2004.3", "Art Deco radio", "Gallery 1", "OnDisplay", attrs: Attr(("accessionNo", "2004.3"), ("period", "1934"), ("insuredValue", 12000))),
                I("ARTEFACT", "ACC-1975.41", "Ceramic vase (Lucie Rie)", "Storeroom B", "InStorage", attrs: Attr(("accessionNo", "1975.41"), ("material", "Stoneware"), ("insuredValue", 40000))),
                I("ARTEFACT", "ACC-2011.7", "Typewriter (Olivetti Valentine)", "Conservation Lab", "Conservation", attrs: Attr(("accessionNo", "2011.7"), ("insuredValue", 3000))),
                I("ARTEFACT", "ACC-1989.2", "Poster collection box 2", "Partner: Museo del Diseño", "OnLoan", "Museo del Diseño", dueBackDays: 120, attrs: Attr(("accessionNo", "1989.2"))),
                I("ARTWORK", "ART-0001", "Untitled (Blue) – oil on canvas", "Storeroom B", "InStorage", attrs: Attr(("artist", "A. Novak"), ("title", "Untitled (Blue)"), ("year", "1971"), ("insuredValue", 220000))),
                I("ARTWORK", "ART-0002", "Study for a Chair – lithograph", "Gallery 2", "Installed", attrs: Attr(("artist", "M. Breuer"), ("title", "Study for a Chair"), ("year", "1926"))),
                I("ARTWORK", "ART-0003", "Composition IX", "Storeroom B", "InStorage", attrs: Attr(("artist", "L. Hart"), ("title", "Composition IX"), ("insuredValue", 96000))),
                I("ART-CRATE", "CRATE-0001", "Museum crate 0001", "Loading Bay", attrs: Attr(("dimensions", "120x90x30"), ("climateControlled", true))),
            },
            Devices = { D("Loading Bay Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Loading Bay", Out), Ant(2, "Loading Bay", In)), D("Gallery 1 Reader", DeviceKind.Fixed, "Impinj R700", null, Ant(1, "Gallery 1")), D("Registrar Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Transfer, 40, Ids("ACC-1998.12", "ACC-2004.3"), to: "Gallery 1", reference: "Exhibition: Modern Living"),
                S(OperationType.Maintain, 3, Ids("ACC-2011.7"), to: "Conservation Lab", reference: "Corrosion treatment"),
                S(OperationType.Dispatch, 60, Ids("ACC-1989.2"), to: "Partner: Museo del Diseño", party: "Museo del Diseño", dueBackDays: 180, reference: "Loan L-2024-03"),
                S(OperationType.Pack, 1, Ids("ART-0003"), container: "CRATE-0001", reference: "Loan L-2024-09"),
                S(OperationType.Count, 0.3, Ids("ACC-1998.12", "ACC-2004.3"), to: "Gallery 1", reference: "Daily gallery check"),
            },
            Reads = { R("Gallery 1 Reader", 1, 4, "ACC-1998.12", "ACC-2004.3"), R("Loading Bay Portal", 1, 1, "ACC-1975.41") },
        },

        // ───────────────────────── Hotel assets / minibar / hotel linen ─────────────────────────
        new Scenario
        {
            Template = "hospitality", AlsoRequires = new[] { "linen-laundry" }, Site = "Grand Hotel", SiteCode = "HOTEL", CompanyPrefix = "0614156",
            Locations = { L("Floor 3", LocationKind.Floor), L("Room 301", LocationKind.Room, "Floor 3"), L("Room 302", LocationKind.Room, "Floor 3"), L("Room 303", LocationKind.Room, "Floor 3"), L("Housekeeping Store F3", LocationKind.Room, "Floor 3", Attr(("clean", true))), L("Engineering Workshop", LocationKind.Room), L("Minibar Store", LocationKind.Room), L("Hotel Laundry", LocationKind.Room), L("Service Exit", LocationKind.Gate) },
            Parties = { P("Housekeeping F3", PartyKind.Department), P("Engineering", PartyKind.Department), P("Guest – Room 302", PartyKind.Customer) },
            Items =
            {
                I("HOTEL-ASSET", "TV-301", "55\" TV", "Room 301", "InRoom", attrs: Attr(("category", "TV"), ("brand", "Samsung"), ("room", "301")), cost: 650), I("HOTEL-ASSET", "TV-302", "55\" TV", "Room 302", "InRoom", attrs: Attr(("category", "TV"), ("room", "302")), cost: 650), I("HOTEL-ASSET", "TV-303", "55\" TV", "Engineering Workshop", "Maintenance", attrs: Attr(("category", "TV"), ("room", "303")), cost: 650),
                I("HOTEL-ASSET", "SAFE-301", "Room safe", "Room 301", "InRoom", attrs: Attr(("category", "Safe"))), I("HOTEL-ASSET", "HD-301", "Hairdryer", "Room 301", "InRoom", attrs: Attr(("category", "Hairdryer"))), I("HOTEL-ASSET", "HD-302", "Hairdryer", "Housekeeping Store F3", "InStore", attrs: Attr(("category", "Hairdryer"))),
                I("HOTEL-ASSET", "IRN-F3", "Iron & board", "Housekeeping Store F3", "InStore", attrs: Attr(("category", "Iron"))), I("HOTEL-ASSET", "ART-302", "Framed print", "Room 302", "InRoom", attrs: Attr(("category", "Artwork")), cost: 180),
                I("MINIBAR", "MB-301-WATER", "Still water 500 ml", "Room 301", qty: 2, expiryDays: 300, attrs: Attr(("sku", "MB-WTR"), ("price", 4))), I("MINIBAR", "MB-301-CHOC", "Chocolate bar", "Room 301", qty: 1, expiryDays: 120, attrs: Attr(("sku", "MB-CHC"), ("price", 5))),
                I("MINIBAR", "MB-302-WATER", "Still water 500 ml", "Room 302", qty: 4, expiryDays: 300, attrs: Attr(("sku", "MB-WTR"), ("price", 4))), I("MINIBAR", "MB-STORE-WATER", "Still water 500 ml (store)", "Minibar Store", qty: 96, expiryDays: 300, attrs: Attr(("sku", "MB-WTR"), ("price", 4))),
                I("LINEN", "HTL-TWL-0001", "Bath towel (hotel)", "Housekeeping Store F3", "Clean", cycles: 80), I("LINEN", "HTL-TWL-0002", "Bath towel (hotel)", "Room 302", "Issued", "Housekeeping F3", cycles: 81), I("LINEN", "HTL-ROB-0001", "Bathrobe", "Room 301", "Issued", "Housekeeping F3", cycles: 33), I("LINEN", "HTL-SHT-0001", "Sheet queen", "Hotel Laundry", "InWash", cycles: 140),
            },
            Devices = { D("Service Exit Reader", DeviceKind.Gate, "Impinj R700", null, Ant(1, "Service Exit", Out)), D("Housekeeping Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Maintain, 1, Ids("TV-303"), to: "Engineering Workshop", reference: "No signal – ticket 2231"),
                S(OperationType.Adjust, 0.5, Ids("MB-301-WATER"), qty: -2, reference: "Guest consumption"), S(OperationType.Adjust, 0.4, Ids("MB-301-CHOC"), qty: -1, reference: "Guest consumption"),
                S(OperationType.Issue, 2, Ids("HTL-TWL-0001"), party: "Housekeeping F3", to: "Room 303"), S(OperationType.Return, 1, Ids("HTL-TWL-0001"), to: "Hotel Laundry"),
                S(OperationType.ProcessStage, 0.8, Ids("HTL-TWL-0001"), state: "InWash"), S(OperationType.ProcessStage, 0.6, Ids("HTL-TWL-0001"), state: "Clean", to: "Housekeeping Store F3"),
            },
            Reads = { R("Service Exit Reader", 1, 2, "HTL-ROB-0001") },
        },

        // ───────────────────────── Food trays / food production / cold chain ─────────────────────────
        new Scenario
        {
            Template = "food-coldchain", Site = "Central Production Kitchen & Cold Logistics", SiteCode = "FOOD", CompanyPrefix = "0614157",
            Locations = { L("Kitchen", LocationKind.Room), L("Tray Wash", LocationKind.Zone), L("Ward Delivery Dock", LocationKind.Dock), L("Ward 5", LocationKind.Room), L("Production Line 1", LocationKind.Zone), L("Blast Chiller", LocationKind.Cabinet, null, Attr(("cold", true))), L("Packing", LocationKind.Zone), L("Cold Dock", LocationKind.Dock, null, Attr(("cold", true))), L("Customer: FreshMart DC", LocationKind.Customer) },
            Parties = { P("Ward 5 Catering", PartyKind.Department), P("FreshMart DC", PartyKind.Customer), P("Chill Logistics", PartyKind.Supplier) },
            Items =
            {
                I("FOOD-TRAY", "TRAY-0001", "Meal tray 0001", "Kitchen", "Kitchen", cycles: 640), I("FOOD-TRAY", "TRAY-0002", "Meal tray 0002", "Ward 5", "Delivered", cycles: 611), I("FOOD-TRAY", "TRAY-0003", "Meal tray 0003", "Tray Wash", "Washing", cycles: 1998), I("FOOD-TRAY", "TRAY-0004", "Meal tray 0004", "Kitchen", "Kitchen", cycles: 12),
                I("FOOD-BATCH", "BATCH-2409-17", "Lasagne batch 2409-17", "Blast Chiller", "Chilling", expiryDays: 4, attrs: Attr(("product", "Beef lasagne"), ("allergens", "Gluten, Milk"))),
                I("FOOD-BATCH", "BATCH-2409-16", "Soup batch 2409-16", "Kitchen", "Prepared", expiryDays: 3, attrs: Attr(("product", "Tomato soup"), ("allergens", "Celery"))),
                I("FOOD-BATCH", "BATCH-2409-12", "Curry batch 2409-12", "Customer: FreshMart DC", "Shipped", expiryDays: -1, attrs: Attr(("product", "Chicken curry"))),
                I("COLD-CONTAINER", "CC-0001", "Insulated roll cage 0001", "Cold Dock", "Loaded", attrs: Attr(("minTempC", 0), ("maxTempC", 5), ("lastTempC", 3.2), ("tempExcursion", false))),
                I("COLD-CONTAINER", "CC-0002", "Insulated roll cage 0002", "Cold Dock", "Loaded", attrs: Attr(("minTempC", 0), ("maxTempC", 5), ("lastTempC", 9.4), ("tempExcursion", true))),
                I("COLD-CONTAINER", "CC-0003", "Insulated roll cage 0003", "Cold Dock", "Returned", attrs: Attr(("minTempC", 0), ("maxTempC", 5), ("lastTempC", 2.1), ("tempExcursion", false))),
            },
            Devices = { D("Cold Dock Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Cold Dock", In), Ant(2, "Cold Dock", Out)), D("Tray Wash Tunnel", DeviceKind.Tunnel, "Impinj R700", null, Ant(1, "Tray Wash")), D("Ward Dock Reader", DeviceKind.Fixed, "Zebra FX7500", null, Ant(1, "Ward Delivery Dock")) },
            History =
            {
                S(OperationType.Dispatch, 1.0, Ids("TRAY-0001"), to: "Ward 5", party: "Ward 5 Catering", reference: "Lunch service"), S(OperationType.Return, 0.8, Ids("TRAY-0001"), to: "Tray Wash"),
                S(OperationType.ProcessStage, 0.7, Ids("TRAY-0001"), state: "Washing"), S(OperationType.ProcessStage, 0.6, Ids("TRAY-0001"), state: "Kitchen", to: "Kitchen"),
                S(OperationType.ProcessStage, 1.5, Ids("BATCH-2409-16"), state: "Cooking", to: "Production Line 1"), S(OperationType.ProcessStage, 1.2, Ids("BATCH-2409-16"), state: "Chilling", to: "Blast Chiller"), S(OperationType.ProcessStage, 0.9, Ids("BATCH-2409-16"), state: "Packed", to: "Packing"),
                S(OperationType.Pack, 0.5, Ids("BATCH-2409-16"), container: "CC-0001"),
                S(OperationType.Dispatch, 2.5, Ids("CC-0002"), to: "Customer: FreshMart DC", party: "FreshMart DC", reference: "Run 44"),
                S(OperationType.Receive, 2.2, Ids("CC-0002"), to: "Customer: FreshMart DC", reference: "POD Run 44"),
            },
            Reads = { R("Tray Wash Tunnel", 1, 5, "TRAY-0003"), R("Ward Dock Reader", 1, 20, "TRAY-0002"), R("Cold Dock Portal", 1, 3, "CC-0003") },
        },

        // ───────────────────────── Vehicles / yard / fleet / tyres / aircraft parts ─────────────────────────
        new Scenario
        {
            Template = "fleet-aviation", Site = "Northern Depot & Part-145 MRO", SiteCode = "FLEET", CompanyPrefix = "0614158",
            Locations = { L("Vehicle Yard", LocationKind.Yard), L("Bay Row A", LocationKind.Area, "Vehicle Yard"), L("Workshop", LocationKind.Room), L("Yard Gate", LocationKind.Gate), L("Tyre Store", LocationKind.Room), L("Rotable Store", LocationKind.Room), L("Quarantine Store", LocationKind.Room, null, Attr(("quarantine", true))), L("Hangar Apron", LocationKind.Area), L("On road", LocationKind.External) },
            Parties = { P("Fleet Ops", PartyKind.Department), P("Driver: Kofi Mensah", PartyKind.Employee), P("Part-145 Workshop", PartyKind.Department) },
            Items =
            {
                I("VEHICLE", "VEH-0001", "DAF LF 12t – AB12 CDE", "On road", "Dispatched", attrs: Attr(("plate", "AB12 CDE"), ("make", "DAF"), ("model", "LF"), ("vin", "XLRAE45GF0L123456"))),
                I("VEHICLE", "VEH-0002", "Ford Transit – CD34 EFG", "On road", "Dispatched", "Driver: Kofi Mensah", attrs: Attr(("plate", "CD34 EFG"), ("make", "Ford"), ("model", "Transit"))),
                I("VEHICLE", "VEH-0003", "Trailer – T-7781", "Workshop", "InWorkshop", attrs: Attr(("plate", "T-7781"), ("make", "Schmitz"))),
                I("TYRE", "TYR-0001", "Tyre 315/70 R22.5", "Tyre Store", "New", cycles: 1, attrs: Attr(("size", "315/70R22.5"), ("dot", "2223"), ("treadMm", 9))), I("TYRE", "TYR-0002", "Tyre 315/70 R22.5", "Tyre Store", "New", attrs: Attr(("size", "315/70R22.5"), ("treadMm", 11))),
                I("TYRE", "TYR-0003", "Tyre 315/70 R22.5", "Tyre Store", "New", attrs: Attr(("size", "315/70R22.5"), ("treadMm", 16))), I("TYRE", "TYR-0004", "Tyre 315/70 R22.5", "Tyre Store", "Retread", cycles: 4, attrs: Attr(("size", "315/70R22.5"), ("treadMm", 3))), I("TYRE", "TYR-0005", "Tyre 235/65 R16C", "Tyre Store", "Removed", cycles: 3, attrs: Attr(("size", "235/65R16C"), ("treadMm", 2))),
                I("AIRCRAFT", "G-ABCD", "ATR 72-600 G-ABCD", "Hangar Apron", attrs: Attr(("registration", "G-ABCD"), ("type", "ATR 72-600"), ("operator", "Northern Air"))),
                I("ROTABLE", "ROT-0001", "Starter generator", "Rotable Store", "Serviceable", inspectedDaysAgo: 100, attrs: Attr(("partNumber", "23081-011"), ("serialNumber", "SG-4471"), ("hoursSinceOverhaul", 812))),
                I("ROTABLE", "ROT-0002", "Starter generator", "Rotable Store", "Serviceable", inspectedDaysAgo: 20, attrs: Attr(("partNumber", "23081-011"), ("serialNumber", "SG-5102"), ("hoursSinceOverhaul", 0))),
                I("ROTABLE", "ROT-0003", "Brake assembly", "Quarantine Store", "Quarantined", inspectedDaysAgo: 5, attrs: Attr(("partNumber", "2-1590-2"), ("serialNumber", "BR-0912"))),
                I("ROTABLE", "ROT-0004", "Fuel pump", "Rotable Store", "Unserviceable", inspectedDaysAgo: 380, attrs: Attr(("partNumber", "1C10-1"), ("serialNumber", "FP-2201"))),
                I("FLEET-EQUIP", "FE-0001", "Tail-lift", "Bay Row A", inspectedDaysAgo: 100, attrs: Attr(("type", "Tail-lift"), ("vehicle", "AB12 CDE"))), I("FLEET-EQUIP", "FE-0002", "Load straps set", "Bay Row A", inspectedDaysAgo: 10, attrs: Attr(("type", "Straps"))),
            },
            Devices = { D("Yard Gate Reader", DeviceKind.Gate, "Impinj R700 + long-range antennas", null, Ant(1, "Yard Gate", In), Ant(2, "Yard Gate", Out)), D("Workshop Handheld", DeviceKind.Handheld, "Zebra RFD40"), D("Rotable Store Reader", DeviceKind.Shelf, "Zebra FX7500", null, Ant(1, "Rotable Store")) },
            History =
            {
                S(OperationType.Receive, 6, Ids("VEH-0001"), to: "Vehicle Yard", reference: "Return from route 3"), S(OperationType.Transfer, 5.9, Ids("VEH-0001"), to: "Bay Row A"),
                S(OperationType.Maintain, 5, Ids("VEH-0001"), to: "Workshop", reference: "Tyre change"), S(OperationType.Pack, 4.9, Ids("TYR-0001", "TYR-0002"), container: "VEH-0001", reference: "Fitment"), S(OperationType.Return, 4.8, Ids("VEH-0001"), to: "Bay Row A"),
                S(OperationType.Dispatch, 1, Ids("VEH-0002"), to: "On road", party: "Driver: Kofi Mensah", reference: "Route 7"),
                S(OperationType.Pack, 3, Ids("ROT-0001"), container: "G-ABCD", reference: "WO-145-2201"),
                S(OperationType.Inspect, 2, Ids("ROT-0003"), state: "Quarantined", to: "Quarantine Store", reference: "Suspect unapproved part"),
                S(OperationType.Maintain, 1.5, Ids("ROT-0004"), to: "Workshop", reference: "Overhaul"),
            },
            Reads = { R("Rotable Store Reader", 1, 3, "ROT-0002"), R("Yard Gate Reader", 2, 24, "VEH-0002"), R("Yard Gate Reader", 2, 1, "VEH-0001") },
        },

        // ───────────────────────── Waste bins / waste tracking ─────────────────────────
        new Scenario
        {
            Template = "waste-management", Site = "Metro Waste Services Depot", SiteCode = "WASTE", CompanyPrefix = "0614159",
            Locations = { L("Depot Yard", LocationKind.Yard), L("Truck 7", LocationKind.Vehicle, "Depot Yard", mobile: true), L("Hazardous Store", LocationKind.Room, null, Attr(("hazardous", true))), L("Address: 12 Elm Street", LocationKind.Customer), L("Address: Riverside Flats", LocationKind.Customer), L("Treatment Plant", LocationKind.External), L("Landfill Site", LocationKind.External) },
            Parties = { P("Route 12 Crew", PartyKind.Department), P("HazChem Carriers", PartyKind.Supplier), P("Riverside Flats Management", PartyKind.Customer) },
            Items =
            {
                I("WASTE-BIN", "BIN-240-0001", "240 L bin – general", "Address: 12 Elm Street", "Deployed", attrs: Attr(("owner", "12 Elm Street"), ("sizeL", 240), ("stream", "General"))),
                I("WASTE-BIN", "BIN-240-0002", "240 L bin – recycling", "Address: 12 Elm Street", "Deployed", attrs: Attr(("owner", "12 Elm Street"), ("sizeL", 240), ("stream", "Recycling"))),
                I("WASTE-BIN", "BIN-1100-0001", "1100 L bin – general", "Address: Riverside Flats", "Deployed", attrs: Attr(("owner", "Riverside Flats"), ("sizeL", 1100), ("stream", "General"))),
                I("WASTE-BIN", "BIN-1100-0002", "1100 L bin – glass", "Address: Riverside Flats", "Collected", attrs: Attr(("owner", "Riverside Flats"), ("sizeL", 1100), ("stream", "Glass"))),
                I("WASTE-BIN", "BIN-240-0003", "240 L bin – organic", "Depot Yard", "Damaged", attrs: Attr(("sizeL", 240), ("stream", "Organic"))),
                I("WASTE-CONTAINER", "HZ-0001", "Solvent drum 200 L", "Hazardous Store", "Filled", attrs: Attr(("wasteCode", "14 06 03*"), ("hazardous", true), ("consignmentNote", "CN-77120"))),
                I("WASTE-CONTAINER", "HZ-0002", "Clinical waste bin", "Treatment Plant", "Treated", attrs: Attr(("wasteCode", "18 01 03*"), ("hazardous", true), ("consignmentNote", "CN-77088"))),
                I("WASTE-CONTAINER", "HZ-0003", "Battery box", "Hazardous Store", "Filled", attrs: Attr(("wasteCode", "16 06 01*"), ("hazardous", true), ("consignmentNote", "CN-77131"))),
                I("WASTE-CONTAINER", "SKIP-0001", "Skip 8 yd", "Landfill Site", "Disposed", attrs: Attr(("wasteCode", "20 03 01"), ("hazardous", false))),
            },
            Devices = { D("Truck 7 Lifter Reader", DeviceKind.Vehicle, "Impinj R420 on bin lifter", null, Ant(1, "Truck 7")), D("Depot Handheld", DeviceKind.Handheld, "Chainway C72") },
            History =
            {
                S(OperationType.Count, 1.2, Ids("BIN-240-0001", "BIN-240-0002"), to: "Address: 12 Elm Street", reference: "Route 12 collection"),
                S(OperationType.Issue, 2, Ids("HZ-0003"), party: "HazChem Carriers", reference: "CN-77131"), S(OperationType.Dispatch, 1.9, Ids("HZ-0003"), to: "Truck 7", reference: "CN-77131"),
                S(OperationType.Inspect, 3, Ids("BIN-240-0003"), state: "Damaged", to: "Depot Yard", reference: "Cracked lid"),
            },
            Reads = { R("Truck 7 Lifter Reader", 1, 30, "BIN-1100-0002") },
        },

        // ───────────────────────── Postal sorting / baggage ─────────────────────────
        new Scenario
        {
            Template = "postal-baggage", Site = "Airport T2 Baggage Hall & Mail Hub", SiteCode = "T2", CompanyPrefix = "0614160",
            Locations = { L("Check-in", LocationKind.Zone), L("Sortation", LocationKind.Zone), L("Make-up Lane 4", LocationKind.Zone), L("Stand 12", LocationKind.Area), L("Arrivals Belt 3", LocationKind.Zone), L("Mail Hub", LocationKind.Building), L("Inbound Dock", LocationKind.Dock, "Mail Hub"), L("Sort Floor", LocationKind.Zone, "Mail Hub"), L("Outbound Dock", LocationKind.Dock, "Mail Hub"), L("Delivery Van 3", LocationKind.Vehicle, "Mail Hub", mobile: true), L("Delivered", LocationKind.External) },
            Parties = { P("Passenger: R. Ito", PartyKind.Person), P("Passenger: L. Moreau", PartyKind.Person), P("Recipient: Baker & Co", PartyKind.Customer), P("Ramp Team", PartyKind.Department) },
            Items =
            {
                I("BAG", "BAG-0001", "Bag 0001 – BA2201", "Check-in", "CheckedIn", attrs: Attr(("pnr", "X7K2LQ"), ("flight", "BA2201"), ("passenger", "R. Ito"), ("destination", "AMS"))),
                I("BAG", "BAG-0002", "Bag 0002 – BA2201", "Check-in", "CheckedIn", attrs: Attr(("pnr", "Q9M4ZT"), ("flight", "BA2201"), ("passenger", "L. Moreau"), ("destination", "AMS"))),
                I("BAG", "BAG-0003", "Bag 0003 – KL1010", "Arrivals Belt 3", "Arrived", attrs: Attr(("flight", "KL1010"), ("passenger", "S. Bakker"))),
                I("BAG", "BAG-0004", "Bag 0004 – KL1010", "Sortation", "Mishandled", attrs: Attr(("flight", "KL1010"), ("passenger", "T. Nguyen"))),
                I("BAG", "BAG-0005", "Bag 0005 – LH905", "Check-in", "CheckedIn", attrs: Attr(("flight", "LH905"), ("destination", "FRA"))),
                I("ULD", "ULD-AKE1234", "AKE 1234 BA", "Stand 12", attrs: Attr(("uldCode", "AKE1234BA"), ("flight", "BA2201"))),
                I("MAIL-CAGE", "CAGE-M-01", "Mail cage 01", "Sort Floor", attrs: Attr(("route", "R-North"))),
                I("PARCEL", "PCL-0001", "Parcel 0001", "Inbound Dock", "Accepted", attrs: Attr(("trackingNo", "JD0001"), ("destination", "Leeds"), ("service", "Next day"))),
                I("PARCEL", "PCL-0002", "Parcel 0002", "Inbound Dock", "Accepted", attrs: Attr(("trackingNo", "JD0002"), ("destination", "Leeds"))),
                I("PARCEL", "PCL-0003", "Parcel 0003", "Inbound Dock", "Accepted", attrs: Attr(("trackingNo", "JD0003"), ("destination", "York"))),
                I("PARCEL", "PCL-0004", "Parcel 0004", "Delivered", "Delivered", "Recipient: Baker & Co", attrs: Attr(("trackingNo", "JD0004"))),
                I("PARCEL", "PCL-0005", "Parcel 0005", "Inbound Dock", "Accepted", attrs: Attr(("trackingNo", "JD0005"))),
            },
            Devices = { D("Sortation Tunnel", DeviceKind.Tunnel, "Zebra FX9600 tunnel", null, Ant(1, "Sortation")), D("Make-up Lane 4 Reader", DeviceKind.Fixed, "Impinj R700", null, Ant(1, "Make-up Lane 4")), D("Inbound Dock Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Inbound Dock", In)), D("Outbound Dock Portal", DeviceKind.Portal, "Zebra FX9600", null, Ant(1, "Outbound Dock", Out)), D("Ramp Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.ProcessStage, 0.3, Ids("BAG-0001"), state: "Sorted", to: "Make-up Lane 4"), S(OperationType.ProcessStage, 0.35, Ids("BAG-0002"), state: "Sorted", to: "Make-up Lane 4"), S(OperationType.Pack, 0.25, Ids("BAG-0002"), container: "ULD-AKE1234", reference: "BA2201"),
                S(OperationType.Receive, 0.5, Ids("BAG-0003", "BAG-0004"), to: "Arrivals Belt 3", reference: "KL1010 arrival"), S(OperationType.ProcessStage, 0.4, Ids("BAG-0004"), state: "Mishandled", to: "Sortation", reference: "Tag damaged"),
                S(OperationType.Receive, 1, Ids("PCL-0001", "PCL-0002", "PCL-0003"), to: "Sort Floor", reference: "Inbound trunk 22"), S(OperationType.Pack, 0.9, Ids("PCL-0001", "PCL-0002"), container: "CAGE-M-01"),
                S(OperationType.Dispatch, 0.3, Ids("PCL-0003"), to: "Delivery Van 3", reference: "Van 3 load"),
            },
            Reads = { R("Sortation Tunnel", 1, 8, "BAG-0001", "BAG-0002"), R("Make-up Lane 4 Reader", 1, 7, "BAG-0001"), R("Outbound Dock Portal", 1, 6, "PCL-0003"), R("Inbound Dock Portal", 1, 2, "PCL-0005") },
        },

        // ───────────────────────── Data centre / cable reels ─────────────────────────
        new Scenario
        {
            Template = "datacentre-cables", Site = "DC-East", SiteCode = "DCE", CompanyPrefix = "0614161",
            Locations = { L("Data Hall 1", LocationKind.Room, null, Attr(("restricted", true))), L("Rack R01", LocationKind.Rack, "Data Hall 1"), L("Rack R02", LocationKind.Rack, "Data Hall 1"), L("Data Hall Exit", LocationKind.Gate, "Data Hall 1"), L("Spares Room", LocationKind.Room), L("Staging Room", LocationKind.Room), L("Cable Store", LocationKind.Room), L("Secure Disposal", LocationKind.External) },
            Parties = { P("Platform Team", PartyKind.Department), P("Network Team", PartyKind.Department), P("Contractor: FibreWorks", PartyKind.Supplier) },
            Items =
            {
                I("DC-ASSET", "SRV-1001", "Dell R760", "Rack R01", "Installed", "Platform Team", attrs: Attr(("hostname", "k8s-node-01"), ("rackU", "R01-U10"), ("model", "R760"), ("owner", "Platform"))),
                I("DC-ASSET", "SRV-1002", "Dell R760", "Rack R01", "Installed", "Platform Team", attrs: Attr(("hostname", "k8s-node-02"), ("rackU", "R01-U12"), ("model", "R760"))),
                I("DC-ASSET", "SRV-1003", "Dell R760", "Spares Room", "Spare", attrs: Attr(("model", "R760"))),
                I("DC-ASSET", "SW-2001", "Arista 7050X3", "Rack R01", "Installed", "Network Team", attrs: Attr(("hostname", "tor-r01-a"), ("rackU", "R01-U42"))),
                I("DC-ASSET", "SW-2002", "Arista 7050X3", "Rack R02", "Installed", "Network Team", attrs: Attr(("hostname", "tor-r02-a"), ("rackU", "R02-U42"))),
                I("DC-ASSET", "SRV-0901", "HPE DL360 Gen10", "Staging Room", "Decommissioned", attrs: Attr(("hostname", "legacy-db-01"))),
                I("DC-ASSET", "STO-3001", "NetApp AFF A400", "Rack R02", "Installed", "Platform Team", attrs: Attr(("hostname", "nas-01"), ("rackU", "R02-U20"))),
                I("CABLE-REEL", "REEL-0001", "OM4 fibre 12-core", "Cable Store", qty: 320, attrs: Attr(("cableType", "OM4"), ("gauge", "12-core"))),
                I("CABLE-REEL", "REEL-0002", "Cat6A UTP", "Cable Store", qty: 100, attrs: Attr(("cableType", "Cat6A"))),
                I("CABLE-REEL", "REEL-0003", "Cat6A UTP", "Data Hall 1", qty: 275, custodian: "Contractor: FibreWorks", attrs: Attr(("cableType", "Cat6A"))),
            },
            Devices = { D("Data Hall Exit Portal", DeviceKind.Portal, "Impinj R700", null, Ant(1, "Data Hall Exit", Out), Ant(2, "Data Hall Exit", In)), D("Rack Audit Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Transfer, 20, Ids("SRV-1002"), to: "Rack R01", party: "Platform Team", reference: "CHG-4410"),
                S(OperationType.ProcessStage, 3, Ids("SRV-0901"), state: "Decommissioned", to: "Staging Room", reference: "CHG-4480"),
                S(OperationType.Adjust, 2, Ids("REEL-0002"), qty: -60, reference: "Patching R02"), S(OperationType.Adjust, 1, Ids("REEL-0003"), qty: -25, reference: "Patching R01"),
                S(OperationType.Count, 0.5, Ids("SRV-1001", "SRV-1002", "SW-2001"), to: "Rack R01", reference: "Quarterly rack audit"),
            },
            Reads = { R("Data Hall Exit Portal", 2, 30, "REEL-0003"), R("Data Hall Exit Portal", 1, 1, "SRV-1001") },
        },

        // ───────────────────────── Construction / oil & gas / offshore / marine ─────────────────────────
        new Scenario
        {
            Template = "field-industrial", Site = "North Sea Supply Base", SiteCode = "BASE", CompanyPrefix = "0614162",
            Locations = { L("Onshore Yard", LocationKind.Yard), L("Pipe Yard", LocationKind.Yard), L("Inspection Bay", LocationKind.Room), L("Quay 3", LocationKind.Dock), L("MV Northern Star", LocationKind.Vessel, null, mobile: true), L("Platform Alpha", LocationKind.External), L("Site: Bridge Project", LocationKind.Site), L("Yard Gate", LocationKind.Gate) },
            Parties = { P("Offshore Logistics", PartyKind.Department), P("Bridge Project (client)", PartyKind.Customer), P("MV Northern Star (crew)", PartyKind.Vehicle) },
            Items =
            {
                I("SITE-EQUIP", "GEN-0001", "Generator 100 kVA", "Site: Bridge Project", "OnSite", "Bridge Project (client)", inspectedDaysAgo: 60, attrs: Attr(("category", "Power"), ("certificateNo", "C-1181"), ("owner", "Base"))),
                I("SITE-EQUIP", "SUBP-0001", "Submersible pump", "Onshore Yard", "InYard", inspectedDaysAgo: 30, attrs: Attr(("category", "Pumps"), ("certificateNo", "C-1190"))),
                I("SITE-EQUIP", "WLD-0001", "Welding set", "Onshore Yard", "InYard", inspectedDaysAgo: 400, attrs: Attr(("category", "Welding"), ("certificateNo", "C-0871"))),
                I("SITE-EQUIP", "LFT-0001", "Lifting beam 10 t", "Inspection Bay", "UnderRepair", inspectedDaysAgo: 200, attrs: Attr(("category", "Lifting"))),
                I("SITE-EQUIP", "GEN-0002", "Generator 60 kVA", "Platform Alpha", "Mobilised", inspectedDaysAgo: 20, attrs: Attr(("category", "Power"))),
                I("OG-COMPONENT", "PIPE-0001", "Pipe 8\" sch 40 x 12 m", "Pipe Yard", "InYard", attrs: Attr(("heatNumber", "H-88213"), ("spec", "API 5L X65"), ("sizeInch", 8))), I("OG-COMPONENT", "PIPE-0002", "Pipe 8\" sch 40 x 12 m", "Pipe Yard", "InYard", attrs: Attr(("heatNumber", "H-88214"), ("spec", "API 5L X65"), ("sizeInch", 8))),
                I("OG-COMPONENT", "VLV-0001", "Ball valve 6\" 600#", "Platform Alpha", "Installed", attrs: Attr(("spec", "API 6D"), ("sizeInch", 6))),
                I("CCU", "CCU-0001", "Half-height CCU 10 ft", "Quay 3", "Onshore", inspectedDaysAgo: 100, attrs: Attr(("ccuNumber", "NSB-0001"), ("tareKg", 1850), ("dnvCert", "DNV-2.7-1"))),
                I("SITE-EQUIP", "TLK-0001", "Tool kit – rigging", "Quay 3", "InYard", parent: "CCU-0001", inspectedDaysAgo: 15, attrs: Attr(("category", "Tools"))),
                I("CCU", "CCU-0002", "Mini container 6 ft", "Platform Alpha", "Offshore", inspectedDaysAgo: 300, attrs: Attr(("ccuNumber", "NSB-0002"), ("tareKg", 900))),
                I("VESSEL-EQUIP", "LR-0001", "Liferaft 25 person", "MV Northern Star", "OnVessel", inspectedDaysAgo: 200, attrs: Attr(("solasCategory", "LSA"), ("vessel", "MV Northern Star"))), I("VESSEL-EQUIP", "LR-0002", "Liferaft 25 person", "Inspection Bay", "Servicing", inspectedDaysAgo: 370, attrs: Attr(("solasCategory", "LSA"))),
            },
            Devices = { D("Yard Gate Reader", DeviceKind.Gate, "Impinj R700", null, Ant(1, "Yard Gate", Out), Ant(2, "Yard Gate", In)), D("Quay 3 Portal", DeviceKind.Portal, "Zebra FX9600 (IP67)", null, Ant(1, "Quay 3", In), Ant(2, "Quay 3", Out)), D("Base Handheld", DeviceKind.Handheld, "Zebra MC3390R") },
            History =
            {
                S(OperationType.Transfer, 15, Ids("GEN-0001"), to: "Site: Bridge Project", party: "Bridge Project (client)", reference: "Hire H-2201"),
                S(OperationType.Pack, 2, Ids("TLK-0001"), container: "CCU-0001", reference: "Manifest M-5510"),
                S(OperationType.Dispatch, 8, Ids("GEN-0002"), to: "Platform Alpha", party: "Offshore Logistics", reference: "Manifest M-5490"),
                S(OperationType.Dispatch, 1, Ids("WLD-0001"), to: "MV Northern Star", party: "Offshore Logistics", reference: "Manifest M-5512"), S(OperationType.Return, 0.9, Ids("WLD-0001"), to: "Onshore Yard", reference: "Rejected at quay – cert expired"),
                S(OperationType.Maintain, 3, Ids("LFT-0001"), to: "Inspection Bay", reference: "LOLER"), S(OperationType.Maintain, 5, Ids("LR-0002"), to: "Inspection Bay", reference: "Annual service"),
            },
            Reads = { R("Quay 3 Portal", 1, 40, "CCU-0001"), R("Yard Gate Reader", 2, 26, "GEN-0001") },
        },

        // ───────────────────────── School / events / sports ─────────────────────────
        new Scenario
        {
            Template = "education-events-sports", Site = "Riverside Academy & Events", SiteCode = "ACAD", CompanyPrefix = "0614163",
            Locations = { L("IT Office", LocationKind.Room), L("Classroom 2B", LocationKind.Room), L("Charging Cabinet 2B", LocationKind.Cabinet, "Classroom 2B"), L("Sports Store", LocationKind.Room), L("Gym", LocationKind.Room), L("Events Store", LocationKind.Room), L("Venue: Town Hall", LocationKind.External) },
            Parties = { P("Student: Amir Hussain", PartyKind.Person, "S-2031"), P("Student: Ella Fitzgerald", PartyKind.Person, "S-2044"), P("Ms Rowe (Year 8)", PartyKind.Employee), P("PE Department", PartyKind.Department), P("Town Hall Events", PartyKind.Customer) },
            Items =
            {
                I("SCHOOL-DEVICE", "CB-0001", "Chromebook", "Charging Cabinet 2B", "InStock", attrs: Attr(("assetTag", "RA-0001"), ("model", "Lenovo 300e"), ("yearGroup", "8"))),
                I("SCHOOL-DEVICE", "CB-0002", "Chromebook", "Charging Cabinet 2B", "InStock", attrs: Attr(("assetTag", "RA-0002"), ("model", "Lenovo 300e"))),
                I("SCHOOL-DEVICE", "CB-0003", "Chromebook", "Classroom 2B", "Assigned", "Student: Ella Fitzgerald", dueBackDays: -10, attrs: Attr(("assetTag", "RA-0003"), ("model", "Lenovo 300e"))),
                I("SCHOOL-DEVICE", "IPD-0001", "iPad 10th gen", "IT Office", "Repair", attrs: Attr(("assetTag", "RA-0101"))), I("SCHOOL-DEVICE", "IPD-0002", "iPad 10th gen", "Classroom 2B", "Assigned", "Ms Rowe (Year 8)", attrs: Attr(("assetTag", "RA-0102"))),
                I("EVENT-CASE", "CASE-AV-01", "AV case 01 (PA)", "Events Store", "InStore", attrs: Attr(("contentsList", "2x speaker, mixer, 4x mic"), ("weightKg", 48))),
                I("AV-EQUIP", "SPK-0001", "Active speaker 12\"", "Events Store", parent: "CASE-AV-01", attrs: Attr(("category", "Speaker"), ("serial", "QSC-1101"))), I("AV-EQUIP", "SPK-0002", "Active speaker 12\"", "Events Store", parent: "CASE-AV-01", attrs: Attr(("category", "Speaker"), ("serial", "QSC-1102"))),
                I("AV-EQUIP", "MIX-0001", "Mixer 16ch", "Events Store", parent: "CASE-AV-01", attrs: Attr(("category", "Mixer"))), I("AV-EQUIP", "MIC-0001", "Wireless mic", "Events Store", parent: "CASE-AV-01", attrs: Attr(("category", "Microphone"))),
                I("EVENT-CASE", "CASE-LX-02", "Lighting case 02", "Venue: Town Hall", "AtVenue", "Town Hall Events", dueBackDays: 2, attrs: Attr(("contentsList", "8x LED par"), ("weightKg", 30))),
                I("AV-EQUIP", "LED-0001", "LED par", "Venue: Town Hall", parent: "CASE-LX-02", attrs: Attr(("category", "Light"))), I("AV-EQUIP", "LED-0002", "LED par", "Venue: Town Hall", parent: "CASE-LX-02", attrs: Attr(("category", "Light"))),
                I("SPORTS-EQUIP", "RKT-0001", "Badminton racquet", "Sports Store", "InStore", attrs: Attr(("sport", "Badminton"))), I("SPORTS-EQUIP", "RKT-0002", "Badminton racquet", "Sports Store", "InStore", attrs: Attr(("sport", "Badminton"))),
                I("SPORTS-EQUIP", "BALL-0001", "Football size 5 (bag of 10)", "Sports Store", "InStore", attrs: Attr(("sport", "Football"), ("size", "5"))), I("SPORTS-EQUIP", "NET-0001", "Volleyball net", "Sports Store", "Damaged", attrs: Attr(("sport", "Volleyball"))),
            },
            Devices = { D("Charging Cabinet 2B Reader", DeviceKind.Cabinet, "Zebra FX7500", null, Ant(1, "Charging Cabinet 2B")), D("Events Store Reader", DeviceKind.Fixed, "Impinj R700", null, Ant(1, "Events Store")), D("School Handheld", DeviceKind.Handheld, "Zebra RFD40") },
            History =
            {
                S(OperationType.Issue, 30, Ids("CB-0001"), party: "Student: Amir Hussain", to: "Classroom 2B", dueBackDays: 180, reference: "Year 8 device scheme"),
                S(OperationType.Maintain, 2, Ids("IPD-0001"), to: "IT Office", reference: "Cracked screen"),
                S(OperationType.Pack, 5, Ids("SPK-0001", "SPK-0002", "MIX-0001", "MIC-0001"), container: "CASE-AV-01"),
                S(OperationType.ProcessStage, 4, Ids("CASE-AV-01"), state: "Packed", reference: "Spring concert"), S(OperationType.Dispatch, 3.5, Ids("CASE-AV-01"), to: "Venue: Town Hall", party: "Town Hall Events", dueBackDays: 2, reference: "Spring concert"),
                S(OperationType.Return, 1.5, Ids("CASE-AV-01"), to: "Events Store", reference: "Spring concert"), S(OperationType.Count, 1.4, Ids("CASE-AV-01"), to: "Events Store", reference: "Case check-in"),
                S(OperationType.Issue, 0.3, Ids("RKT-0002", "BALL-0001"), party: "PE Department", to: "Gym", dueBackDays: 1, reference: "Period 3"),
                S(OperationType.Inspect, 6, Ids("NET-0001"), state: "Damaged", reference: "Torn"),
            },
            Reads = { R("Charging Cabinet 2B Reader", 1, 2, "CB-0002"), R("Events Store Reader", 1, 1, "CASE-AV-01", "SPK-0001", "SPK-0002", "MIX-0001", "MIC-0001"), R("Charging Cabinet 2B Reader", 1, 1, "CB-0003") },
        },
    };
}

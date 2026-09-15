using Rfid.Domain.Lifecycle;

namespace Rfid.Domain.Entities;

public class ItemType : TenantEntity
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public ItemCategory Category { get; set; } = ItemCategory.Serialized;
    public bool IsContainer { get; set; }
    public bool TracksExpiry { get; set; }
    public bool TracksCycles { get; set; }
    public int? MaxCycles { get; set; }
    public bool RequiresInspection { get; set; }
    public int? InspectionIntervalDays { get; set; }
    public decimal? ReorderPoint { get; set; }
    public string? Unit { get; set; }
    /// <summary>List of attribute definitions {name,type,required,options}.</summary>
    public List<AttributeDefinition> AttributeSchema { get; set; } = new();
    public LifecycleDefinition? Lifecycle { get; set; }
    public string? ImageUrl { get; set; }
    public string? Vertical { get; set; }
    /// <summary>Straight-line depreciation horizon for fixed-asset reporting.</summary>
    public int? UsefulLifeMonths { get; set; }
    /// <summary>Optional ZPL label template; placeholders {name} {identifier} {epc} {type} {location} {attributes.x}.</summary>
    public string? LabelTemplate { get; set; }
    /// <summary>Label designer document (JSON) that LabelTemplate was compiled from.</summary>
    public string? LabelDesign { get; set; }
}

public class AttributeDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "string"; // string|number|date|bool|select
    public bool Required { get; set; }
    public List<string>? Options { get; set; }
}

public class Item : TenantEntity
{
    public Guid ItemTypeId { get; set; }
    public ItemType? ItemType { get; set; }
    /// <summary>Serial / asset number / lot identifier. Unique per tenant.</summary>
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public string? State { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Active;
    public Guid? CurrentLocationId { get; set; }
    public Location? CurrentLocation { get; set; }
    public Guid? CustodianPartyId { get; set; }
    public Party? CustodianParty { get; set; }
    public Guid? ParentItemId { get; set; }
    public Item? ParentItem { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? Unit { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public int CycleCount { get; set; }
    public DateTime? LastInspectedAt { get; set; }
    public DateTime? NextInspectionDue { get; set; }
    public DateTime? DueBackAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public Guid? LastSeenLocationId { get; set; }
    public Guid? LastSeenDeviceId { get; set; }
    /// <summary>Estimated x/y (metres) inside PositionLocationId, from multi-antenna RSSI trilateration.</summary>
    public double? PositionX { get; set; }
    public double? PositionY { get; set; }
    public Guid? PositionLocationId { get; set; }
    public DateTime? PositionAt { get; set; }
    public double? PositionAccuracyM { get; set; }
    /// <summary>Serialised Kalman track state so smoothing continues across API nodes and restarts.</summary>
    public string? PositionTrack { get; set; }
    public decimal? Cost { get; set; }
    public DateOnly? PurchasedAt { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
    public List<Tag> Tags { get; set; } = new();
}

public class Tag : TenantEntity
{
    public string Epc { get; set; } = "";
    public string? Tid { get; set; }
    public TagTechnology Technology { get; set; } = TagTechnology.UhfGen2;
    public Guid? ItemId { get; set; }
    public Item? Item { get; set; }
    public TagStatus Status { get; set; } = TagStatus.Unassigned;
    public DateTime? EncodedAt { get; set; }
}

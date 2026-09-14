using Rfid.Domain;

namespace Rfid.Infrastructure.Persistence.Demo;

/// <summary>Declarative description of a demo dataset for one solution template.</summary>
public class Scenario
{
    public required string Template { get; init; }
    public string[] AlsoRequires { get; init; } = Array.Empty<string>();
    public required string Site { get; init; }
    public required string SiteCode { get; init; }
    /// <summary>GS1 company prefix used to generate SGTIN-96 EPCs for this scenario (7 digits).</summary>
    public required string CompanyPrefix { get; init; }
    public List<LocDef> Locations { get; init; } = new();
    public List<PartyDef> Parties { get; init; } = new();
    public List<ItemDef> Items { get; init; } = new();
    public List<DeviceDef> Devices { get; init; } = new();
    /// <summary>Back-dated operations replayed through the real OperationProcessor (oldest first by DaysAgo).</summary>
    public List<StepDef> History { get; init; } = new();
    /// <summary>Back-dated fixed-reader reads replayed through ReadIngestionService.</summary>
    public List<ReadDef> Reads { get; init; } = new();
}

/// <param name="Parent">Name of another location in the scenario; null = directly under the site (or a root when Kind is Customer/External).</param>
public record LocDef(string Name, LocationKind Kind, string? Parent = null, string? Code = null, Dictionary<string, object?>? Attrs = null, bool Mobile = false);
public record PartyDef(string Name, PartyKind Kind, string? Code = null);
public record ItemDef(string Type, string Identifier, string Name, string? Location = null, string? State = null, string? Custodian = null, string? Parent = null,
    decimal Qty = 1, string? Lot = null, int? ExpiryDays = null, int Cycles = 0, Dictionary<string, object?>? Attrs = null,
    int? LastInspectedDaysAgo = null, int? DueBackInDays = null, int LastSeenHoursAgo = 6, decimal? Cost = null);
public record AntennaDef(int Port, string Location, AntennaDirection Direction = AntennaDirection.None);
public record DeviceDef(string Name, DeviceKind Kind, string Model, AntennaDef[] Antennas, string? Token = null);
public record StepDef(OperationType Type, string[] Items, double DaysAgo, string? To = null, string? Party = null, string? State = null, string? Container = null, decimal? Qty = null, string? Reference = null, int? DueBackDays = null);
public record ReadDef(string Device, int Port, string[] Items, double HoursAgo);

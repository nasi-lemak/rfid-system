using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Integrations;

/// <summary>Everything a formatter needs to build a vendor payload for one delivery batch.</summary>
public record DeliveryBatch(IntegrationEndpoint Endpoint, DateTime SentAt, List<ItemEvent> Events, List<Alert> Alerts,
    Dictionary<Guid, Item> Items, Dictionary<Guid, Location> Locations, Dictionary<Guid, Party> Parties);

public interface IPayloadFormatter { object Build(DeliveryBatch b); }

public static class PayloadFormatters
{
    public static IPayloadFormatter For(IntegrationFormat f) => f switch
    {
        IntegrationFormat.SapAssetManagement => new SapAssetFormatter(),
        IntegrationFormat.Dynamics365 => new Dynamics365Formatter(),
        IntegrationFormat.Maximo => new MaximoFormatter(),
        _ => new GenericFormatter(),
    };

    internal static object? Loc(DeliveryBatch b, Guid? id) => id.HasValue && b.Locations.TryGetValue(id.Value, out var l) ? new { l.Name, l.Code, Kind = l.Kind.ToString() } : null;
    internal static object? Party(DeliveryBatch b, Guid? id) => id.HasValue && b.Parties.TryGetValue(id.Value, out var p) ? new { p.Name, p.Code, Kind = p.Kind.ToString() } : null;
    internal static string? LocName(DeliveryBatch b, Guid? id) => id.HasValue && b.Locations.TryGetValue(id.Value, out var l) ? l.Code ?? l.Name : null;
    internal static string? PartyName(DeliveryBatch b, Guid? id) => id.HasValue && b.Parties.TryGetValue(id.Value, out var p) ? p.Code ?? p.Name : null;
    internal static string M(DeliveryBatch b, string key, string def) => b.Endpoint.Mapping.TryGetValue(key, out var v) && v != null ? v.ToString()! : def;
    internal static string? Attr(Item? i, string key) => i?.Attributes.TryGetValue(key, out var v) == true ? v?.ToString() : null;
}

/// <summary>The platform's own envelope (events + alerts with denormalised item/location/party).</summary>
public class GenericFormatter : IPayloadFormatter
{
    public object Build(DeliveryBatch b) => new
    {
        endpoint = b.Endpoint.Name, sentAt = b.SentAt, tenantId = b.Endpoint.TenantId,
        events = b.Events.Select(x => new
        {
            x.Id, Type = x.Type.ToString(), x.OccurredAt, item = ItemDto(b.Items.GetValueOrDefault(x.ItemId)),
            fromLocation = PayloadFormatters.Loc(b, x.FromLocationId), toLocation = PayloadFormatters.Loc(b, x.ToLocationId),
            fromParty = PayloadFormatters.Party(b, x.FromPartyId), toParty = PayloadFormatters.Party(b, x.ToPartyId),
            x.FromState, x.ToState, x.OperationId, x.DeviceId, x.Data,
        }),
        alerts = b.Alerts.Select(a => new { a.Id, Severity = a.Severity.ToString(), a.Message, Status = a.Status.ToString(), a.RaisedAt, item = a.ItemId.HasValue ? ItemDto(b.Items.GetValueOrDefault(a.ItemId.Value)) : null, location = PayloadFormatters.Loc(b, a.LocationId) }),
    };
    public static object? ItemDto(Item? i) => i == null ? null : new { i.Id, i.Identifier, i.Name, type = i.ItemType?.Code, i.State, Status = i.Status.ToString(), i.Quantity, i.LotNumber, i.Attributes, epc = i.Tags.FirstOrDefault()?.Epc, i.Cost, i.PurchasedAt };
}

/// <summary>
/// SAP S/4HANA Asset Accounting / EAM shape: one "AssetMaster" record per touched item (for upsert via
/// API_FIXEDASSET-style services or a middleware/CPI flow) plus "AssetTransfer" and "AssetMovement" documents
/// for custody and location changes. Mapping keys: companyCode, costCenterAttribute, plant.
/// </summary>
public class SapAssetFormatter : IPayloadFormatter
{
    public object Build(DeliveryBatch b)
    {
        var company = PayloadFormatters.M(b, "companyCode", "1000"); var plant = PayloadFormatters.M(b, "plant", "");
        var ccAttr = PayloadFormatters.M(b, "costCenterAttribute", "costCentre");
        var touched = b.Events.Select(e => e.ItemId).Distinct().Select(id => b.Items.GetValueOrDefault(id)).Where(i => i != null).ToList();
        return new
        {
            MessageHeader = new { ID = Guid.NewGuid().ToString("N"), CreationDateTime = b.SentAt, SenderBusinessSystemID = "RFID-PLATFORM", Endpoint = b.Endpoint.Name },
            AssetMaster = touched.Select(i => new
            {
                CompanyCode = company, AssetNumber = i!.Identifier, AssetDescription = i.Name, AssetClass = i.ItemType?.Code, Plant = plant,
                CostCenter = PayloadFormatters.Attr(i, ccAttr), Location = PayloadFormatters.LocName(b, i.CurrentLocationId), Room = i.CurrentLocation?.Name,
                AcquisitionValue = i.Cost, CapitalizationDate = i.PurchasedAt, InventoryNumber = i.Tags.FirstOrDefault()?.Epc, SerialNumber = PayloadFormatters.Attr(i, "serialNumber") ?? i.Identifier,
                AssetStatus = i.Status.ToString(), UserStatus = i.State, DeactivationDate = i.Status == ItemStatus.Disposed ? (DateTime?)b.SentAt : null,
            }),
            AssetTransfer = b.Events.Where(e => e.Type == ItemEventType.CustodyChanged).Select(e => new { AssetNumber = b.Items.GetValueOrDefault(e.ItemId)?.Identifier, TransferDate = e.OccurredAt, FromPersonnelNumber = PayloadFormatters.PartyName(b, e.FromPartyId), ToPersonnelNumber = PayloadFormatters.PartyName(b, e.ToPartyId), DocumentReference = e.OperationId }),
            AssetMovement = b.Events.Where(e => e.Type == ItemEventType.Moved).Select(e => new { AssetNumber = b.Items.GetValueOrDefault(e.ItemId)?.Identifier, MovementDate = e.OccurredAt, FromLocation = PayloadFormatters.LocName(b, e.FromLocationId), ToLocation = PayloadFormatters.LocName(b, e.ToLocationId), Direction = e.Data.GetValueOrDefault("direction")?.ToString() }),
            AssetRetirement = b.Events.Where(e => e.Type == ItemEventType.Disposed).Select(e => new { AssetNumber = b.Items.GetValueOrDefault(e.ItemId)?.Identifier, RetirementDate = e.OccurredAt }),
            Notifications = b.Alerts.Select(a => new { NotificationType = a.Severity == Severity.Critical ? "M2" : "M1", ShortText = a.Message, AssetNumber = a.ItemId.HasValue ? b.Items.GetValueOrDefault(a.ItemId.Value)?.Identifier : null, ReportedAt = a.RaisedAt }),
        };
    }
}

/// <summary>Dynamics 365 Field Service / Supply Chain shape: msdyn_customerasset upserts keyed by the identifier, plus asset transaction rows. Mapping keys: accountId, siteId.</summary>
public class Dynamics365Formatter : IPayloadFormatter
{
    public object Build(DeliveryBatch b)
    {
        var account = PayloadFormatters.M(b, "accountId", ""); var site = PayloadFormatters.M(b, "siteId", "");
        var touched = b.Events.Select(e => e.ItemId).Concat(b.Alerts.Where(a => a.ItemId.HasValue).Select(a => a.ItemId!.Value)).Distinct().Select(id => b.Items.GetValueOrDefault(id)).Where(i => i != null).ToList();
        return new
        {
            value = touched.Select(i => new Dictionary<string, object?>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.msdyn_customerasset", ["msdyn_name"] = i!.Name, ["msdyn_serialnumber"] = i.Identifier, ["msdyn_productid"] = i.ItemType?.Code,
                ["msdyn_customerasset_rfid_epc"] = i.Tags.FirstOrDefault()?.Epc, ["msdyn_customerasset_location"] = PayloadFormatters.LocName(b, i.CurrentLocationId), ["msdyn_customerasset_state"] = i.State,
                ["msdyn_customerasset_status"] = i.Status.ToString(), ["msdyn_customerasset_custodian"] = PayloadFormatters.PartyName(b, i.CustodianPartyId), ["msdyn_account@odata.bind"] = account == "" ? null : $"/accounts({account})", ["msdyn_site"] = site == "" ? null : site,
                ["msdyn_lastseen"] = i.LastSeenAt,
            }),
            transactions = b.Events.Select(e => new { transactionType = e.Type.ToString(), serialnumber = b.Items.GetValueOrDefault(e.ItemId)?.Identifier, occurredon = e.OccurredAt, fromlocation = PayloadFormatters.LocName(b, e.FromLocationId), tolocation = PayloadFormatters.LocName(b, e.ToLocationId), fromcustodian = PayloadFormatters.PartyName(b, e.FromPartyId), tocustodian = PayloadFormatters.PartyName(b, e.ToPartyId), fromstate = e.FromState, tostate = e.ToState, reference = e.OperationId }),
            alerts = b.Alerts.Select(a => new { subject = a.Message, prioritycode = a.Severity == Severity.Critical ? 1 : a.Severity == Severity.Warning ? 2 : 3, serialnumber = a.ItemId.HasValue ? b.Items.GetValueOrDefault(a.ItemId.Value)?.Identifier : null, createdon = a.RaisedAt }),
        };
    }
}

/// <summary>IBM Maximo MXASSET object structure (REST/OSLC "MXASSET" or MIF) with ASSETNUM upserts, ASSETTRANS for moves and SR for alerts. Mapping keys: siteId, orgId.</summary>
public class MaximoFormatter : IPayloadFormatter
{
    public object Build(DeliveryBatch b)
    {
        var site = PayloadFormatters.M(b, "siteId", "SITE1"); var org = PayloadFormatters.M(b, "orgId", "ORG1");
        var touched = b.Events.Select(e => e.ItemId).Distinct().Select(id => b.Items.GetValueOrDefault(id)).Where(i => i != null).ToList();
        return new Dictionary<string, object?>
        {
            ["MXASSET"] = new
            {
                ASSET = touched.Select(i => new Dictionary<string, object?>
                {
                    ["ASSETNUM"] = i!.Identifier, ["DESCRIPTION"] = i.Name, ["SITEID"] = site, ["ORGID"] = org, ["ASSETTYPE"] = i.ItemType?.Code, ["LOCATION"] = PayloadFormatters.LocName(b, i.CurrentLocationId),
                    ["STATUS"] = i.Status == ItemStatus.Disposed ? "DECOMMISSIONED" : i.Status == ItemStatus.Missing ? "MISSING" : "OPERATING", ["SERIALNUM"] = PayloadFormatters.Attr(i, "serialNumber") ?? i.Identifier, ["RFIDTAG"] = i.Tags.FirstOrDefault()?.Epc,
                    ["PURCHASEPRICE"] = i.Cost, ["INSTALLDATE"] = i.PurchasedAt, ["CUSTODIAN"] = PayloadFormatters.PartyName(b, i.CustodianPartyId), ["RFIDSTATE"] = i.State,
                    ["ASSETTRANS"] = b.Events.Where(e => e.ItemId == i.Id && e.Type == ItemEventType.Moved).Select(e => new { DATEMOVED = e.OccurredAt, FROMLOC = PayloadFormatters.LocName(b, e.FromLocationId), TOLOC = PayloadFormatters.LocName(b, e.ToLocationId), TRANSDATE = e.OccurredAt }),
                }),
            },
            ["MXSR"] = new { SR = b.Alerts.Select(a => new { DESCRIPTION = a.Message, REPORTDATE = a.RaisedAt, ASSETNUM = a.ItemId.HasValue ? b.Items.GetValueOrDefault(a.ItemId.Value)?.Identifier : null, SITEID = site, REPORTEDPRIORITY = a.Severity == Severity.Critical ? 1 : a.Severity == Severity.Warning ? 2 : 3, STATUS = "NEW" }) },
        };
    }
}

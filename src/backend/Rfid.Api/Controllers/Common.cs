using Microsoft.AspNetCore.Mvc;
using Rfid.Domain.Entities;

namespace Rfid.Api.Controllers;

public record Paged<T>(List<T> Items, int Total, int Page, int PageSize);

public static class Dto
{
    public static object Item(Item i) => new
    {
        i.Id, i.ItemTypeId, ItemType = i.ItemType == null ? null : new { i.ItemType.Id, i.ItemType.Name, i.ItemType.Code, i.ItemType.Category, i.ItemType.IsContainer, i.ItemType.Lifecycle, i.ItemType.AttributeSchema, i.ItemType.MaxCycles, i.ItemType.Unit },
        i.Identifier, i.Name, i.State, i.Status, i.CurrentLocationId,
        CurrentLocation = i.CurrentLocation == null ? null : new { i.CurrentLocation.Id, i.CurrentLocation.Name, i.CurrentLocation.Kind, i.CurrentLocation.Code },
        i.CustodianPartyId, CustodianParty = i.CustodianParty == null ? null : new { i.CustodianParty.Id, i.CustodianParty.Name, i.CustodianParty.Kind },
        i.ParentItemId, ParentItem = i.ParentItem == null ? null : new { i.ParentItem.Id, i.ParentItem.Name, i.ParentItem.Identifier },
        i.Quantity, i.Unit, i.LotNumber, i.ExpiryDate, i.CycleCount, i.LastInspectedAt, i.NextInspectionDue, i.DueBackAt,
        i.LastSeenAt, i.LastSeenLocationId, i.LastSeenDeviceId, i.Cost, i.PurchasedAt, i.Attributes, i.CreatedAt,
        i.PositionX, i.PositionY, i.PositionLocationId, i.PositionAt, i.PositionAccuracyM,
        Tags = i.Tags.Select(t => new { t.Id, t.Epc, t.Tid, t.Technology, t.Status }).ToList(),
    };

    public static object Location(Location l) => new { l.Id, l.ParentId, l.Kind, l.Name, l.Code, l.Path, l.Latitude, l.Longitude, l.IsMobile, l.Attributes };

    public static object Event(ItemEvent e, Item? item = null) => new
    {
        e.Id, e.ItemId, ItemName = item?.Name, ItemIdentifier = item?.Identifier, e.Type, e.FromLocationId, e.ToLocationId, e.FromPartyId, e.ToPartyId,
        e.FromState, e.ToState, e.OperationId, e.DeviceId, e.UserId, e.OccurredAt, e.Data,
    };

    public static object Alert(Alert a, Item? item = null) => new
    {
        a.Id, a.RuleId, a.ItemId, ItemName = item?.Name, ItemIdentifier = item?.Identifier, a.LocationId, a.Severity, a.Message, a.Status, a.RaisedAt, a.AcknowledgedBy, a.ClosedAt,
    };
}

public static class Query
{
    public static (int page, int size) Page(int? page, int? pageSize) => (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 50, 1, 500));
}

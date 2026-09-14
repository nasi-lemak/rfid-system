using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Sends the caller-prepared body to an endpoint; the API implements it with HttpClient, tests with a fake.</summary>
public interface IIntegrationTransport
{
    Task<(bool ok, string? error)> PostAsync(string url, string body, IDictionary<string, string> headers, CancellationToken ct);
}

/// <summary>
/// Cursor-based outbound delivery of item events and alerts to ERP/EAM/BI endpoints. Each endpoint keeps its
/// own cursor, so a slow or failing system never loses data; failures back off exponentially (max ~1 h).
/// </summary>
public class IntegrationService
{
    private readonly IAppDb _db;
    private readonly IIntegrationTransport _transport;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public IntegrationService(IAppDb db, IIntegrationTransport transport) { _db = db; _transport = transport; }

    public async Task<int> DispatchAllAsync(DateTime now, CancellationToken ct = default)
    {
        var endpoints = await _db.IntegrationEndpoints.Where(e => e.Enabled && (e.NextAttemptAt == null || e.NextAttemptAt <= now)).ToListAsync(ct);
        var delivered = 0;
        foreach (var e in endpoints) delivered += await DispatchAsync(e, now, ct);
        return delivered;
    }

    public async Task<int> DispatchAsync(IntegrationEndpoint e, DateTime now, CancellationToken ct = default)
    {
        var take = Math.Clamp(e.BatchSize, 1, 1000);
        var q = _db.ItemEvents.Where(x => x.OccurredAt > e.EventCursor && x.OccurredAt <= now);
        if (e.EventTypes.Count > 0) q = q.Where(x => e.EventTypes.Contains(x.Type));
        var events = await q.OrderBy(x => x.OccurredAt).Take(take).ToListAsync(ct);
        var alerts = e.IncludeAlerts ? await _db.Alerts.Where(a => a.RaisedAt > e.AlertCursor && a.RaisedAt <= now).OrderBy(a => a.RaisedAt).Take(take).ToListAsync(ct) : new();
        if (events.Count == 0 && alerts.Count == 0) return 0;

        var itemIds = events.Select(x => x.ItemId).Concat(alerts.Where(a => a.ItemId.HasValue).Select(a => a.ItemId!.Value)).Distinct().ToList();
        var items = await _db.Items.Include(i => i.ItemType).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, l => new { l.Name, l.Code, Kind = l.Kind.ToString() }, ct);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, p => new { p.Name, p.Code, Kind = p.Kind.ToString() }, ct);
        var payload = new
        {
            endpoint = e.Name, sentAt = now, tenantId = e.TenantId,
            events = events.Select(x => new
            {
                x.Id, x.Type, x.OccurredAt, item = Item(items.GetValueOrDefault(x.ItemId)),
                fromLocation = x.FromLocationId.HasValue ? locs.GetValueOrDefault(x.FromLocationId.Value) : null, toLocation = x.ToLocationId.HasValue ? locs.GetValueOrDefault(x.ToLocationId.Value) : null,
                fromParty = x.FromPartyId.HasValue ? parties.GetValueOrDefault(x.FromPartyId.Value) : null, toParty = x.ToPartyId.HasValue ? parties.GetValueOrDefault(x.ToPartyId.Value) : null,
                x.FromState, x.ToState, x.OperationId, x.DeviceId, x.Data,
            }),
            alerts = alerts.Select(a => new { a.Id, a.Severity, a.Message, a.Status, a.RaisedAt, item = a.ItemId.HasValue ? Item(items.GetValueOrDefault(a.ItemId.Value)) : null, location = a.LocationId.HasValue ? locs.GetValueOrDefault(a.LocationId.Value) : null }),
        };
        var body = JsonSerializer.Serialize(payload, Json);
        var headers = e.Headers.Where(h => h.Value != null).ToDictionary(h => h.Key, h => h.Value!.ToString()!);
        headers["X-Rfid-Endpoint"] = e.Name;
        headers["X-Rfid-Timestamp"] = now.ToString("O");
        if (!string.IsNullOrEmpty(e.Secret)) headers["X-Rfid-Signature"] = Sign(e.Secret, body);

        var (ok, error) = await _transport.PostAsync(e.Url, body, headers, ct);
        if (ok)
        {
            if (events.Count > 0) e.EventCursor = events[^1].OccurredAt;
            if (alerts.Count > 0) e.AlertCursor = alerts[^1].RaisedAt;
            e.LastDeliveryAt = now; e.LastError = null; e.FailureCount = 0; e.NextAttemptAt = null; e.DeliveredCount += events.Count + alerts.Count;
        }
        else
        {
            e.FailureCount++; e.LastError = error;
            e.NextAttemptAt = now.AddSeconds(Math.Min(3600, 10 * Math.Pow(2, Math.Min(e.FailureCount, 9))));
        }
        await _db.SaveChangesAsync(ct);
        return ok ? events.Count + alerts.Count : 0;
    }

    private static object? Item(Item? i) => i == null ? null : new { i.Id, i.Identifier, i.Name, type = i.ItemType?.Code, i.State, i.Status, i.Quantity, i.LotNumber, i.Attributes, epc = i.Tags.FirstOrDefault()?.Epc };

    public static string Sign(string secret, string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Integrations;
using Rfid.Application.Platform;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Sends the caller-prepared body to an endpoint; the API implements it with HttpClient, tests with a fake.</summary>
public interface IIntegrationTransport
{
    Task<(bool ok, string? error)> PostAsync(string url, string body, IDictionary<string, string> headers, CancellationToken ct);
}

/// <summary>Fetches OAuth2 client-credentials tokens (HTTP in the API, fake in tests).</summary>
public interface IOAuthTokenProvider { Task<string> GetTokenAsync(string tokenUrl, string clientId, string clientSecret, string? scope, CancellationToken ct); }

/// <summary>
/// Outbound delivery of item events and alerts to ERP/EAM/BI endpoints.
///
/// The endpoint's <b>cursor</b> is the source of truth (nothing is ever skipped); the <b>outbox</b> is the single
/// delivery/retry mechanism. <see cref="EnqueueAsync"/> builds the next batch after the cursor, writes it as an
/// <c>integration</c> outbox message and remembers it as in flight; <see cref="IntegrationOutboxHandler"/> posts
/// it and, on success, advances the cursor. One message in flight per endpoint keeps ordering; a dead-lettered
/// message only clears the in-flight marker, so the next pass rebuilds the same data from the cursor.
/// </summary>
public class IntegrationService
{
    private readonly IAppDb _db;
    private readonly Outbox _outbox;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    /// <summary>Vendor formats keep their exact field names (SAP PascalCase, Maximo UPPERCASE); the generic envelope is camelCase.</summary>
    private static readonly JsonSerializerOptions VendorJson = new() { PropertyNamingPolicy = null, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static JsonSerializerOptions SerializerFor(IntegrationFormat f) => f == IntegrationFormat.Generic ? Json : VendorJson;
    public IntegrationService(IAppDb db, Outbox outbox) { _db = db; _outbox = outbox; }

    /// <summary>Payload of an <c>integration</c> outbox message.</summary>
    public record Delivery(Guid EndpointId, string Body, DateTime? EventCursorTo, DateTime? AlertCursorTo, int Events, int Alerts);

    /// <summary>Builds and enqueues the next batch for every enabled endpoint without one in flight. Returns the number of records enqueued.</summary>
    public async Task<int> DispatchAllAsync(DateTime now, CancellationToken ct = default)
    {
        var endpoints = await _db.IntegrationEndpoints.Where(e => e.Enabled && (e.NextAttemptAt == null || e.NextAttemptAt <= now)).ToListAsync(ct);
        var n = 0;
        foreach (var e in endpoints) n += await EnqueueAsync(e, now, ct);
        return n;
    }

    public async Task<int> EnqueueAsync(IntegrationEndpoint e, DateTime now, CancellationToken ct = default)
    {
        if (e.InFlightMessageId is Guid inflight)
        {
            var pending = await _db.Outbox.AnyAsync(m => m.Id == inflight && m.ProcessedAt == null, ct);
            if (pending) return 0;                       // ordering: one batch at a time per endpoint
            e.InFlightMessageId = null;
        }
        var take = Math.Clamp(e.BatchSize, 1, 1000);
        var q = _db.ItemEvents.Where(x => x.OccurredAt > e.EventCursor && x.OccurredAt <= now);
        if (e.EventTypes.Count > 0) q = q.Where(x => e.EventTypes.Contains(x.Type));
        IQueryable<Guid>? allowedItems = null;
        if (e.ItemTypeCodes.Count > 0) allowedItems = _db.Items.Where(i => e.ItemTypeCodes.Contains(i.ItemType!.Code)).Select(i => i.Id);
        if (allowedItems != null) q = q.Where(x => allowedItems.Contains(x.ItemId));
        if (e.SiteLocationId is Guid site)
        {
            var sitePath = await _db.Locations.Where(l => l.Id == site).Select(l => l.Path).FirstOrDefaultAsync(ct);
            if (sitePath != null)
            {
                var inSite = _db.Locations.Where(l => l.Path.StartsWith(sitePath)).Select(l => l.Id);
                var itemsInSite = _db.Items.Where(i => i.CurrentLocationId != null && inSite.Contains(i.CurrentLocationId.Value)).Select(i => i.Id);
                q = q.Where(x => (x.ToLocationId != null && inSite.Contains(x.ToLocationId.Value)) || (x.FromLocationId != null && inSite.Contains(x.FromLocationId.Value)) || itemsInSite.Contains(x.ItemId));
            }
        }
        var events = await q.OrderBy(x => x.OccurredAt).Take(take).ToListAsync(ct);
        var aq = _db.Alerts.Where(a => a.RaisedAt > e.AlertCursor && a.RaisedAt <= now);
        if (allowedItems != null) aq = aq.Where(a => a.ItemId != null && allowedItems.Contains(a.ItemId.Value));
        var alerts = e.IncludeAlerts ? await aq.OrderBy(a => a.RaisedAt).Take(take).ToListAsync(ct) : new();
        if (events.Count == 0 && alerts.Count == 0) return 0;

        var itemIds = events.Select(x => x.ItemId).Concat(alerts.Where(a => a.ItemId.HasValue).Select(a => a.ItemId!.Value)).Distinct().ToList();
        var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, ct);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, ct);
        var payload = PayloadFormatters.For(e.Format).Build(new DeliveryBatch(e, now, events, alerts, items, locs, parties));
        var body = JsonSerializer.Serialize(payload, SerializerFor(e.Format));
        var m = _outbox.Enqueue(OutboxKinds.Integration, new Delivery(e.Id, body, events.Count > 0 ? events[^1].OccurredAt : null, alerts.Count > 0 ? alerts[^1].RaisedAt : null, events.Count, alerts.Count), e.Url, e.TenantId);
        e.InFlightMessageId = m.Id;
        await _db.SaveChangesAsync(ct);
        return events.Count + alerts.Count;
    }

    /// <summary>Request headers for a body: HMAC signature, custom headers and authentication, resolved at send time so tokens are fresh.</summary>
    public static async Task<Dictionary<string, string>> HeadersAsync(IntegrationEndpoint e, string body, DateTime now, IOAuthTokenProvider? oauth, CancellationToken ct)
    {
        var headers = e.Headers.Where(h => h.Value != null).ToDictionary(h => h.Key, h => h.Value!.ToString()!);
        headers["X-Rfid-Endpoint"] = e.Name;
        headers["X-Rfid-Timestamp"] = now.ToString("O");
        if (!string.IsNullOrEmpty(e.Secret)) headers["X-Rfid-Signature"] = Sign(e.Secret, body);
        switch (e.AuthType)
        {
            case IntegrationAuth.Bearer when !string.IsNullOrEmpty(e.ApiToken): headers["Authorization"] = "Bearer " + e.ApiToken; break;
            case IntegrationAuth.Basic: headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{e.Username}:{e.Password}")); break;
            case IntegrationAuth.OAuth2ClientCredentials:
                if (oauth == null || string.IsNullOrEmpty(e.TokenUrl)) throw new InvalidOperationException("OAuth2 token endpoint not configured");
                headers["Authorization"] = "Bearer " + await oauth.GetTokenAsync(e.TokenUrl, e.ClientId ?? "", e.ClientSecret ?? "", e.Scope, ct);
                break;
        }
        return headers;
    }

    public static string Sign(string secret, string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

/// <summary>Delivers <c>integration</c> outbox messages and moves the endpoint cursor on success.</summary>
public sealed class IntegrationOutboxHandler : IOutboxHandler
{
    private readonly IAppDb _db; private readonly IIntegrationTransport _transport; private readonly IOAuthTokenProvider? _oauth;
    public IntegrationOutboxHandler(IAppDb db, IIntegrationTransport transport, IOAuthTokenProvider? oauth = null) { _db = db; _transport = transport; _oauth = oauth; }
    public string Kind => OutboxKinds.Integration;

    public async Task DeliverAsync(OutboxMessage m, CancellationToken ct)
    {
        var d = JsonSerializer.Deserialize<IntegrationService.Delivery>(m.Payload, Outbox.Json) ?? throw new InvalidOperationException("bad integration payload");
        var e = await _db.IntegrationEndpoints.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.EndpointId, ct);
        if (e == null) return;                                           // endpoint deleted: nothing to deliver to
        var now = DateTime.UtcNow;
        var headers = await IntegrationService.HeadersAsync(e, d.Body, now, _oauth, ct);
        var (ok, error) = await _transport.PostAsync(e.Url, d.Body, headers, ct);
        if (!ok)
        {
            e.FailureCount++; e.LastError = error; e.NextAttemptAt = m.NextAttemptAt;
            throw new InvalidOperationException(error ?? "integration endpoint failed");
        }
        if (d.EventCursorTo is DateTime ec && ec > e.EventCursor) e.EventCursor = ec;
        if (d.AlertCursorTo is DateTime ac && ac > e.AlertCursor) e.AlertCursor = ac;
        e.LastDeliveryAt = now; e.LastError = null; e.FailureCount = 0; e.NextAttemptAt = null; e.DeliveredCount += d.Events + d.Alerts;
        if (e.InFlightMessageId == m.Id) e.InFlightMessageId = null;
    }

    public async Task OnFailureAsync(OutboxMessage m, bool deadLettered, CancellationToken ct)
    {
        var d = JsonSerializer.Deserialize<IntegrationService.Delivery>(m.Payload, Outbox.Json); if (d == null) return;
        var e = await _db.IntegrationEndpoints.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.EndpointId, ct); if (e == null) return;
        e.NextAttemptAt = deadLettered ? DateTime.UtcNow.AddMinutes(15) : m.NextAttemptAt;
        if (deadLettered) { e.InFlightMessageId = null; e.LastError = (m.Error ?? "delivery failed") + " — batch dead-lettered; it will be rebuilt from the cursor"; }
    }
}

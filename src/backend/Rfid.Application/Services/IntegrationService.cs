using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Application.Integrations;
using Rfid.Domain;
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
/// <summary>Fetches OAuth2 client-credentials tokens (HTTP in the API, fake in tests).</summary>
public interface IOAuthTokenProvider { Task<string> GetTokenAsync(string tokenUrl, string clientId, string clientSecret, string? scope, CancellationToken ct); }

public class IntegrationService
{
    private readonly IAppDb _db;
    private readonly IIntegrationTransport _transport;
    private readonly IOAuthTokenProvider? _oauth;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    /// <summary>Vendor formats keep their exact field names (SAP PascalCase, Maximo UPPERCASE); the generic envelope is camelCase.</summary>
    private static readonly JsonSerializerOptions VendorJson = new() { PropertyNamingPolicy = null, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public static JsonSerializerOptions SerializerFor(IntegrationFormat f) => f == IntegrationFormat.Generic ? Json : VendorJson;
    public IntegrationService(IAppDb db, IIntegrationTransport transport, IOAuthTokenProvider? oauth = null) { _db = db; _transport = transport; _oauth = oauth; }

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
        var items = await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.Tags).Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var locs = await _db.Locations.ToDictionaryAsync(l => l.Id, ct);
        var parties = await _db.Parties.ToDictionaryAsync(p => p.Id, ct);
        var payload = PayloadFormatters.For(e.Format).Build(new DeliveryBatch(e, now, events, alerts, items, locs, parties));
        var body = JsonSerializer.Serialize(payload, SerializerFor(e.Format));
        var headers = e.Headers.Where(h => h.Value != null).ToDictionary(h => h.Key, h => h.Value!.ToString()!);
        headers["X-Rfid-Endpoint"] = e.Name;
        headers["X-Rfid-Timestamp"] = now.ToString("O");
        if (!string.IsNullOrEmpty(e.Secret)) headers["X-Rfid-Signature"] = Sign(e.Secret, body);
        try
        {
            switch (e.AuthType)
            {
                case IntegrationAuth.Bearer when !string.IsNullOrEmpty(e.ApiToken): headers["Authorization"] = "Bearer " + e.ApiToken; break;
                case IntegrationAuth.Basic: headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{e.Username}:{e.Password}")); break;
                case IntegrationAuth.OAuth2ClientCredentials:
                    if (_oauth == null || string.IsNullOrEmpty(e.TokenUrl)) throw new InvalidOperationException("OAuth2 token endpoint not configured");
                    headers["Authorization"] = "Bearer " + await _oauth.GetTokenAsync(e.TokenUrl, e.ClientId ?? "", e.ClientSecret ?? "", e.Scope, ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            e.FailureCount++; e.LastError = "auth: " + ex.Message; e.NextAttemptAt = now.AddSeconds(Math.Min(3600, 10 * Math.Pow(2, Math.Min(e.FailureCount, 9))));
            await _db.SaveChangesAsync(ct); return 0;
        }

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


    public static string Sign(string secret, string body)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

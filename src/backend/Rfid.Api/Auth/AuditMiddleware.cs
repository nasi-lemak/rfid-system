using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Auth;

/// <summary>
/// Records every mutating API call (POST/PUT/DELETE) as an AuditEntry: who, what route, which entity, status and a
/// redacted copy of the request body. High-volume ingest and auth endpoints are excluded; secrets are masked.
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next; private readonly ILogger<AuditMiddleware> _log;
    private static readonly string[] Skip = { "/api/ingest/", "/api/auth/login", "/api/auth/device", "/api/devices/heartbeat", "/heartbeat", "/api/dashboards/evaluate", "/api/encoding/preview", "/api/labels/designs/compile", "/api/reports/" };
    private static readonly HashSet<string> Secrets = new(StringComparer.OrdinalIgnoreCase) { "password", "token", "authToken", "clientSecret", "secret", "apiKey" };
    public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> log) { _next = next; _log = log; }

    public async Task Invoke(HttpContext ctx, AppDbContext db)
    {
        var method = ctx.Request.Method;
        var path = ctx.Request.Path.Value ?? "";
        if (method is not ("POST" or "PUT" or "DELETE" or "PATCH") || !path.StartsWith("/api/") || Skip.Any(s => path.Contains(s, StringComparison.OrdinalIgnoreCase))) { await _next(ctx); return; }
        string? body = null;
        if (ctx.Request.ContentLength is > 0 and < 64 * 1024 && (ctx.Request.ContentType?.Contains("json") ?? false))
        {
            ctx.Request.EnableBuffering();
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
            body = await reader.ReadToEndAsync(); ctx.Request.Body.Position = 0;
        }
        var sw = Stopwatch.StartNew();
        await _next(ctx);
        sw.Stop();
        try
        {
            var user = ctx.User;
            if (user.Identity?.IsAuthenticated != true) return;
            var tenant = Guid.TryParse(user.FindFirst("tenant")?.Value, out var t) ? t : Guid.Empty;
            if (tenant == Guid.Empty) return;
            var descriptor = ctx.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            var entityId = ctx.Request.RouteValues.TryGetValue("id", out var idv) && Guid.TryParse(idv?.ToString(), out var eid) ? eid : (Guid?)null;
            var entry = new AuditEntry
            {
                TenantId = tenant, UserId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null, UserName = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value,
                DeviceId = Guid.TryParse(user.FindFirst("device")?.Value, out var did) ? did : null,
                Method = method, Path = path.Length > 500 ? path[..500] : path, Action = descriptor != null ? $"{descriptor.ControllerName}.{descriptor.ActionName}" : path,
                EntityType = descriptor?.ControllerName, EntityId = entityId, StatusCode = ctx.Response.StatusCode, Body = Redact(body), IpAddress = ctx.Connection.RemoteIpAddress?.ToString(),
                UserAgent = ctx.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? (ua.Length > 200 ? ua[..200] : ua) : null, At = DateTime.UtcNow, DurationMs = (int)sw.ElapsedMilliseconds,
            };
            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception ex) { _log.LogDebug(ex, "Audit write failed for {Path}", path); }
    }

    public static string? Redact(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var node = JsonNode.Parse(body); if (node == null) return null;
            Walk(node);
            var s = node.ToJsonString(); return s.Length > 4000 ? s[..4000] + "…" : s;
        }
        catch { return body.Length > 4000 ? body[..4000] + "…" : body; }
    }
    private static void Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList()) { if (Secrets.Contains(key)) o[key] = "***"; else if (o[key] is { } child) Walk(child); }
                break;
            case JsonArray a: foreach (var child in a) if (child != null) Walk(child); break;
        }
    }
}

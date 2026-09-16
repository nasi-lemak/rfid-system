using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Rfid.Domain.Entities;

namespace Rfid.Api.Auth;

public class JwtService
{
    private readonly IConfiguration _cfg;
    public JwtService(IConfiguration cfg) => _cfg = cfg;

    public string IssueForUser(User user, IEnumerable<UserSiteAccess>? sites = null) =>
        Issue(PlatformClaims.ForUser(user, sites ?? Array.Empty<UserSiteAccess>()), TimeSpan.FromMinutes(_cfg.GetValue("Jwt:ExpiresMinutes", 720)));

    public string IssueForDevice(Device device) => Issue(new[]
    {
        new Claim(ClaimTypes.Name, device.Name),
        new Claim(ClaimTypes.Role, "Device"),
        new Claim("device", device.Id.ToString()),
        new Claim("tenant", device.TenantId.ToString()),
    }, TimeSpan.FromDays(30));

    private string Issue(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var token = new JwtSecurityToken(_cfg["Jwt:Issuer"] ?? "rfid-platform", null, claims,
            expires: DateTime.UtcNow.Add(lifetime), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _log;
    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log) { _next = next; _log = log; }

    public async Task Invoke(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (Rfid.Application.Contracts.NotFoundException ex)
        {
            ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Rfid.Application.Contracts.DomainException ex)
        {
            ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _log.LogWarning(ex, "DB update failed");
            ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "Conflict: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }
}

/// <summary>HTTP transport for outbox webhooks (retries are the dispatcher's job).</summary>
public class HttpWebhookTransport : Rfid.Application.Platform.IWebhookTransport
{
    private readonly HttpClient _http;
    public HttpWebhookTransport(HttpClient http) { _http = http; _http.Timeout = TimeSpan.FromSeconds(20); }
    public async Task<(bool ok, string? error)> PostJsonAsync(string url, string json, CancellationToken ct = default)
    {
        try
        {
            using var res = await _http.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            return res.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { return (false, ex.Message); }
    }
}

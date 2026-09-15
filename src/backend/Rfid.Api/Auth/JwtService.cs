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

public class HttpWebhookDispatcher : Rfid.Application.Contracts.IWebhookDispatcher
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpWebhookDispatcher> _log;
    public HttpWebhookDispatcher(HttpClient http, ILogger<HttpWebhookDispatcher> log) { _http = http; _log = log; }
    public async Task SendAsync(string url, object payload, CancellationToken ct = default)
    {
        try { await _http.PostAsJsonAsync(url, payload, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Webhook to {Url} failed", url); }
    }
}

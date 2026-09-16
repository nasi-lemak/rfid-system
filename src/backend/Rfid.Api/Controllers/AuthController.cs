using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Api.Auth;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Application.Services;
using Microsoft.Extensions.Options;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly ICurrentContext _ctx; private readonly IOptions<SsoOptions> _sso;
    public AuthController(AppDbContext db, JwtService jwt, ICurrentContext ctx, IOptions<SsoOptions> sso) { _db = db; _jwt = jwt; _ctx = ctx; _sso = sso; }

    /// <summary>Public sign-in configuration for the web/mobile clients (whether SSO is on and where to redirect).</summary>
    [HttpGet("config"), AllowAnonymous]
    public IActionResult Config()
    {
        var o = _sso.Value;
        var enabled = o.Enabled && !string.IsNullOrWhiteSpace(o.Authority) && !string.IsNullOrWhiteSpace(o.ClientId);
        return Ok(new { PasswordLogin = true, Sso = enabled ? new { o.Authority, o.ClientId, o.Scopes, o.Audience } : null });
    }

    /// <summary>TenantCode disambiguates an e-mail that exists in more than one tenant (optional otherwise).</summary>
    public record LoginRequest(string Email, string Password, string? TenantCode = null);
    public record DeviceLoginRequest(string Token);

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var candidates = await _db.Users.IgnoreQueryFilters().Where(u => u.Email == email && u.IsActive).Take(3).ToListAsync();
        if (!string.IsNullOrWhiteSpace(req.TenantCode))
        {
            var code = req.TenantCode.Trim();
            var tenantIds = await _db.Tenants.Where(t => t.Code == code).Select(t => t.Id).ToListAsync();
            candidates = candidates.Where(u => tenantIds.Contains(u.TenantId)).ToList();
        }
        if (candidates.Count > 1) return Unauthorized(new { error = "This e-mail exists in more than one tenant; supply tenantCode", ambiguous = true });
        var user = candidates.SingleOrDefault();
        if (user == null || !PasswordHasher.Verify(req.Password, user.PasswordHash)) return Unauthorized(new { error = "Invalid credentials" });
        var tenant = await _db.Tenants.FindAsync(user.TenantId);
        var sites = user.RestrictToSites ? await _db.UserSiteAccess.IgnoreQueryFilters().Where(s => s.UserId == user.Id).ToListAsync() : new List<Rfid.Domain.Entities.UserSiteAccess>();
        user.LastLoginAt = DateTime.UtcNow; await _db.SaveChangesAsync();
        return Ok(new { token = _jwt.IssueForUser(user, sites), user = new { user.Id, user.Email, user.DisplayName, Role = user.Role.ToString(), user.TenantId, TenantName = tenant?.Name, user.RestrictToSites, user.PortalPartyId, Sites = sites.Select(s => new { s.SiteLocationId, s.Role }) } });
    }

    /// <summary>Fixed readers and handhelds exchange their provisioning token for a JWT.</summary>
    [HttpPost("device"), AllowAnonymous]
    public async Task<IActionResult> DeviceLogin(DeviceLoginRequest req)
    {
        var hash = PasswordHasher.HashToken(req.Token);
        var device = await _db.Devices.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.TokenHash == hash);
        if (device == null) return Unauthorized(new { error = "Unknown device token" });
        return Ok(new { token = _jwt.IssueForDevice(device), device = new { device.Id, device.Name, Kind = device.Kind.ToString(), device.TenantId } });
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        if (_ctx.UserId is Guid uid)
        {
            var user = await _db.Users.FindAsync(uid);
            var (restricted, _, siteRoles) = PlatformClaims.Parse(User);
            var siteNames = restricted ? await _db.Locations.Where(l => siteRoles.Keys.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name) : new Dictionary<Guid, string>();
            return Ok(new { user?.Id, user?.Email, user?.DisplayName, Role = user?.Role.ToString(), _ctx.TenantId, RestrictToSites = restricted, Sso = User.FindFirst("sso")?.Value, user?.PortalPartyId,
                Sites = siteRoles.Select(kv => new { SiteLocationId = kv.Key, SiteName = siteNames.GetValueOrDefault(kv.Key), Role = kv.Value.ToString() }) });
        }
        return Ok(new { _ctx.DeviceId, Role = "Device", _ctx.TenantId });
    }
}

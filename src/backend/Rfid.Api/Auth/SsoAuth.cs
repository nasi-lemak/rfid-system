using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Api.Auth;

/// <summary>Claim names shared by the login JWT and the OIDC claims transformation.</summary>
public static class PlatformClaims
{
    public const string Tenant = "tenant";
    public const string Restricted = "restricted";
    /// <summary>One claim per site: "{siteLocationId:N}={Role}".</summary>
    public const string Site = "site";

    public static IEnumerable<Claim> ForUser(User user, IEnumerable<UserSiteAccess> sites)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, user.Id.ToString());
        yield return new Claim(ClaimTypes.Name, user.DisplayName);
        yield return new Claim(ClaimTypes.Email, user.Email);
        yield return new Claim(ClaimTypes.Role, user.Role.ToString());
        yield return new Claim(Tenant, user.TenantId.ToString());
        if (user.PortalPartyId is { } portal) yield return new Claim("portal", portal.ToString());
        if (!user.RestrictToSites) yield break;
        yield return new Claim(Restricted, "true");
        foreach (var s in sites) yield return new Claim(Site, $"{s.SiteLocationId:N}={s.Role}");
    }

    public static (bool restricted, UserRole role, Dictionary<Guid, UserRole> sites) Parse(ClaimsPrincipal? p)
    {
        var role = Enum.TryParse<UserRole>(p?.FindFirst(ClaimTypes.Role)?.Value, out var r) ? r : UserRole.Viewer;
        var restricted = p?.HasClaim(Restricted, "true") == true;
        var sites = new Dictionary<Guid, UserRole>();
        foreach (var c in p?.FindAll(Site) ?? Enumerable.Empty<Claim>())
        {
            var parts = c.Value.Split('=', 2);
            if (parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) && Enum.TryParse<UserRole>(parts[1], out var sr)) sites[id] = sr;
        }
        return (restricted, role, sites);
    }

    /// <summary>True when the principal may act as an operator anywhere: globally, or on at least one of its sites.</summary>
    public static bool CanOperateSomewhere(ClaimsPrincipal p)
    {
        var (_, role, sites) = Parse(p);
        return RoleRank.AtLeast(role, UserRole.Operator) || sites.Values.Any(r => RoleRank.AtLeast(r, UserRole.Operator));
    }
}

/// <summary>Builds the request-scoped ISiteAccess from the principal's claims.</summary>
public static class SiteAccessFactory
{
    public static ISiteAccess Create(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<IAppDb>();
        var user = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return SiteAccess.Unrestricted(db, UserRole.Admin); // background jobs run unrestricted
        var (restricted, role, sites) = PlatformClaims.Parse(user);
        return restricted ? new SiteAccess(db, true, role, sites) : SiteAccess.Unrestricted(db, role);
    }
}

/// <summary>
/// Turns a validated external (OIDC) token into a platform principal: maps/provisions the user via SsoUserMapper
/// and replaces the external claims with the same claims a password login would issue. Cached per subject.
/// </summary>
public class OidcClaimsTransformation : IClaimsTransformation
{
    private readonly IServiceProvider _sp; private readonly IMemoryCache _cache; private readonly IOptions<SsoOptions> _opts; private readonly ILogger<OidcClaimsTransformation> _log;
    public OidcClaimsTransformation(IServiceProvider sp, IMemoryCache cache, IOptions<SsoOptions> opts, ILogger<OidcClaimsTransformation> log) { _sp = sp; _cache = cache; _opts = opts; _log = log; }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == PlatformClaims.Tenant)) return principal; // platform-issued token
        var issuer = principal.FindFirst("iss")?.Value ?? principal.Claims.FirstOrDefault()?.Issuer;
        var subject = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (issuer == null || subject == null) return principal;
        var key = $"sso:{issuer}|{subject}";
        if (!_cache.TryGetValue(key, out List<Claim>? claims) || claims == null)
        {
            var o = _opts.Value;
            var roles = principal.FindAll(o.RoleClaim).Select(c => c.Value).Concat(principal.FindAll("groups").Select(c => c.Value)).Distinct().ToList();
            var email = principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("preferred_username")?.Value;
            var name = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;
            using var scope = _sp.CreateScope();
            var mapper = scope.ServiceProvider.GetRequiredService<SsoUserMapper>();
            var user = await mapper.MapAsync(new SsoIdentity(issuer, subject, email, name, roles), o);
            if (user == null) { _log.LogWarning("SSO login for {Subject}@{Issuer} could not be mapped to a user", subject, issuer); return principal; }
            var sites = user.RestrictToSites ? await scope.ServiceProvider.GetRequiredService<IAppDb>().UserSiteAccess.IgnoreQueryFilters().Where(s => s.UserId == user.Id).ToListAsync() : new List<UserSiteAccess>();
            claims = PlatformClaims.ForUser(user, sites).ToList();
            claims.Add(new Claim("sso", issuer));
            _cache.Set(key, claims, TimeSpan.FromMinutes(5));
        }
        var identity = new ClaimsIdentity(claims, "oidc", ClaimTypes.Name, ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
}

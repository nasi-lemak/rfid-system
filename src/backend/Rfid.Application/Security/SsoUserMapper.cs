using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Security;

public class SsoOptions
{
    public bool Enabled { get; set; }
    public string? Authority { get; set; }
    public string? Audience { get; set; }
    public string? ClientId { get; set; }
    public string Scopes { get; set; } = "openid profile email";
    public string RoleClaim { get; set; } = "roles";
    /// <summary>External group/role value → platform role, e.g. {"rfid-admins":"Admin","rfid-ops":"Operator"}.</summary>
    public Dictionary<string, string> RoleMap { get; set; } = new();
    public UserRole DefaultRole { get; set; } = UserRole.Viewer;
    public string TenantCode { get; set; } = "demo";
    public bool AutoProvision { get; set; } = true;
}

public record SsoIdentity(string Issuer, string Subject, string? Email, string? Name, IReadOnlyList<string> ExternalRoles);

/// <summary>Maps an external (OIDC) identity to a platform user: looks up by subject or email, provisions on first login, applies the role mapping.</summary>
public class SsoUserMapper
{
    private readonly IAppDb _db;
    public SsoUserMapper(IAppDb db) => _db = db;

    public static UserRole MapRole(SsoOptions o, IEnumerable<string> externalRoles)
    {
        var best = (UserRole?)null;
        foreach (var r in externalRoles)
            if (o.RoleMap.TryGetValue(r, out var mapped) && Enum.TryParse<UserRole>(mapped, true, out var role) && (best == null || RoleRank.Of(role) > RoleRank.Of(best.Value))) best = role;
        return best ?? o.DefaultRole;
    }

    public async Task<User?> MapAsync(SsoIdentity id, SsoOptions o, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Code == o.TenantCode, ct);
        if (tenant == null) return null;
        var email = id.Email?.ToLowerInvariant();
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenant.Id && ((u.ExternalSubject == id.Subject && u.ExternalIssuer == id.Issuer) || (email != null && u.Email == email)), ct);
        var role = MapRole(o, id.ExternalRoles);
        if (user == null)
        {
            if (!o.AutoProvision || email == null) return null;
            user = new User { TenantId = tenant.Id, Email = email, DisplayName = id.Name ?? email, PasswordHash = "sso", Role = role, ExternalSubject = id.Subject, ExternalIssuer = id.Issuer };
            _db.Users.Add(user);
        }
        else
        {
            user.ExternalSubject ??= id.Subject; user.ExternalIssuer ??= id.Issuer;
            if (o.RoleMap.Count > 0 && id.ExternalRoles.Count > 0) user.Role = role; // IdP groups are the source of truth when mapped
            if (id.Name != null) user.DisplayName = id.Name;
        }
        if (!user.IsActive) return null;
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return user;
    }
}

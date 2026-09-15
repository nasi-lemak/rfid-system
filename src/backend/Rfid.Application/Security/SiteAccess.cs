using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Application.Security;

/// <summary>Per-site role-based access for the current principal. Unrestricted users (and devices) have full tenant access.</summary>
public interface ISiteAccess
{
    bool Restricted { get; }
    UserRole GlobalRole { get; }
    IReadOnlyDictionary<Guid, UserRole> SiteRoles { get; }
    /// <summary>Materialised paths of the allowed site subtrees, or null when unrestricted.</summary>
    Task<List<string>?> AllowedPathsAsync(CancellationToken ct = default);
    /// <summary>Effective role for a location (site subtree) – the higher of the global and site role.</summary>
    Task<UserRole> RoleForLocationAsync(Guid? locationId, CancellationToken ct = default);
    /// <summary>Throws when the principal may not act on the location with at least the given role.</summary>
    Task EnsureAsync(Guid? locationId, UserRole minimum, CancellationToken ct = default);
}

public static class RoleRank
{
    public static int Of(UserRole r) => r switch { UserRole.Admin => 3, UserRole.Operator => 2, UserRole.Device => 2, UserRole.Viewer => 1, _ => 0 };
    public static bool AtLeast(UserRole have, UserRole need) => Of(have) >= Of(need);
}

public class SiteAccess : ISiteAccess
{
    private readonly IAppDb _db;
    private List<string>? _paths; private bool _loaded;
    public bool Restricted { get; }
    public UserRole GlobalRole { get; }
    public IReadOnlyDictionary<Guid, UserRole> SiteRoles { get; }

    public SiteAccess(IAppDb db, bool restricted, UserRole globalRole, IReadOnlyDictionary<Guid, UserRole> siteRoles) { _db = db; Restricted = restricted; GlobalRole = globalRole; SiteRoles = siteRoles; }

    public static SiteAccess Unrestricted(IAppDb db, UserRole role) => new(db, false, role, new Dictionary<Guid, UserRole>());

    public async Task<List<string>?> AllowedPathsAsync(CancellationToken ct = default)
    {
        if (!Restricted) return null;
        if (_loaded) return _paths;
        var ids = SiteRoles.Keys.ToList();
        _paths = await _db.Locations.Where(l => ids.Contains(l.Id)).Select(l => l.Path).ToListAsync(ct); _loaded = true;
        return _paths;
    }

    public async Task<UserRole> RoleForLocationAsync(Guid? locationId, CancellationToken ct = default)
    {
        if (!Restricted) return GlobalRole;
        if (locationId == null) return GlobalRole;
        var path = await _db.Locations.Where(l => l.Id == locationId).Select(l => l.Path).FirstOrDefaultAsync(ct);
        if (path == null) return GlobalRole;
        var best = GlobalRole;
        foreach (var (site, role) in SiteRoles)
            if (path.Contains("/" + site.ToString("N") + "/") && RoleRank.Of(role) > RoleRank.Of(best)) best = role;
        var inScope = SiteRoles.Keys.Any(site => path.Contains("/" + site.ToString("N") + "/"));
        return inScope ? best : UserRole.Viewer is var v && RoleRank.Of(GlobalRole) >= RoleRank.Of(UserRole.Admin) ? GlobalRole : (UserRole)(-1);
    }

    public async Task EnsureAsync(Guid? locationId, UserRole minimum, CancellationToken ct = default)
    {
        if (!Restricted) { if (!RoleRank.AtLeast(GlobalRole, minimum)) throw new DomainException($"Requires {minimum} role"); return; }
        var role = await RoleForLocationAsync(locationId, ct);
        if ((int)role < 0) throw new DomainException("Location is outside your sites");
        if (!RoleRank.AtLeast(role, minimum)) throw new DomainException($"Requires {minimum} role on this site");
    }

    /// <summary>Applies the site filter to a query of items (by current location path).</summary>
    public static IQueryable<Item> Filter(IQueryable<Item> q, List<string>? paths) => paths == null ? q : q.Where(PathPredicate<Item>(paths, i => i.CurrentLocation!.Path, requireNotNull: i => i.CurrentLocation));
    public static IQueryable<Location> Filter(IQueryable<Location> q, List<string>? paths) => paths == null ? q : q.Where(PathPredicate<Location>(paths, l => l.Path));

    /// <summary>Builds "x.Path starts with p1 OR p2 …" as a single expression so every EF provider can translate it.</summary>
    public static Expression<Func<T, bool>> PathPredicate<T>(IReadOnlyList<string> paths, Expression<Func<T, string>> pathOf, Expression<Func<T, object?>>? requireNotNull = null)
    {
        var param = pathOf.Parameters[0];
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;
        Expression body = Expression.Constant(false);
        foreach (var p in paths.Distinct())
            body = Expression.OrElse(body, Expression.Call(pathOf.Body, startsWith, Expression.Constant(p)));
        if (requireNotNull != null)
        {
            var nn = Expression.NotEqual(new Rebinder(requireNotNull.Parameters[0], param).Visit(requireNotNull.Body), Expression.Constant(null));
            body = Expression.AndAlso(nn, body);
        }
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private sealed class Rebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from, _to;
        public Rebinder(ParameterExpression from, ParameterExpression to) { _from = from; _to = to; }
        protected override Expression VisitParameter(ParameterExpression node) => node == _from ? _to : base.VisitParameter(node);
    }
}

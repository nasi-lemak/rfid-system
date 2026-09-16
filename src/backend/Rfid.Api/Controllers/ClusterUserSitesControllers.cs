using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Rfid.Api.Auth;
using Rfid.Application.Cluster;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

/// <summary>Cluster status: this node, the leases held across the deployment, and which scale-out features are on.</summary>
[ApiController, Route("api/cluster"), Authorize(Policy = "Admin")]
public class ClusterController : ControllerBase
{
    private readonly LeaseService _leases; private readonly IConfiguration _cfg; private readonly IOptions<SsoOptions> _sso;
    public ClusterController(LeaseService leases, IConfiguration cfg, IOptions<SsoOptions> sso) { _leases = leases; _cfg = cfg; _sso = sso; }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var leases = await _leases.ListAsync(ct);
        var nodes = leases.Where(l => l.ExpiresAt > now).GroupBy(l => l.Owner).Select(g => new { Id = g.Key, Leases = g.Count(), Since = g.Min(l => l.AcquiredAt), IsThisNode = g.Key == ClusterNode.Id }).OrderBy(n => n.Id).ToList();
        if (nodes.All(n => n.Id != ClusterNode.Id)) nodes.Add(new { Id = ClusterNode.Id, Leases = 0, Since = ClusterNode.StartedAt, IsThisNode = true });
        return Ok(new
        {
            Node = new { ClusterNode.Id, ClusterNode.StartedAt, Machine = Environment.MachineName, Pid = Environment.ProcessId, UptimeSeconds = (int)(now - ClusterNode.StartedAt).TotalSeconds },
            Nodes = nodes,
            Leases = leases.Select(l => new { l.Name, l.Owner, l.AcquiredAt, l.ExpiresAt, Active = l.ExpiresAt > now, HeldByThisNode = l.Owner == ClusterNode.Id, l.Version }),
            Features = new
            {
                RedisBackplane = !string.IsNullOrWhiteSpace(_cfg["Redis:ConnectionString"]),
                Mqtt = _cfg.GetValue("Mqtt:Enabled", false),
                Llrp = _cfg.GetValue("Llrp:Enabled", true),
                Sso = _sso.Value.Enabled && !string.IsNullOrWhiteSpace(_sso.Value.Authority),
                PositionRetentionDays = _cfg.GetValue("Positions:RetentionDays", 30),
            },
        });
    }

    /// <summary>Drops a lease so another node can pick the job up (e.g. before draining this node).</summary>
    [HttpPost("leases/{name}/release")]
    public async Task<IActionResult> Release(string name, CancellationToken ct)
    {
        var lease = (await _leases.ListAsync(ct)).FirstOrDefault(l => l.Name == name);
        if (lease == null) return NotFound();
        await _leases.ReleaseAsync(name, lease.Owner, ct: ct);
        return Ok(new { released = name, owner = lease.Owner });
    }
}

/// <summary>Per-user site scoping (which sites a restricted user may see and act on, and with which role).</summary>
[ApiController, Route("api/users/{userId:guid}/sites"), Authorize(Policy = "Admin")]
public class UserSitesController : ControllerBase
{
    private readonly AppDbContext _db; private readonly ICurrentContext _ctx;
    public UserSitesController(AppDbContext db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    public record SiteRole(Guid SiteLocationId, UserRole Role);
    public record SitesWrite(bool RestrictToSites, List<SiteRole> Sites);

    [HttpGet]
    public async Task<IActionResult> Get(Guid userId)
    {
        var u = await _db.Users.FindAsync(userId); if (u == null) return NotFound();
        var sites = await _db.UserSiteAccess.Where(s => s.UserId == userId).Join(_db.Locations, s => s.SiteLocationId, l => l.Id, (s, l) => new { s.SiteLocationId, SiteName = l.Name, SitePath = l.Path, s.Role }).OrderBy(x => x.SiteName).ToListAsync();
        return Ok(new { u.Id, u.RestrictToSites, Sites = sites });
    }

    [HttpPut]
    public async Task<IActionResult> Put(Guid userId, SitesWrite w)
    {
        var u = await _db.Users.FindAsync(userId); if (u == null) return NotFound();
        if (u.Id == _ctx.UserId && w.RestrictToSites) return BadRequest(new { error = "You cannot restrict your own account" });
        var wanted = w.Sites.GroupBy(s => s.SiteLocationId).ToDictionary(g => g.Key, g => g.Last().Role);
        var validIds = await _db.Locations.Where(l => wanted.Keys.Contains(l.Id)).Select(l => l.Id).ToListAsync();
        if (validIds.Count != wanted.Count) return BadRequest(new { error = "Unknown site location" });
        var existing = await _db.UserSiteAccess.Where(s => s.UserId == userId).ToListAsync();
        foreach (var e in existing) { if (wanted.TryGetValue(e.SiteLocationId, out var role)) e.Role = role; else _db.UserSiteAccess.Remove(e); }
        foreach (var (site, role) in wanted) if (existing.All(e => e.SiteLocationId != site)) _db.UserSiteAccess.Add(new UserSiteAccess { TenantId = _ctx.TenantId, UserId = userId, SiteLocationId = site, Role = role });
        u.RestrictToSites = w.RestrictToSites;
        await _db.SaveChangesAsync();
        return await Get(userId);
    }
}

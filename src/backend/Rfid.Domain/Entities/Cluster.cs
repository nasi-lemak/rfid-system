namespace Rfid.Domain.Entities;

/// <summary>Cluster-wide lease for a background job or a reader connection: whichever node holds the lease runs it.</summary>
public class WorkerLease : EntityBase
{
    public string Name { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime AcquiredAt { get; set; }
    /// <summary>Optimistic-concurrency token so two nodes cannot both win a lease.</summary>
    public int Version { get; set; }
}

/// <summary>Position history sample (RTLS). Written when an item moves or periodically while stationary; powers heat maps and path replay.</summary>
public class PositionFix : TenantEntity
{
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double? AccuracyM { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Site-scoped role for a user (per-site RBAC). A user restricted to sites only sees/acts within these subtrees.</summary>
public class UserSiteAccess : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid SiteLocationId { get; set; }
    public UserRole Role { get; set; } = UserRole.Viewer;
}

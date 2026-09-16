namespace Rfid.Domain.Entities;

public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public abstract class TenantEntity : EntityBase
{
    public Guid TenantId { get; set; }
}

public class Tenant : EntityBase
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
}

public class User : TenantEntity
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    public bool IsActive { get; set; } = true;
    /// <summary>When true the user only sees and acts within the sites listed in UserSiteAccess (site role may exceed the global role).</summary>
    public bool RestrictToSites { get; set; }
    /// <summary>External identity (OIDC subject) when the user signs in through SSO.</summary>
    public string? ExternalSubject { get; set; }
    public string? ExternalIssuer { get; set; }
    public DateTime? LastLoginAt { get; set; }
    /// <summary>Portal users: the supplier/customer party this login is scoped to (read-only view of their items, activity and invoices).</summary>
    public Guid? PortalPartyId { get; set; }
}

public class Party : TenantEntity
{
    public PartyKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? ExternalRef { get; set; }
    public string? Email { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
}

public class Location : TenantEntity
{
    public Guid? ParentId { get; set; }
    public Location? Parent { get; set; }
    public LocationKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    /// <summary>Materialised path "/{rootId}/{...}/{thisId}/" for subtree queries.</summary>
    public string Path { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsMobile { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
}

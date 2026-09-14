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

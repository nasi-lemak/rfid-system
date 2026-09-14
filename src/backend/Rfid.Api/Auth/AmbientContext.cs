using System.Security.Claims;
using Rfid.Application.Contracts;

namespace Rfid.Api.Auth;

/// <summary>Tenant/user scope for code running outside an HTTP request (background services set it per unit of work).</summary>
public static class AmbientContext
{
    private static readonly AsyncLocal<(Guid tenant, Guid? user, Guid? device)?> Current = new();
    public static IDisposable Use(Guid tenantId, Guid? userId = null, Guid? deviceId = null)
    {
        var prev = Current.Value; Current.Value = (tenantId, userId, deviceId);
        return new Restore(() => Current.Value = prev);
    }
    public static (Guid tenant, Guid? user, Guid? device)? Value => Current.Value;
    private sealed class Restore : IDisposable { private readonly Action _a; public Restore(Action a) => _a = a; public void Dispose() => _a(); }
}

/// <summary>ICurrentContext that reads JWT claims inside a request and the ambient scope otherwise.</summary>
public class RequestOrAmbientContext : ICurrentContext
{
    private readonly ClaimsPrincipal? _user;
    public RequestOrAmbientContext(IHttpContextAccessor accessor) => _user = accessor.HttpContext?.User;
    public Guid TenantId => Guid.TryParse(_user?.FindFirst("tenant")?.Value, out var g) ? g : AmbientContext.Value?.tenant ?? Guid.Empty;
    public Guid? UserId => Guid.TryParse(_user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var g) ? g : AmbientContext.Value?.user;
    public Guid? DeviceId => Guid.TryParse(_user?.FindFirst("device")?.Value, out var g) ? g : AmbientContext.Value?.device;
}

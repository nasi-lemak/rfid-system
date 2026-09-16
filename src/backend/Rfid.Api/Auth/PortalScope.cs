using System.Security.Claims;

namespace Rfid.Api.Auth;

/// <summary>Portal (supplier/customer) logins carry a "portal" claim and may only call /api/portal/* and /api/auth/*.</summary>
public class PortalScopeMiddleware
{
    private readonly RequestDelegate _next;
    public PortalScopeMiddleware(RequestDelegate next) => _next = next;
    public async Task Invoke(HttpContext ctx)
    {
        var portal = ctx.User.FindFirst("portal")?.Value;
        if (portal != null && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/portal") && !ctx.Request.Path.StartsWithSegments("/api/auth") && !ctx.Request.Path.StartsWithSegments("/api/lookups"))
        {
            ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "Portal accounts can only use the portal API" }); return;
        }
        await _next(ctx);
    }
}

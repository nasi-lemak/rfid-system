using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rfid.Api.Auth;
using Rfid.Application.Contracts;
using Rfid.Application.Services;
using Rfid.Infrastructure.Persistence;

namespace Rfid.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtService _jwt;
    private readonly ICurrentContext _ctx;
    public AuthController(AppDbContext db, JwtService jwt, ICurrentContext ctx) { _db = db; _jwt = jwt; _ctx = ctx; }

    public record LoginRequest(string Email, string Password);
    public record DeviceLoginRequest(string Token);

    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant() && u.IsActive);
        if (user == null || !PasswordHasher.Verify(req.Password, user.PasswordHash)) return Unauthorized(new { error = "Invalid credentials" });
        var tenant = await _db.Tenants.FindAsync(user.TenantId);
        return Ok(new { token = _jwt.IssueForUser(user), user = new { user.Id, user.Email, user.DisplayName, Role = user.Role.ToString(), user.TenantId, TenantName = tenant?.Name } });
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
            return Ok(new { user?.Id, user?.Email, user?.DisplayName, Role = user?.Role.ToString(), _ctx.TenantId });
        }
        return Ok(new { _ctx.DeviceId, Role = "Device", _ctx.TenantId });
    }
}

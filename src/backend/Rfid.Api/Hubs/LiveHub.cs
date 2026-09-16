using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Api.Hubs;

/// <summary>Clients join their tenant group and receive live reads, events and alerts.</summary>
[Authorize]
public class LiveHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenant = Context.User?.FindFirst("tenant")?.Value;
        if (tenant != null) await Groups.AddToGroupAsync(Context.ConnectionId, tenant);
        await base.OnConnectedAsync();
    }
}

/// <summary>SignalR transport used by the outbox dispatcher (events/alerts, after commit) and for the best-effort raw read stream.</summary>
public class SignalRLiveTransport : Rfid.Application.Platform.ILiveTransport
{
    private readonly IHubContext<LiveHub> _hub;
    public SignalRLiveTransport(IHubContext<LiveHub> hub) => _hub = hub;
    public Task SendAsync(Guid tenantId, string method, object payload, CancellationToken ct = default) => _hub.Clients.Group(tenantId.ToString()).SendAsync(method, payload, ct);
}

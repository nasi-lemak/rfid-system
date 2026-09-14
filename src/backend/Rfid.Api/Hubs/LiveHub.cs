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

public class SignalRLivePublisher : ILivePublisher
{
    private readonly IHubContext<LiveHub> _hub;
    private readonly ICurrentContext _ctx;
    public SignalRLivePublisher(IHubContext<LiveHub> hub, ICurrentContext ctx) { _hub = hub; _ctx = ctx; }
    private IClientProxy Group => _hub.Clients.Group(_ctx.TenantId.ToString());

    public Task PublishReadsAsync(IEnumerable<LiveRead> reads, CancellationToken ct = default) => Group.SendAsync("reads", reads.ToList(), ct);
    public Task PublishAlertAsync(Alert alert, CancellationToken ct = default) => Group.SendAsync("alert", new { alert.Id, alert.ItemId, alert.LocationId, Severity = alert.Severity.ToString(), alert.Message, alert.RaisedAt }, ct);
    public Task PublishEventAsync(ItemEvent ev, CancellationToken ct = default) => Group.SendAsync("event", new { ev.Id, ev.ItemId, Type = ev.Type.ToString(), ev.FromLocationId, ev.ToLocationId, ev.ToState, ev.OccurredAt }, ct);
}

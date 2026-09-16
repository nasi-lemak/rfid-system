using System.Text.Json;
using Rfid.Application.Contracts;
using Rfid.Application.Platform;
using Rfid.Domain.Entities;

namespace Rfid.Application.Operations;

/// <summary>An operation a rule asked for. Carried through the outbox so it runs only after the triggering unit of work committed.</summary>
public record RunOperationCommand(Guid TenantId, Guid ItemId, string Operation, Guid? ToLocationId, Guid? PartyId, string? TargetState, string? Reference, Guid RuleId, string RuleName);

/// <summary>Executes an operation for a tenant outside the current request scope (the API opens a tenant-scoped service scope; tests call the processor directly).</summary>
public interface IOperationRunner
{
    Task<OperationResult> RunAsync(Guid tenantId, OperationRequest request, CancellationToken ct = default);
}

public sealed class RunOperationOutboxHandler : IOutboxHandler
{
    private readonly IOperationRunner _runner;
    public RunOperationOutboxHandler(IOperationRunner runner) => _runner = runner;
    public string Kind => OutboxKinds.RunOperation;

    public async Task DeliverAsync(OutboxMessage m, CancellationToken ct)
    {
        var cmd = JsonSerializer.Deserialize<RunOperationCommand>(m.Payload, Outbox.Json) ?? throw new InvalidOperationException("bad run-operation payload");
        var req = new OperationRequest
        {
            Operation = cmd.Operation, ToLocationId = cmd.ToLocationId, PartyId = cmd.PartyId, TargetState = cmd.TargetState,
            Reference = cmd.Reference, Notes = $"rule:{cmd.RuleName}", ClientId = $"rule:{cmd.RuleId:N}:{m.Id:N}",   // idempotent per message
            Lines = { new OperationLineRequest { ItemId = cmd.ItemId } },
        };
        var r = await _runner.RunAsync(cmd.TenantId, req, ct);
        // A rejected line (lifecycle, disposed item, …) is a business outcome recorded on the operation, not a delivery failure to retry.
        if (r.Rejected > 0) m.Error = "rejected: " + string.Join("; ", r.Lines.Where(l => l.Message != null).Select(l => l.Message));
    }
}

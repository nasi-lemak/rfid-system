namespace Rfid.Domain.Entities;

/// <summary>
/// Transactional outbox: external side effects (live pushes, webhooks, notifications) are written here in the same
/// database transaction as the business state that caused them and delivered afterwards by the outbox dispatcher.
/// Nothing leaves the process before the state it describes is committed.
/// </summary>
public class OutboxMessage : TenantEntity
{
    /// <summary>live.event · live.alert · webhook · notification</summary>
    public string Kind { get; set; } = "";
    /// <summary>Kind-specific target: SignalR group, webhook URL, notification channel id.</summary>
    public string? Destination { get; set; }
    public string Payload { get; set; } = "{}";
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? Error { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}

/// <summary>Idempotency record for client-generated request keys (handheld sync, edge read batches): the stored response is replayed on retry.</summary>
public static class IdempotencyScopes
{
    public const string ReadBatch = "read-batch";
    public const string Gps = "gps";
    public const string Epcis = "epcis";
}

public class IdempotencyKey : TenantEntity
{
    /// <summary>operation · ingest · gps · epcis</summary>
    public string Scope { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Response { get; set; }
}

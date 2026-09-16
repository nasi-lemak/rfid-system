using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Parquet.Serialization;
using Rfid.Application.Contracts;
using Rfid.Domain.Entities;

namespace Rfid.Application.Services;

/// <summary>Flat, warehouse-friendly rows (string ids, UTC timestamps) for each exportable dataset.</summary>
public static class WarehouseRows
{
    public class ItemRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string Identifier { get; set; } = ""; public string Name { get; set; } = ""; public string? ItemType { get; set; } public string? ItemTypeCode { get; set; } public string? State { get; set; } public string Status { get; set; } = ""; public string? Location { get; set; } public string? LocationId { get; set; } public string? Custodian { get; set; } public double Quantity { get; set; } public int CycleCount { get; set; } public DateTime? LastSeenAt { get; set; } public double? Cost { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } public DateTime CreatedAt { get; set; } public string Attributes { get; set; } = "{}"; }
    public class EventRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string ItemId { get; set; } = ""; public string Type { get; set; } = ""; public string? FromLocationId { get; set; } public string? ToLocationId { get; set; } public string? FromPartyId { get; set; } public string? ToPartyId { get; set; } public string? FromState { get; set; } public string? ToState { get; set; } public string? OperationId { get; set; } public string? DeviceId { get; set; } public string? UserId { get; set; } public DateTime OccurredAt { get; set; } public string Data { get; set; } = "{}"; }
    public class ReadRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string Epc { get; set; } = ""; public string? ItemId { get; set; } public string? DeviceId { get; set; } public int? AntennaPort { get; set; } public double? Rssi { get; set; } public DateTime ReadAt { get; set; } public string? LocationId { get; set; } public string Source { get; set; } = ""; public string? SessionId { get; set; } }
    public class AlertRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string? RuleId { get; set; } public string? ItemId { get; set; } public string? LocationId { get; set; } public string? DeviceId { get; set; } public string Severity { get; set; } = ""; public string Message { get; set; } = ""; public string Status { get; set; } = ""; public string? Source { get; set; } public DateTime RaisedAt { get; set; } public DateTime? AcknowledgedAt { get; set; } public DateTime? ClosedAt { get; set; } public int EscalationLevel { get; set; } }
    public class OperationRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string Type { get; set; } = ""; public string Status { get; set; } = ""; public string? FromLocationId { get; set; } public string? ToLocationId { get; set; } public string? PartyId { get; set; } public string? DeviceId { get; set; } public string? UserId { get; set; } public DateTime StartedAt { get; set; } public DateTime? CompletedAt { get; set; } public int Lines { get; set; } public string? Reference { get; set; } }
    public class PositionRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string ItemId { get; set; } = ""; public string LocationId { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public double? AccuracyM { get; set; } public DateTime At { get; set; } }
    public class GpsRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string ItemId { get; set; } = ""; public double Lat { get; set; } public double Lng { get; set; } public double? SpeedKph { get; set; } public double? HeadingDeg { get; set; } public DateTime At { get; set; } public string? DeviceId { get; set; } }
    public class PresenceRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string ItemId { get; set; } = ""; public string LocationId { get; set; } = ""; public string? DeviceId { get; set; } public DateTime EnteredAt { get; set; } public DateTime? ExitedAt { get; set; } public double? DwellMinutes { get; set; } public int ReadCount { get; set; } }
    public class StocktakeRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string Name { get; set; } = ""; public string LocationId { get; set; } = ""; public string Status { get; set; } = ""; public int Expected { get; set; } public int Found { get; set; } public int Missing { get; set; } public int Unexpected { get; set; } public DateTime StartedAt { get; set; } public DateTime? CompletedAt { get; set; } }
    public class HeartbeatRow { public string Id { get; set; } = ""; public string TenantId { get; set; } = ""; public string DeviceId { get; set; } = ""; public DateTime At { get; set; } public string? FirmwareVersion { get; set; } public double? CpuPercent { get; set; } public double? TemperatureC { get; set; } public int? ReadsPerMinute { get; set; } }
}

/// <summary>
/// Exports datasets to a data-warehouse landing zone as Parquet (default) or CSV, one file per run under
/// {root}/{dataset}/dt=YYYY-MM-DD/. Incremental runs continue from the previous run's upper bound.
/// </summary>
public class WarehouseExportService
{
    private readonly IAppDb _db; private readonly ICurrentContext _ctx;
    public string RootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "warehouse");
    public string DefaultFormat { get; set; } = "parquet";
    public TimeSpan InitialWindow { get; set; } = TimeSpan.FromDays(30);
    public WarehouseExportService(IAppDb db, ICurrentContext ctx) { _db = db; _ctx = ctx; }

    public static readonly string[] Datasets = { "items", "events", "reads", "alerts", "operations", "position_fixes", "gps_fixes", "presence_sessions", "stocktakes", "device_heartbeats" };
    /// <summary>Snapshot datasets are exported in full each run; the others are time-windowed.</summary>
    public static bool IsSnapshot(string dataset) => dataset is "items";

    private static string S(Guid g) => g.ToString();
    private static string? S(Guid? g) => g?.ToString();
    private static string J(object o) => System.Text.Json.JsonSerializer.Serialize(o);

    public async Task<(Type type, System.Collections.IList rows)> QueryAsync(string dataset, DateTime from, DateTime to, CancellationToken ct = default)
    {
        switch (dataset)
        {
            case "items":
                return (typeof(WarehouseRows.ItemRow), (await _db.Items.Include(i => i.ItemType).Include(i => i.CurrentLocation).Include(i => i.CustodianParty).AsNoTracking().ToListAsync(ct))
                    .Select(i => new WarehouseRows.ItemRow { Id = S(i.Id), TenantId = S(i.TenantId), Identifier = i.Identifier, Name = i.Name, ItemType = i.ItemType?.Name, ItemTypeCode = i.ItemType?.Code, State = i.State, Status = i.Status.ToString(), Location = i.CurrentLocation?.Name, LocationId = S(i.CurrentLocationId), Custodian = i.CustodianParty?.Name, Quantity = (double)i.Quantity, CycleCount = i.CycleCount, LastSeenAt = i.LastSeenAt, Cost = (double?)i.Cost, Latitude = i.Latitude, Longitude = i.Longitude, CreatedAt = i.CreatedAt, Attributes = J(i.Attributes) }).ToList());
            case "events":
                return (typeof(WarehouseRows.EventRow), (await _db.ItemEvents.AsNoTracking().Where(e => e.OccurredAt >= from && e.OccurredAt < to).OrderBy(e => e.OccurredAt).ToListAsync(ct))
                    .Select(e => new WarehouseRows.EventRow { Id = S(e.Id), TenantId = S(e.TenantId), ItemId = S(e.ItemId), Type = e.Type.ToString(), FromLocationId = S(e.FromLocationId), ToLocationId = S(e.ToLocationId), FromPartyId = S(e.FromPartyId), ToPartyId = S(e.ToPartyId), FromState = e.FromState, ToState = e.ToState, OperationId = S(e.OperationId), DeviceId = S(e.DeviceId), UserId = S(e.UserId), OccurredAt = e.OccurredAt, Data = J(e.Data) }).ToList());
            case "reads":
                return (typeof(WarehouseRows.ReadRow), (await _db.TagReads.AsNoTracking().Where(r => r.ReadAt >= from && r.ReadAt < to).OrderBy(r => r.ReadAt).ToListAsync(ct))
                    .Select(r => new WarehouseRows.ReadRow { Id = S(r.Id), TenantId = S(r.TenantId), Epc = r.Epc, ItemId = S(r.ItemId), DeviceId = S(r.DeviceId), AntennaPort = r.AntennaPort, Rssi = r.Rssi, ReadAt = r.ReadAt, LocationId = S(r.LocationId), Source = r.Source.ToString(), SessionId = r.SessionId }).ToList());
            case "alerts":
                return (typeof(WarehouseRows.AlertRow), (await _db.Alerts.AsNoTracking().Where(a => a.RaisedAt >= from && a.RaisedAt < to).OrderBy(a => a.RaisedAt).ToListAsync(ct))
                    .Select(a => new WarehouseRows.AlertRow { Id = S(a.Id), TenantId = S(a.TenantId), RuleId = S(a.RuleId), ItemId = S(a.ItemId), LocationId = S(a.LocationId), DeviceId = S(a.DeviceId), Severity = a.Severity.ToString(), Message = a.Message, Status = a.Status.ToString(), Source = a.Source, RaisedAt = a.RaisedAt, AcknowledgedAt = a.AcknowledgedAt, ClosedAt = a.ClosedAt, EscalationLevel = a.EscalationLevel }).ToList());
            case "operations":
                return (typeof(WarehouseRows.OperationRow), (await _db.Operations.AsNoTracking().Include(o => o.Lines).Where(o => o.StartedAt >= from && o.StartedAt < to).OrderBy(o => o.StartedAt).ToListAsync(ct))
                    .Select(o => new WarehouseRows.OperationRow { Id = S(o.Id), TenantId = S(o.TenantId), Type = o.Type.ToString(), Status = o.Status.ToString(), FromLocationId = S(o.FromLocationId), ToLocationId = S(o.ToLocationId), PartyId = S(o.PartyId), DeviceId = S(o.DeviceId), UserId = S(o.UserId), StartedAt = o.StartedAt, CompletedAt = o.CompletedAt, Lines = o.Lines.Count, Reference = o.Reference }).ToList());
            case "position_fixes":
                return (typeof(WarehouseRows.PositionRow), (await _db.PositionFixes.AsNoTracking().Where(f => f.At >= from && f.At < to).OrderBy(f => f.At).ToListAsync(ct))
                    .Select(f => new WarehouseRows.PositionRow { Id = S(f.Id), TenantId = S(f.TenantId), ItemId = S(f.ItemId), LocationId = S(f.LocationId), X = f.X, Y = f.Y, AccuracyM = f.AccuracyM, At = f.At }).ToList());
            case "gps_fixes":
                return (typeof(WarehouseRows.GpsRow), (await _db.GpsFixes.AsNoTracking().Where(f => f.At >= from && f.At < to).OrderBy(f => f.At).ToListAsync(ct))
                    .Select(f => new WarehouseRows.GpsRow { Id = S(f.Id), TenantId = S(f.TenantId), ItemId = S(f.ItemId), Lat = f.Lat, Lng = f.Lng, SpeedKph = f.SpeedKph, HeadingDeg = f.HeadingDeg, At = f.At, DeviceId = S(f.DeviceId) }).ToList());
            case "presence_sessions":
                return (typeof(WarehouseRows.PresenceRow), (await _db.PresenceSessions.AsNoTracking().Where(s => (s.ExitedAt ?? s.LastSeenAt) >= from && (s.ExitedAt ?? s.LastSeenAt) < to).OrderBy(s => s.EnteredAt).ToListAsync(ct))
                    .Select(s => new WarehouseRows.PresenceRow { Id = S(s.Id), TenantId = S(s.TenantId), ItemId = S(s.ItemId), LocationId = S(s.LocationId), DeviceId = S(s.DeviceId), EnteredAt = s.EnteredAt, ExitedAt = s.ExitedAt, DwellMinutes = s.ExitedAt.HasValue ? Math.Round((s.ExitedAt.Value - s.EnteredAt).TotalMinutes, 1) : null, ReadCount = s.ReadCount }).ToList());
            case "stocktakes":
                return (typeof(WarehouseRows.StocktakeRow), (await _db.Stocktakes.AsNoTracking().Where(s => (s.CompletedAt ?? s.StartedAt) >= from && (s.CompletedAt ?? s.StartedAt) < to).OrderBy(s => s.StartedAt).ToListAsync(ct))
                    .Select(s => new WarehouseRows.StocktakeRow { Id = S(s.Id), TenantId = S(s.TenantId), Name = s.Name, LocationId = S(s.LocationId), Status = s.Status.ToString(), Expected = s.ExpectedCount, Found = s.FoundCount, Missing = s.MissingCount, Unexpected = s.UnexpectedCount, StartedAt = s.StartedAt, CompletedAt = s.CompletedAt }).ToList());
            case "device_heartbeats":
                return (typeof(WarehouseRows.HeartbeatRow), (await _db.DeviceHeartbeats.AsNoTracking().Where(h => h.At >= from && h.At < to).OrderBy(h => h.At).ToListAsync(ct))
                    .Select(h => new WarehouseRows.HeartbeatRow { Id = S(h.Id), TenantId = S(h.TenantId), DeviceId = S(h.DeviceId), At = h.At, FirmwareVersion = h.FirmwareVersion, CpuPercent = h.CpuPercent, TemperatureC = h.TemperatureC, ReadsPerMinute = h.ReadsPerMinute }).ToList());
            default: throw new DomainException($"Unknown dataset '{dataset}'");
        }
    }

    /// <summary>Serialises rows to Parquet or CSV in memory.</summary>
    public static async Task<byte[]> SerializeAsync(Type type, System.Collections.IList rows, string format, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        if (format == "csv")
        {
            var props = type.GetProperties();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", props.Select(p => p.Name)));
            foreach (var r in rows) sb.AppendLine(string.Join(",", props.Select(p => Csv(p.GetValue(r)))));
            var bytes = Encoding.UTF8.GetBytes(sb.ToString()); await ms.WriteAsync(bytes, ct);
        }
        else
        {
            var method = typeof(WarehouseExportService).GetMethod(nameof(WriteParquetAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(type);
            await (Task)method.Invoke(null, new object[] { rows, ms, ct })!;
        }
        return ms.ToArray();
    }
    private static async Task WriteParquetAsync<T>(System.Collections.IList rows, Stream stream, CancellationToken ct) where T : new()
        => await ParquetSerializer.SerializeAsync(rows.Cast<T>().ToList(), stream, cancellationToken: ct);

    private static string Csv(object? v) => v switch
    {
        null => "", DateTime d => d.ToString("O", CultureInfo.InvariantCulture), double x => x.ToString(CultureInfo.InvariantCulture), float x => x.ToString(CultureInfo.InvariantCulture), decimal x => x.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false", string s => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s, _ => v.ToString() ?? "",
    };

    /// <summary>Exports one dataset window to the landing zone and records the run.</summary>
    public async Task<WarehouseExportRun> ExportAsync(string dataset, DateTime from, DateTime to, string? format = null, bool manual = false, CancellationToken ct = default)
    {
        var fmt = (format ?? DefaultFormat).ToLowerInvariant() is "csv" ? "csv" : "parquet";
        var run = new WarehouseExportRun { TenantId = _ctx.TenantId, Dataset = dataset, From = from, To = to, Format = fmt, Manual = manual, StartedAt = DateTime.UtcNow };
        _db.WarehouseExportRuns.Add(run);
        try
        {
            var (type, rows) = await QueryAsync(dataset, from, to, ct);
            var bytes = await SerializeAsync(type, rows, fmt, ct);
            var dir = Path.Combine(RootPath, _ctx.TenantId.ToString("N")[..8], dataset, $"dt={to:yyyy-MM-dd}");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{dataset}-{from:yyyyMMddHHmm}-{to:yyyyMMddHHmm}-{run.Id.ToString("N")[..8]}.{fmt}");
            await File.WriteAllBytesAsync(file, bytes, ct);
            run.Rows = rows.Count; run.Bytes = bytes.LongLength; run.Path = file; run.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { run.Error = ex.Message; run.CompletedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>Incremental export of every dataset from the last successful run up to now (minute-aligned). Returns runs performed.</summary>
    public async Task<List<WarehouseExportRun>> RunIncrementalAsync(DateTime now, IEnumerable<string>? datasets = null, CancellationToken ct = default)
    {
        var to = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var runs = new List<WarehouseExportRun>();
        foreach (var ds in datasets ?? Datasets)
        {
            var last = await _db.WarehouseExportRuns.Where(r => r.Dataset == ds && r.Error == null && !r.Manual).OrderByDescending(r => r.To).Select(r => (DateTime?)r.To).FirstOrDefaultAsync(ct);
            var from = IsSnapshot(ds) ? to.AddDays(-1) : last ?? to - InitialWindow;
            if (!IsSnapshot(ds) && to - from < TimeSpan.FromMinutes(1)) continue;
            if (IsSnapshot(ds) && last != null && to - last.Value < TimeSpan.FromHours(23)) continue; // daily snapshot
            runs.Add(await ExportAsync(ds, from, to, null, false, ct));
        }
        return runs;
    }
}

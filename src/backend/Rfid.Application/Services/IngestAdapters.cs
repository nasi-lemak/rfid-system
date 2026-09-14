using System.Globalization;
using System.Text.Json;
using Rfid.Application.Contracts;

namespace Rfid.Application.Services;

/// <summary>
/// Normalises vendor reader payloads into <see cref="ReadBatchRequest"/>. Supported:
///  - Impinj IoT Interface (R700 "tagInventoryEvent" stream / webhook)
///  - Zebra IoT Connector (FX7500/FX9600 "data.idHex" events)
///  - Generic: {"reads":[{"epc":..}]}, an array of reads, or a single read object.
/// Unknown shapes fall back to a best-effort scan for epc-like fields.
/// </summary>
public static class IngestAdapters
{
    public static ReadBatchRequest Parse(string json, string? vendorHint = null)
    {
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement, vendorHint);
    }

    public static ReadBatchRequest Parse(JsonElement root, string? vendorHint = null)
    {
        var batch = new ReadBatchRequest();
        var v = vendorHint?.ToLowerInvariant();
        if (v == "impinj" || (v == null && LooksImpinj(root))) ParseImpinj(root, batch);
        else if (v == "zebra" || (v == null && LooksZebra(root))) ParseZebra(root, batch);
        else ParseGeneric(root, batch);
        return batch;
    }

    private static bool LooksImpinj(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.TryGetProperty("tagInventoryEvent", out _) : e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].ValueKind == JsonValueKind.Object && e[0].TryGetProperty("tagInventoryEvent", out _);
    private static bool LooksZebra(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("idHex", out _) : e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && LooksZebra(e[0]);

    private static void ParseImpinj(JsonElement e, ReadBatchRequest b)
    {
        foreach (var ev in AsArray(e))
        {
            if (!ev.TryGetProperty("tagInventoryEvent", out var t)) continue;
            var epc = Str(t, "epcHex") ?? (Str(t, "epc") is { } b64 ? Convert.ToHexString(Convert.FromBase64String(b64)) : null);
            if (epc == null) continue;
            b.Reads.Add(new ReadRequest { Epc = epc, Tid = Str(t, "tidHex"), AntennaPort = Int(t, "antennaPort"), Rssi = Dbl(t, "peakRssiCdbm") is { } cd ? cd / 100.0 : Dbl(t, "peakRssi"), ReadAt = Date(ev, "timestamp") ?? Date(t, "lastSeenTime") });
            b.DeviceId ??= null;
            if (Str(ev, "hostname") is { } h) b.SessionId ??= h;
        }
    }

    private static void ParseZebra(JsonElement e, ReadBatchRequest b)
    {
        foreach (var ev in AsArray(e))
        {
            if (!ev.TryGetProperty("data", out var d)) continue;
            var epc = Str(d, "idHex"); if (epc == null) continue;
            b.Reads.Add(new ReadRequest { Epc = epc, Tid = Str(d, "TID"), AntennaPort = Int(d, "antenna"), Rssi = Dbl(d, "peakRssi"), ReadAt = Date(ev, "timestamp") ?? Date(d, "eventTime") });
        }
    }

    private static void ParseGeneric(JsonElement e, ReadBatchRequest b)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (Str(e, "deviceId") is { } dev && Guid.TryParse(dev, out var g)) b.DeviceId = g;
            b.SessionId = Str(e, "sessionId") ?? Str(e, "reader") ?? Str(e, "hostname");
            if (e.TryGetProperty("reads", out var reads) || e.TryGetProperty("tags", out reads) || e.TryGetProperty("events", out reads)) { foreach (var r in AsArray(reads)) AddGeneric(r, b); return; }
            AddGeneric(e, b); return;
        }
        foreach (var r in AsArray(e)) AddGeneric(r, b);
    }

    private static void AddGeneric(JsonElement r, ReadBatchRequest b)
    {
        if (r.ValueKind == JsonValueKind.String) { b.Reads.Add(new ReadRequest { Epc = r.GetString()! }); return; }
        if (r.ValueKind != JsonValueKind.Object) return;
        var epc = Str(r, "epc") ?? Str(r, "epcHex") ?? Str(r, "idHex") ?? Str(r, "tag") ?? Str(r, "id");
        if (epc == null) return;
        b.Reads.Add(new ReadRequest { Epc = epc, Tid = Str(r, "tid") ?? Str(r, "tidHex"), AntennaPort = Int(r, "antennaPort") ?? Int(r, "antenna") ?? Int(r, "port"), Rssi = Dbl(r, "rssi") ?? Dbl(r, "peakRssi"), ReadAt = Date(r, "readAt") ?? Date(r, "timestamp") ?? Date(r, "time"), LocationId = Str(r, "locationId") is { } l && Guid.TryParse(l, out var lg) ? lg : null });
    }

    private static IEnumerable<JsonElement> AsArray(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : new[] { e };
    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out p) && p.ValueKind == JsonValueKind.Number ? p.ToString() : null;
    private static int? Int(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;
    private static double? Dbl(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.Number ? p.GetDouble() : p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null : null;
    private static DateTime? Date(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && DateTime.TryParse(p.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}

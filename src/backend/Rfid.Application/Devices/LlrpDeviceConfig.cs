using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rfid.Application.Contracts;
using Rfid.Domain;
using Rfid.Domain.Entities;
using Rfid.Protocols;
using Rfid.Protocols.Llrp;

namespace Rfid.Application.Devices;

/// <summary>How a device's <c>Config</c> document describes its LLRP endpoint and who drives it (server or an edge gateway).</summary>
public static class LlrpDeviceConfig
{
    public static bool Flag(Device d, string key) => d.Config.TryGetValue(key, out var v) && v?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsEdgeManaged(Device d) => Flag(d, "edgeManaged");
    public static Guid? GatewayId(Device d) => d.Config.TryGetValue("edgeGatewayId", out var v) && Guid.TryParse(v?.ToString(), out var g) ? g : null;

    /// <summary>Host/port when LLRP is configured and not paused; null otherwise (regardless of who drives it).</summary>
    public static (string host, int port)? Endpoint(Device d)
    {
        if (!d.Config.TryGetValue("llrpHost", out var h) || string.IsNullOrWhiteSpace(h?.ToString())) return null;
        if (d.Config.TryGetValue("llrpEnabled", out var en) && en?.ToString()?.Equals("false", StringComparison.OrdinalIgnoreCase) == true) return null;
        var port = d.Config.TryGetValue("llrpPort", out var p) && int.TryParse(p?.ToString(), out var pi) ? pi : 5084;
        return (h!.ToString()!, port);
    }

    public static LlrpReaderOptions Options(Device d)
    {
        var c = d.Config;
        double? Dbl(string k) => c.TryGetValue(k, out var v) && double.TryParse(v?.ToString(), out var x) ? x : null;
        int? Int(string k) => c.TryGetValue(k, out var v) && int.TryParse(v?.ToString(), out var x) ? x : null;
        var ants = c.TryGetValue("llrpAntennas", out var a) && a != null ? a.ToString()!.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => ushort.TryParse(x, out var u) ? u : (ushort)0).Where(u => u > 0).ToArray() : null;
        return new LlrpReaderOptions { TransmitPowerDbm = Dbl("llrpPower"), Session = Int("llrpSession") ?? 1, TagPopulation = Int("llrpTagPopulation") ?? 32, AntennaIds = ants is { Length: > 0 } ? ants : null, GpiStartPort = Int("llrpGpiStart"), ReportEveryNTags = Int("llrpReportEveryN") ?? 1 };
    }
}

/// <summary>Builds the desired configuration for an edge gateway: every edge-managed LLRP reader assigned to it.</summary>
public class EdgeConfigService
{
    private readonly IAppDb _db;
    public EdgeConfigService(IAppDb db) => _db = db;

    public async Task<EdgeConfig> ForGatewayAsync(Guid gatewayDeviceId, CancellationToken ct = default)
    {
        var devices = await _db.Devices.Where(d => d.Kind != DeviceKind.Handheld && d.Kind != DeviceKind.Printer && d.Kind != DeviceKind.Gateway).OrderBy(d => d.Name).ToListAsync(ct);
        var readers = new List<EdgeReader>();
        foreach (var d in devices)
        {
            if (!LlrpDeviceConfig.IsEdgeManaged(d) || LlrpDeviceConfig.GatewayId(d) != gatewayDeviceId) continue;
            var ep = LlrpDeviceConfig.Endpoint(d);
            var o = LlrpDeviceConfig.Options(d);
            readers.Add(new EdgeReader { DeviceId = d.Id, Name = d.Name, Host = ep?.host ?? "", Port = ep?.port ?? 5084, Enabled = ep != null, PowerDbm = o.TransmitPowerDbm, Session = o.Session, TagPopulation = o.TagPopulation, Antennas = o.AntennaIds, GpiStartPort = o.GpiStartPort });
        }
        var cfg = new EdgeConfig { GatewayDeviceId = gatewayDeviceId, Readers = readers };
        cfg.Revision = Revision(cfg);
        return cfg;
    }

    public static string Revision(EdgeConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg.Readers.OrderBy(r => r.DeviceId).ToList());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16].ToLowerInvariant();
    }
}

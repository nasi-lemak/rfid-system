using System.Text.Json;
using Rfid.Protocols;

namespace Rfid.Edge;

/// <summary>
/// The readers this agent drives: the local configuration as the offline baseline, replaced by the platform's
/// desired configuration once pulled (and remembered on disk so a restart without connectivity keeps it).
/// Exposes what changed so the agent reconnects only the readers whose options differ.
/// </summary>
public sealed class ReaderCatalog
{
    private readonly string _file; private readonly List<EdgeOptions.ReaderOptions> _local;
    private EdgeConfig? _server; private readonly object _lock = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public ReaderCatalog(string queueDirectory, IEnumerable<EdgeOptions.ReaderOptions> local)
    {
        _file = Path.Combine(Path.GetFullPath(queueDirectory), "config.json"); _local = local.ToList();
        try { if (File.Exists(_file)) _server = JsonSerializer.Deserialize<EdgeConfig>(File.ReadAllText(_file), Json); } catch (Exception) { _server = null; }
    }

    public string? Revision { get { lock (_lock) return _server?.Revision; } }
    public bool FromServer { get { lock (_lock) return _server != null; } }

    /// <summary>Current desired readers (server configuration when present, else local).</summary>
    public List<EdgeOptions.ReaderOptions> Current()
    {
        lock (_lock)
        {
            if (_server == null) return _local.Where(r => r.Enabled).ToList();
            return _server.Readers.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Host)).Select(r => new EdgeOptions.ReaderOptions
            {
                DeviceId = r.DeviceId, Host = r.Host, Port = r.Port, PowerDbm = r.PowerDbm, Session = r.Session, TagPopulation = r.TagPopulation, Antennas = r.Antennas, GpiStartPort = r.GpiStartPort, Enabled = true,
            }).ToList();
        }
    }

    /// <summary>Applies a pulled configuration. Returns the device ids whose options changed (added, modified or removed) or an empty set when the revision is unchanged.</summary>
    public IReadOnlySet<Guid> Apply(EdgeConfig cfg)
    {
        lock (_lock)
        {
            if (_server?.Revision == cfg.Revision) return new HashSet<Guid>();
            var before = Current().ToDictionary(r => r.DeviceId, Key);
            _server = cfg;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(cfg, Json));
            var after = Current().ToDictionary(r => r.DeviceId, Key);
            var changed = new HashSet<Guid>();
            foreach (var (id, key) in after) if (!before.TryGetValue(id, out var b) || b != key) changed.Add(id);
            foreach (var id in before.Keys) if (!after.ContainsKey(id)) changed.Add(id);
            return changed;
        }
    }

    public static string Key(EdgeOptions.ReaderOptions r) => JsonSerializer.Serialize(new { r.Host, r.Port, r.PowerDbm, r.Session, r.TagPopulation, r.Antennas, r.GpiStartPort });
}

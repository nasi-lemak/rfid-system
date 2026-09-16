namespace Rfid.Edge;

/// <summary>Agent configuration (section <c>Edge</c>; environment variables use <c>Edge__Server__Url</c> style).</summary>
public class EdgeOptions
{
    /// <summary>Stable identifier of this agent; becomes the prefix of every batch id. Defaults to the machine name.</summary>
    public string AgentId { get; set; } = Environment.MachineName;
    public ServerOptions Server { get; set; } = new();
    public QueueOptions Queue { get; set; } = new();
    public FlushOptions Flush { get; set; } = new();
    public List<ReaderOptions> Readers { get; set; } = new();
    /// <summary>Optional HTTP listener for reader push formats (Impinj IoT Interface, Zebra IoT Connector, generic JSON), e.g. http://0.0.0.0:8090/.</summary>
    public string? Listen { get; set; }
    public int HeartbeatSeconds { get; set; } = 60;

    public class ServerOptions
    {
        public string Url { get; set; } = "http://localhost:5080";
        /// <summary>Provisioning token of the gateway device this agent authenticates as (Devices → token).</summary>
        public string DeviceToken { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 30;
    }

    public class QueueOptions
    {
        public string Path { get; set; } = "./queue";
        /// <summary>Oldest batches are dropped (to <c>poison/</c>) beyond this many pending batches.</summary>
        public int MaxBatches { get; set; } = 20_000;
        public int MaxBackoffSeconds { get; set; } = 300;
    }

    public class FlushOptions
    {
        public double Seconds { get; set; } = 2;
        public int MaxReads { get; set; } = 500;
    }

    public class ReaderOptions
    {
        /// <summary>The platform device id this reader's reads are attributed to.</summary>
        public Guid DeviceId { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 5084;
        public double? PowerDbm { get; set; }
        public int Session { get; set; } = 1;
        public int TagPopulation { get; set; } = 32;
        public ushort[]? Antennas { get; set; }
        public int? GpiStartPort { get; set; }
        public bool Enabled { get; set; } = true;
    }
}

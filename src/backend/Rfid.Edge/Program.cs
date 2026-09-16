using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rfid.Edge;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<EdgeOptions>(builder.Configuration.GetSection("Edge"));
builder.Services.AddSingleton(sp => { var o = sp.GetRequiredService<IOptions<EdgeOptions>>().Value; return new EdgeQueue(o.Queue.Path, o.AgentId, o.Queue.MaxBatches); });
builder.Services.AddSingleton(sp => { var o = sp.GetRequiredService<IOptions<EdgeOptions>>().Value; return new ReaderCatalog(o.Queue.Path, o.Readers); });
builder.Services.AddSingleton(sp => new ReadBuffer(sp.GetRequiredService<IOptions<EdgeOptions>>().Value.Flush.MaxReads));
// One client instance for the whole agent: it caches the device JWT and re-logs in on 401.
builder.Services.AddSingleton<IServerClient>(sp => new HttpServerClient(new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }), sp.GetRequiredService<IOptions<EdgeOptions>>().Value.Server));
builder.Services.AddSingleton(sp => new Forwarder(sp.GetRequiredService<EdgeQueue>(), sp.GetRequiredService<IServerClient>(), sp.GetRequiredService<ILogger<Forwarder>>(), sp.GetRequiredService<IOptions<EdgeOptions>>().Value.Queue.MaxBackoffSeconds));
builder.Services.AddHostedService<EdgeAgent>();
await builder.Build().RunAsync();

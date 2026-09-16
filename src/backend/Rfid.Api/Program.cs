using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Rfid.Api.Auth;
using Rfid.Api.Hubs;
using Rfid.Application.Cluster;
using Rfid.Application.Contracts;
using Rfid.Application.Security;
using Rfid.Application.Services;
using Rfid.Application.Templates;
using Rfid.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "RFID Platform API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { In = ParameterLocation.Header, Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer" });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() } });
});
var signalR = builder.Services.AddSignalR();
var redis = cfg["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redis)) signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(cfg["Redis:ChannelPrefix"] ?? "rfid"));
builder.Services.AddMemoryCache();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Default")));
builder.Services.AddScoped<IAppDb>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentContext, RequestOrAmbientContext>();
builder.Services.AddScoped<ILivePublisher, SignalRLivePublisher>();
builder.Services.AddHttpClient<IWebhookDispatcher, HttpWebhookDispatcher>();
builder.Services.AddScoped<TagResolver>();
builder.Services.AddScoped<RuleEngine>(sp => new RuleEngine(sp.GetRequiredService<IAppDb>(), sp.GetRequiredService<ICurrentContext>(), sp.GetRequiredService<ILivePublisher>(), sp.GetRequiredService<IWebhookDispatcher>(), sp.GetRequiredService<NotificationService>()));
builder.Services.AddScoped<OperationProcessor>();
builder.Services.AddScoped<StocktakeService>();
builder.Services.AddScoped<ReadIngestionService>();
builder.Services.AddScoped<TemplateProvisioner>();
builder.Services.AddScoped<PresenceService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<StocktakeScheduleService>();
builder.Services.AddScoped<IntegrationService>();
builder.Services.AddHttpClient<IIntegrationTransport, Rfid.Api.Background.HttpIntegrationTransport>();
builder.Services.AddSingleton<IPrinterClient, Rfid.Api.Background.RawPrinterClient>();
builder.Services.AddHostedService<Rfid.Api.Background.PresenceSweeperService>();
builder.Services.AddHostedService<Rfid.Api.Background.StocktakeSchedulerService>();
builder.Services.AddHostedService<Rfid.Api.Background.IntegrationDispatcherService>();
builder.Services.AddHostedService<Rfid.Api.Background.MqttIngestService>();
builder.Services.AddSingleton<Rfid.Api.Background.LlrpReaderService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Rfid.Api.Background.LlrpReaderService>());
builder.Services.AddSingleton<Rfid.Application.Positioning.IPositionSmoother, Rfid.Application.Positioning.PersistentPositionSmoother>();
builder.Services.AddScoped<PositionService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<PrintQueueService>();
builder.Services.AddHostedService<Rfid.Api.Background.PrintQueueWorkerService>();
builder.Services.AddHttpClient<IOAuthTokenProvider, Rfid.Api.Background.HttpOAuthTokenProvider>();
builder.Services.AddScoped<Rfid.Infrastructure.Persistence.Demo.DemoSeeder>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddScoped<LeaseService>();
builder.Services.AddHttpClient<INotificationSender, Rfid.Api.Background.HttpNotificationSender>();
builder.Services.AddScoped<NotificationService>(sp => new NotificationService(sp.GetRequiredService<IAppDb>(), sp.GetRequiredService<INotificationSender>()) { BaseUrl = cfg["Notifications:BaseUrl"] ?? cfg.GetSection("Cors:Origins").Get<string[]>()?.FirstOrDefault() });
builder.Services.AddScoped<DeviceHealthService>(sp => new DeviceHealthService(sp.GetRequiredService<IAppDb>(), sp.GetRequiredService<ICurrentContext>(), sp.GetRequiredService<NotificationService>()) { DefaultSlaMinutes = cfg.GetValue("DeviceHealth:DefaultSlaMinutes", 15) });
builder.Services.AddScoped<GeoService>(sp => new GeoService(sp.GetRequiredService<IAppDb>(), sp.GetRequiredService<ICurrentContext>(), sp.GetRequiredService<RuleEngine>(), sp.GetRequiredService<ILivePublisher>(), sp.GetRequiredService<NotificationService>()));
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<WarehouseExportService>(sp => new WarehouseExportService(sp.GetRequiredService<IAppDb>(), sp.GetRequiredService<ICurrentContext>()) { RootPath = cfg["Warehouse:Path"] ?? Path.Combine(builder.Environment.ContentRootPath, "warehouse"), DefaultFormat = cfg["Warehouse:Format"] ?? "parquet", InitialWindow = TimeSpan.FromDays(cfg.GetValue("Warehouse:InitialDays", 30)) });
builder.Services.AddScoped<AnomalyService>(sp => new AnomalyService(sp.GetRequiredService<IAppDb>(), sp.GetRequiredService<ICurrentContext>(), sp.GetRequiredService<NotificationService>()) { BaselineDays = cfg.GetValue("Anomaly:BaselineDays", 14), ZThreshold = cfg.GetValue("Anomaly:ZThreshold", 3.0), AlertZ = cfg.GetValue("Anomaly:AlertZ", 4.0), MinReads = cfg.GetValue("Anomaly:MinReads", 20) });
builder.Services.AddScoped<EncodingService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddHostedService<Rfid.Api.Background.AnomalyDetectionService>();
builder.Services.AddHostedService<Rfid.Api.Background.RetentionJobService>();
builder.Services.AddHostedService<Rfid.Api.Background.MonitoringService>();
builder.Services.AddHostedService<Rfid.Api.Background.WarehouseExportJobService>();
builder.Services.AddScoped<SsoUserMapper>();
builder.Services.Configure<SsoOptions>(cfg.GetSection("Oidc"));
builder.Services.AddScoped<ISiteAccess>(SiteAccessFactory.Create);
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, OidcClaimsTransformation>();

var jwtKey = cfg["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
var sso = cfg.GetSection("Oidc").Get<SsoOptions>() ?? new SsoOptions();
var ssoEnabled = sso.Enabled && !string.IsNullOrWhiteSpace(sso.Authority);
static void AcceptHubQueryToken(JwtBearerOptions o) => o.Events = new JwtBearerEvents
{
    // SignalR sends the token as a query string parameter.
    OnMessageReceived = ctx =>
    {
        var token = ctx.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
        return Task.CompletedTask;
    }
};
var auth = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = cfg["Jwt:Issuer"] ?? "rfid-platform",
        ValidateAudience = false, ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
    };
    AcceptHubQueryToken(o);
});
if (ssoEnabled)
{
    // Second bearer scheme: tokens issued by the external OIDC provider (Entra ID, Keycloak, Okta, Auth0 …).
    auth.AddJwtBearer("oidc", o =>
    {
        o.Authority = sso.Authority; o.RequireHttpsMetadata = sso.Authority!.StartsWith("https", StringComparison.OrdinalIgnoreCase);
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters { ValidateAudience = !string.IsNullOrEmpty(sso.Audience), ValidAudiences = new[] { sso.Audience ?? "", sso.ClientId ?? "" }, NameClaimType = "name", RoleClaimType = sso.RoleClaim };
        AcceptHubQueryToken(o);
    });
}
builder.Services.AddAuthorization(o =>
{
    var schemes = ssoEnabled ? new[] { JwtBearerDefaults.AuthenticationScheme, "oidc" } : new[] { JwtBearerDefaults.AuthenticationScheme };
    o.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(schemes).RequireAuthenticatedUser().RequireClaim(PlatformClaims.Tenant).Build();
    o.AddPolicy("Admin", p => p.AddAuthenticationSchemes(schemes).RequireAuthenticatedUser().RequireClaim(PlatformClaims.Tenant).RequireRole("Admin"));
    // Operators: global role, a device token, or an Operator/Admin role on at least one site (per-location checks happen in the controllers).
    o.AddPolicy("Operator", p => p.AddAuthenticationSchemes(schemes).RequireAuthenticatedUser().RequireClaim(PlatformClaims.Tenant).RequireAssertion(c => PlatformClaims.CanOperateSomewhere(c.User)));
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditMiddleware>();
app.MapControllers();
app.MapHub<LiveHub>("/hubs/live");
app.MapGet("/health", () => Results.Ok(new { status = "ok", node = ClusterNode.Id, time = DateTime.UtcNow }));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (cfg.GetValue("Database:AutoMigrate", true)) await db.Database.MigrateAsync();
    if (cfg.GetValue("Database:Seed", true))
    {
        var results = await SeedData.EnsureSeededAsync(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            cfg["Seed:AdminEmail"] ?? "admin@demo.local", cfg["Seed:AdminPassword"] ?? "admin123", cfg["Seed:Scenarios"] ?? "all");
        var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        foreach (var r in results.Where(r => !r.Skipped))
            log.LogInformation("Seeded scenario {Scenario}: {Items} items, {Ops} operations, {Reads} reads, {Alerts} alerts{Warn}", r.Scenario, r.Items, r.Operations, r.Reads, r.Alerts, r.Warnings.Count > 0 ? " · warnings: " + string.Join("; ", r.Warnings) : "");
    }
}

app.Run();

public partial class Program { }

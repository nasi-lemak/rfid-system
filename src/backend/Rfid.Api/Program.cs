using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Rfid.Api.Auth;
using Rfid.Api.Hubs;
using Rfid.Application.Contracts;
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
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cfg.GetConnectionString("Default")));
builder.Services.AddScoped<IAppDb>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentContext, HttpCurrentContext>();
builder.Services.AddScoped<ILivePublisher, SignalRLivePublisher>();
builder.Services.AddHttpClient<IWebhookDispatcher, HttpWebhookDispatcher>();
builder.Services.AddScoped<TagResolver>();
builder.Services.AddScoped<RuleEngine>();
builder.Services.AddScoped<OperationProcessor>();
builder.Services.AddScoped<StocktakeService>();
builder.Services.AddScoped<ReadIngestionService>();
builder.Services.AddScoped<TemplateProvisioner>();
builder.Services.AddScoped<Rfid.Infrastructure.Persistence.Demo.DemoSeeder>();
builder.Services.AddSingleton<JwtService>();

var jwtKey = cfg["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = cfg["Jwt:Issuer"] ?? "rfid-platform",
        ValidateAudience = false, ValidateLifetime = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
    };
    // SignalR sends the token as a query string parameter.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs")) ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Admin", p => p.RequireRole("Admin"));
    o.AddPolicy("Operator", p => p.RequireRole("Admin", "Operator", "Device"));
});

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<LiveHub>("/hubs/live");
app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));

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

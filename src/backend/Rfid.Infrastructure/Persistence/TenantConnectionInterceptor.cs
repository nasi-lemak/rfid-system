using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Rfid.Application.Contracts;

namespace Rfid.Infrastructure.Persistence;

/// <summary>
/// Pins every opened connection to the current tenant (<c>set_config('app.tenant_id', …)</c>) so the
/// row-level-security policies in <see cref="RowLevelSecurity"/> apply. A context with no tenant
/// (pre-authentication requests, cluster-wide background loops) runs in the explicit system scope.
/// Npgsql resets session state when a pooled connection is returned, so the setting never leaks
/// between requests; do not disable that reset ("No Reset On Close").
/// </summary>
public sealed class TenantConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ICurrentContext _ctx;
    public TenantConnectionInterceptor(ICurrentContext ctx) => _ctx = ctx;

    public static string Scope(Guid tenantId) => tenantId == Guid.Empty ? RowLevelSecurity.SystemScope : tenantId.ToString();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = Command(connection);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        using var cmd = Command(connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand Command(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT set_config('{RowLevelSecurity.Setting}', @scope, false)";
        var p = cmd.CreateParameter(); p.ParameterName = "scope"; p.Value = Scope(_ctx.TenantId);
        cmd.Parameters.Add(p);
        return cmd;
    }
}

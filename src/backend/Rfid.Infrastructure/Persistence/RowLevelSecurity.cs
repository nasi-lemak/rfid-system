using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rfid.Infrastructure.Persistence;

/// <summary>
/// Second line of tenant isolation, below the EF global query filters: every tenant-scoped table carries a
/// Postgres row-level-security policy that only exposes rows whose <c>TenantId</c> matches the connection's
/// <c>app.tenant_id</c> setting (set per connection by <see cref="TenantConnectionInterceptor"/>).
/// The API connects as a non-owner role (<see cref="AppRole"/>) so the policies actually apply; the owner
/// role (used for migrations) bypasses them. The literal <see cref="SystemScope"/> is the explicit,
/// auditable "no tenant" mode used only by pre-authentication lookups and background infrastructure.
/// </summary>
public static class RowLevelSecurity
{
    public const string Setting = "app.tenant_id";
    public const string SystemScope = "*";
    public const string PolicyName = "tenant_isolation";
    public const string AppRole = "rfid_app";
    public const string FunctionName = "app_tenant_allows";

    /// <summary>A table is tenant-scoped when it has a non-nullable Guid <c>TenantId</c> column (every <c>TenantEntity</c>).</summary>
    public static IReadOnlyList<(string schema, string table)> TenantTables(IModel model)
    {
        var defaultSchema = model.GetDefaultSchema() ?? "public";
        return model.GetEntityTypes()
            .Where(e => !e.IsOwned() && e.GetTableName() != null && e.FindProperty("TenantId") is { ClrType: var t, IsNullable: false } && t == typeof(Guid))
            .Select(e => (e.GetSchema() ?? defaultSchema, e.GetTableName()!))
            .Distinct().OrderBy(x => x.Item2).ToList();
    }

    public static string FunctionSql(string schema) => $$"""
        CREATE OR REPLACE FUNCTION "{{schema}}".{{FunctionName}}(row_tenant uuid) RETURNS boolean
        LANGUAGE sql STABLE AS $fn$
          SELECT CASE
            WHEN current_setting('{{Setting}}', true) IS NULL OR current_setting('{{Setting}}', true) = '' THEN false
            WHEN current_setting('{{Setting}}', true) = '{{SystemScope}}' THEN true
            ELSE row_tenant = current_setting('{{Setting}}', true)::uuid
          END
        $fn$;
        """;

    public static string EnableSql(IEnumerable<(string schema, string table)> tables)
    {
        var list = tables.ToList();
        if (list.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var schema in list.Select(t => t.schema).Distinct()) sb.AppendLine(FunctionSql(schema));
        foreach (var (schema, table) in list)
        {
            sb.AppendLine($"ALTER TABLE \"{schema}\".\"{table}\" ENABLE ROW LEVEL SECURITY;");
            sb.AppendLine($"DROP POLICY IF EXISTS {PolicyName} ON \"{schema}\".\"{table}\";");
            sb.AppendLine($"CREATE POLICY {PolicyName} ON \"{schema}\".\"{table}\" USING (\"{schema}\".{FunctionName}(\"TenantId\")) WITH CHECK (\"{schema}\".{FunctionName}(\"TenantId\"));");
        }
        return sb.ToString();
    }

    public static string DisableSql(IEnumerable<(string schema, string table)> tables)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (schema, table) in tables)
        {
            sb.AppendLine($"DROP POLICY IF EXISTS {PolicyName} ON \"{schema}\".\"{table}\";");
            sb.AppendLine($"ALTER TABLE \"{schema}\".\"{table}\" DISABLE ROW LEVEL SECURITY;");
        }
        return sb.ToString();
    }

    /// <summary>Grants the runtime role access to everything in the schema when that role exists (no-op otherwise, e.g. local dev as the owner).</summary>
    public static string GrantsSql(string schema) => $$"""
        DO $do$ BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
            GRANT USAGE ON SCHEMA "{{schema}}" TO {{AppRole}};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA "{{schema}}" TO {{AppRole}};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA "{{schema}}" TO {{AppRole}};
            GRANT EXECUTE ON FUNCTION "{{schema}}".{{FunctionName}}(uuid) TO {{AppRole}};
          END IF;
        END $do$;
        """;

    /// <summary>Idempotent: (re)creates the policies for every tenant table in the current model and refreshes grants. Run after migrations by the owner role.</summary>
    public static async Task EnsureAsync(DbContext db, CancellationToken ct = default)
    {
        if (!db.Database.IsNpgsql()) return;
        var tables = TenantTables(db.Model);
        var sql = EnableSql(tables) + string.Concat(tables.Select(t => t.schema).Distinct().Select(GrantsSql));
        if (sql.Length > 0) await db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}

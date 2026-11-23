using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PyroLens.Infrastructure.Persistence;

/// <summary>
/// Applies the embedded SQL scripts in Persistence/Migrations in name order, once each.
/// A session advisory lock lets the API and the worker start at the same time safely.
/// </summary>
public sealed class DatabaseMigrator(PyroLensDbContext db, ILogger<DatabaseMigrator> logger)
{
    private const long MigrationLockKey = 7_212_001;
    private const string ResourcePrefix = "PyroLens.Infrastructure.Persistence.Migrations.";

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(ct);

        try
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_lock({MigrationLockKey})", ct);

            await ExecuteAsync(connection, null, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version    text PRIMARY KEY,
                    applied_at timestamptz NOT NULL DEFAULT now())
                """, ct);

            var applied = new HashSet<string>();
            await using (var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations", connection))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            foreach (var (version, sql) in LoadScripts().Where(s => !applied.Contains(s.Version)))
            {
                await using var tx = await connection.BeginTransactionAsync(ct);
                await ExecuteAsync(connection, tx, sql, ct);

                await using (var insert = new NpgsqlCommand(
                    "INSERT INTO schema_migrations (version) VALUES (@version)", connection, tx))
                {
                    insert.Parameters.AddWithValue("version", version);
                    await insert.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
                logger.LogInformation("Applied migration {Version}", version);
            }

            // Types such as geometry may have been created just now.
            await connection.ReloadTypesAsync(ct);
        }
        finally
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_unlock({MigrationLockKey})", CancellationToken.None);
            await connection.CloseAsync();
        }
    }

    private static IEnumerable<(string Version, string Sql)> LoadScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (name[ResourcePrefix.Length..^".sql".Length], reader.ReadToEnd());
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

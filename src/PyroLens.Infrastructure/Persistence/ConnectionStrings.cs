using Npgsql;

namespace PyroLens.Infrastructure.Persistence;

public static class ConnectionStrings
{
    /// <summary>
    /// Accepts either an Npgsql connection string ("Host=…;Database=…") or a URL as hosting providers show it
    /// ("postgresql://user:password@host:5432/db?sslmode=require"), and returns an Npgsql connection string.
    /// URLs default to SSL Mode=Require and a small pool, which suits free-tier poolers with few connections.
    /// </summary>
    public static string Normalize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var value = connectionString.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(value).Host))
                {
                    return value;
                }
            }
            catch (ArgumentException)
            {
                // Not "key=value;…" at all; reported below.
            }

            // Never include the value: it may be a password pasted into the wrong field.
            throw new FormatException(
                "The database connection string is not valid. Expected a URL such as " +
                "postgresql://user:password@host:5432/database or a connection string such as " +
                "Host=…;Database=…;Username=…;Password=….");
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')) is { Length: > 0 } db ? db : "postgres",
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = SslMode.Require,
            MaxPoolSize = 5,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]).ToLowerInvariant();
            var setting = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            switch (key)
            {
                case "sslmode":
                    builder.SslMode = setting.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "require" => SslMode.Require,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => throw new FormatException($"Unknown sslmode '{setting}' in the database URL."),
                    };
                    break;
                case "application_name":
                    builder.ApplicationName = setting;
                    break;
                // Other URL options (e.g. pgbouncer=true) have no Npgsql equivalent and are ignored.
            }
        }

        return builder.ConnectionString;
    }
}

using Npgsql;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.IntegrationTests;

/// <summary>Database URLs as hosting dashboards show them. No database needed.</summary>
public sealed class ConnectionStringTests
{
    [Fact]
    public void Npgsql_connection_strings_pass_through_unchanged()
    {
        const string value = "Host=localhost;Port=5432;Database=pyrolens;Username=pyrolens;Password=pyrolens";

        Assert.Equal(value, ConnectionStrings.Normalize(value));
    }

    [Fact]
    public void Pooler_url_becomes_an_npgsql_connection_string()
    {
        // Shape of a Supabase session-pooler URL; the password has characters that must be URL-encoded.
        var result = new NpgsqlConnectionStringBuilder(ConnectionStrings.Normalize(
            "postgresql://postgres.abcdefghijklmnop:p%40ss%3Aw0rd%2F1@aws-0-eu-central-1.pooler.supabase.com:5432/postgres"));

        Assert.Equal("aws-0-eu-central-1.pooler.supabase.com", result.Host);
        Assert.Equal(5432, result.Port);
        Assert.Equal("postgres", result.Database);
        Assert.Equal("postgres.abcdefghijklmnop", result.Username);
        Assert.Equal("p@ss:w0rd/1", result.Password);
        Assert.Equal(SslMode.Require, result.SslMode);
        Assert.Equal(5, result.MaxPoolSize);
    }

    [Fact]
    public void Url_options_and_defaults_are_applied()
    {
        var result = new NpgsqlConnectionStringBuilder(ConnectionStrings.Normalize(
            "postgres://user@db.example.com/?sslmode=verify-full&application_name=pyrolens&pgbouncer=true"));

        Assert.Equal(5432, result.Port);
        Assert.Equal("postgres", result.Database);
        Assert.Null(result.Password);
        Assert.Equal(SslMode.VerifyFull, result.SslMode);
        Assert.Equal("pyrolens", result.ApplicationName);
    }

    [Fact]
    public void Unknown_sslmode_is_rejected()
    {
        Assert.Throws<FormatException>(() => ConnectionStrings.Normalize("postgresql://u:p@h/db?sslmode=sometimes"));
    }
}

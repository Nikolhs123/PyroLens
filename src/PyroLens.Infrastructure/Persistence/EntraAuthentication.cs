using Azure.Core;
using Azure.Identity;
using Npgsql;

namespace PyroLens.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>
    /// Sign in to Azure Database for PostgreSQL with a Microsoft Entra token instead of a password.
    /// The connection string then carries only host, database and the identity's name as Username.
    /// </summary>
    public bool UseEntraId { get; set; }

    /// <summary>Client id of a user-assigned managed identity. Empty uses the default credential chain (e.g. az login locally).</summary>
    public string? ManagedIdentityClientId { get; set; }
}

internal static class EntraAuthentication
{
    private static readonly string[] Scopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    public static void Configure(NpgsqlDataSourceBuilder builder, string? managedIdentityClientId)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(managedIdentityClientId) ? null : managedIdentityClientId,
        });

        // Tokens live about an hour; Npgsql refreshes in the background and uses the latest for new connections.
        builder.UsePeriodicPasswordProvider(
            async (_, ct) => (await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct)).Token,
            successRefreshInterval: TimeSpan.FromMinutes(45),
            failureRefreshInterval: TimeSpan.FromSeconds(10));
    }
}

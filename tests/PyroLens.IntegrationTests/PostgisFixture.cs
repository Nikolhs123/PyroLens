using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PyroLens.Infrastructure;
using Testcontainers.PostgreSql;

namespace PyroLens.IntegrationTests;

/// <summary>One PostGIS container per test run; every test gets its own empty database in it.</summary>
public sealed class PostgisFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgis/postgis:17-3.5")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<ServiceProvider> CreateServicesAsync(DateTimeOffset now)
    {
        var database = "test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PyroLens"] = connectionString,
                ["Firms:Mode"] = "File",
                ["Firms:SampleFile"] = Path.Combine(AppContext.BaseDirectory, "samples", "firms_sample_synthetic.csv"),
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddPyroLensIngestion(configuration)
            .AddSingleton<TimeProvider>(new FixedClock(now))
            .BuildServiceProvider();

        await services.MigrateDatabaseAsync();
        return services;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgisCollection : ICollectionFixture<PostgisFixture>
{
    public const string Name = "postgis";
}

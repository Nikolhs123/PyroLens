using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PyroLens.Infrastructure.Firms;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Persistence;

namespace PyroLens.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "PyroLens";

    /// <summary>Database, options and the read side. Used by both the API and the worker.</summary>
    public static IServiceCollection AddPyroLensCore(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing.");

        services.AddDbContext<PyroLensDbContext>(o => o.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
        services.AddScoped<DatabaseMigrator>();
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<DetectionFilterOptions>()
            .Bind(configuration.GetSection(DetectionFilterOptions.Section))
            .PostConfigure(o =>
            {
                if (o.ViirsConfidence.Count == 0)
                {
                    o.ViirsConfidence = [.. DetectionFilterOptions.DefaultViirsConfidence];
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ClusteringOptions>().Bind(configuration.GetSection(ClusteringOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        return services;
    }

    /// <summary>FIRMS ingestion and clustering. Used by the worker only.</summary>
    public static IServiceCollection AddPyroLensIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPyroLensCore(configuration);

        services.AddOptions<FirmsOptions>()
            .Bind(configuration.GetSection(FirmsOptions.Section))
            .PostConfigure(o =>
            {
                if (o.Sources.Count == 0)
                {
                    o.Sources = [.. FirmsOptions.DefaultSources];
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<IngestionOptions>().Bind(configuration.GetSection(IngestionOptions.Section));

        var firms = configuration.GetSection(FirmsOptions.Section).Get<FirmsOptions>() ?? new FirmsOptions();
        if (firms.Mode == FirmsMode.Api)
        {
            services.AddHttpClient<IFirmsSource, FirmsApiSource>(http =>
                {
                    http.BaseAddress = new Uri(firms.BaseUrl);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("PyroLens/0.1");
                })
                // The default HttpClient logs print the full URL, which contains the MAP_KEY.
                .RemoveAllLoggers()
                .AddStandardResilienceHandler();
        }
        else
        {
            services.AddSingleton<IFirmsSource, FirmsFileSource>();
        }

        services.AddScoped<DetectionWriter>();
        services.AddScoped<IncidentClusterer>();
        services.AddScoped<IngestionService>();

        return services;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(ct);
    }
}

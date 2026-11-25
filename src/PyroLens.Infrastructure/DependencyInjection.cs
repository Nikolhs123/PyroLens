using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PyroLens.Infrastructure.Firms;
using PyroLens.Infrastructure.Forecasting;
using PyroLens.Infrastructure.Ingestion;
using PyroLens.Infrastructure.Jobs;
using PyroLens.Infrastructure.Operations;
using PyroLens.Infrastructure.Persistence;
using PyroLens.Infrastructure.Weather;

namespace PyroLens.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "PyroLens";

    /// <summary>Database, options and the read side. Used by both the API and the worker.</summary>
    public static IServiceCollection AddPyroLensCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Ingestion and forecasting both call this; register once.
        if (services.Any(d => d.ServiceType == typeof(DatabaseMigrator)))
        {
            return services;
        }

        var rawConnectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is missing. Set it in appsettings or with the " +
                $"environment variable ConnectionStrings__{ConnectionStringName} (a Npgsql connection string or a postgresql:// URL).");

        var database = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();

        // Normalized when the database is first used, not here: a malformed value then shows up as a clear
        // error in the logs and /health/ready while the API keeps serving the map.
        services.AddDbContext<PyroLensDbContext>(o => o.UseNpgsql(ConnectionStrings.Normalize(rawConnectionString), npgsql =>
        {
            npgsql.UseNetTopologySuite();
            if (database.UseEntraId)
            {
                // Azure: no password; a managed identity gets a short-lived Entra token instead.
                npgsql.ConfigureDataSource(dataSource => EntraAuthentication.Configure(dataSource, database.ManagedIdentityClientId));
            }
        }));
        services.AddScoped<DatabaseMigrator>();
        services.AddScoped<JobRunner>();
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

        // Dispatcher actions: lifecycle, resources, airspace.
        services.AddOptions<OperationsOptions>().Bind(configuration.GetSection(OperationsOptions.Section));
        services.AddScoped<IncidentOperations>();
        services.AddScoped<ResourceOperations>();
        services.AddScoped<AirspaceOperations>();

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

    /// <summary>Weather and spread forecasts. Used by the worker only.</summary>
    public static IServiceCollection AddPyroLensForecasting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPyroLensCore(configuration);

        services.AddOptions<ForecastOptions>().Bind(configuration.GetSection(ForecastOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        var forecast = configuration.GetSection(ForecastOptions.Section).Get<ForecastOptions>() ?? new ForecastOptions();
        services.AddHttpClient<IWeatherProvider, OpenMeteoClient>(http =>
            {
                http.BaseAddress = new Uri(forecast.OpenMeteoBaseUrl);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PyroLens/0.2");
            })
            .AddStandardResilienceHandler();

        services.AddScoped<ForecastService>();
        return services;
    }

    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(ct);
    }
}

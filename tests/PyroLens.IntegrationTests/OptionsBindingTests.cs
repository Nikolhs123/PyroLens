using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PyroLens.Infrastructure;

namespace PyroLens.IntegrationTests;

/// <summary>
/// Regression: the configuration binder appends to lists that already hold defaults, which made every FIRMS
/// product run twice per cycle and made it impossible to narrow the VIIRS filter from configuration.
/// No database needed.
/// </summary>
public sealed class OptionsBindingTests
{
    [Fact]
    public void Configured_lists_replace_the_defaults()
    {
        using var services = Build(new()
        {
            ["Firms:Sources:0"] = "MODIS_NRT",
            ["DetectionFilter:ViirsConfidence:0"] = "high",
        });

        Assert.Equal(["MODIS_NRT"], services.GetRequiredService<IOptions<FirmsOptions>>().Value.Sources);
        Assert.Equal(["high"], services.GetRequiredService<IOptions<DetectionFilterOptions>>().Value.ViirsConfidence);
    }

    [Fact]
    public void Missing_lists_fall_back_to_the_defaults()
    {
        using var services = Build(new());

        Assert.Equal(FirmsOptions.DefaultSources, services.GetRequiredService<IOptions<FirmsOptions>>().Value.Sources);
        Assert.Equal(DetectionFilterOptions.DefaultViirsConfidence, services.GetRequiredService<IOptions<DetectionFilterOptions>>().Value.ViirsConfidence);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:PyroLens"] = "Host=unused";

        return new ServiceCollection()
            .AddLogging()
            .AddPyroLensIngestion(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();
    }
}

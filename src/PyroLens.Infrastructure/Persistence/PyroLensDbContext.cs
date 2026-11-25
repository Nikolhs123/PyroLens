using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PyroLens.Core.Detections;
using PyroLens.Core.Forecasting;
using PyroLens.Core.Incidents;

namespace PyroLens.Infrastructure.Persistence;

/// <summary>
/// EF Core is used for reads. The schema is owned by the SQL scripts in Persistence/Migrations,
/// and bulk writes and clustering run as SQL (see Ingestion/).
/// </summary>
public sealed class PyroLensDbContext(DbContextOptions<PyroLensDbContext> options) : DbContext(options)
{
    public DbSet<FireDetection> Detections => Set<FireDetection>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentForecast> Forecasts => Set<IncidentForecast>();
    public DbSet<IncidentEvent> Events => Set<IncidentEvent>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<AirspaceReservation> Airspace => Set<AirspaceReservation>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();

    /// <summary>Database spelling of an enum value: StatusChanged -> "status_changed".</summary>
    public static string ToSnakeCase<T>(T value) where T : struct, Enum =>
        string.Concat(value.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public static T FromSnakeCase<T>(string value) where T : struct, Enum => Enum.Parse<T>(value.Replace("_", ""), ignoreCase: true);

    private static ValueConverter<T, string> SnakeCase<T>() where T : struct, Enum =>
        new(v => ToSnakeCase(v), v => FromSnakeCase<T>(v));

    /// <summary>Database spelling of a rating, e.g. VeryHigh -> "very-high".</summary>
    public static string ToDbValue(FireDangerRating rating) => rating switch
    {
        FireDangerRating.LowModerate => "low-moderate",
        FireDangerRating.High => "high",
        FireDangerRating.VeryHigh => "very-high",
        FireDangerRating.Severe => "severe",
        FireDangerRating.Extreme => "extreme",
        FireDangerRating.Catastrophic => "catastrophic",
        _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, null),
    };

    public static FireDangerRating FromDbValue(string value) =>
        Enum.GetValues<FireDangerRating>().Single(r => ToDbValue(r) == value);

    protected override void OnModelCreating(ModelBuilder model)
    {
        var instrument = new ValueConverter<Instrument, string>(
            v => v.ToString().ToUpperInvariant(),
            v => Enum.Parse<Instrument>(v, true));

        var confidenceClass = new ValueConverter<ConfidenceClass, string>(
            v => v.ToString().ToLowerInvariant(),
            v => Enum.Parse<ConfidenceClass>(v, true));

        var status = new ValueConverter<IncidentStatus, string>(
            v => v.ToString().ToLowerInvariant(),
            v => Enum.Parse<IncidentStatus>(v, true));

        model.Entity<FireDetection>(e =>
        {
            e.ToTable("fire_detections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Source).HasColumnName("source");
            e.Property(x => x.Instrument).HasColumnName("instrument").HasConversion(instrument);
            e.Property(x => x.Satellite).HasColumnName("satellite");
            e.Property(x => x.Latitude).HasColumnName("latitude");
            e.Property(x => x.Longitude).HasColumnName("longitude");
            e.Property(x => x.Location).HasColumnName("location").HasColumnType("geometry(Point,4326)")
                .ValueGeneratedOnAddOrUpdate();
            e.Property(x => x.AcquiredAt).HasColumnName("acquired_at");
            e.Property(x => x.ConfidenceRaw).HasColumnName("confidence_raw");
            e.Property(x => x.ConfidenceClass).HasColumnName("confidence_class").HasConversion(confidenceClass);
            e.Property(x => x.ConfidencePercent).HasColumnName("confidence_pct");
            e.Property(x => x.BrightnessKelvin).HasColumnName("brightness_k");
            e.Property(x => x.FrpMegawatts).HasColumnName("frp_mw");
            e.Property(x => x.IsDaytime).HasColumnName("is_daytime");
            e.Property(x => x.LikelyIndustrial).HasColumnName("likely_industrial");
            e.Property(x => x.AreaCode).HasColumnName("area_code");
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.IngestedAt).HasColumnName("ingested_at");
            e.HasOne(x => x.Incident).WithMany(i => i.Detections).HasForeignKey(x => x.IncidentId);
        });

        model.Entity<Incident>(e =>
        {
            e.ToTable("incidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Status).HasColumnName("status").HasConversion(status);
            e.Property(x => x.Centroid).HasColumnName("centroid").HasColumnType("geometry(Point,4326)");
            e.Property(x => x.Footprint).HasColumnName("footprint").HasColumnType("geometry(Geometry,4326)");
            e.Property(x => x.FirstDetectedAt).HasColumnName("first_detected_at");
            e.Property(x => x.LastDetectedAt).HasColumnName("last_detected_at");
            e.Property(x => x.DetectionCount).HasColumnName("detection_count");
            e.Property(x => x.MaxFrpMegawatts).HasColumnName("max_frp_mw");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        model.Entity<IncidentForecast>(e =>
        {
            e.ToTable("incident_forecasts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.IssuedAt).HasColumnName("issued_at");
            e.Property(x => x.WeatherFrom).HasColumnName("weather_from");
            e.Property(x => x.HorizonHours).HasColumnName("horizon_hours");
            e.Property(x => x.MeanTemperatureC).HasColumnName("mean_temperature_c");
            e.Property(x => x.MeanRelativeHumidityPercent).HasColumnName("mean_relative_humidity_pct");
            e.Property(x => x.MeanWindSpeedKmh).HasColumnName("mean_wind_speed_kmh");
            e.Property(x => x.MeanWindFromDegrees).HasColumnName("mean_wind_from_deg");
            e.Property(x => x.MaxWindGustKmh).HasColumnName("max_wind_gust_kmh");
            e.Property(x => x.DroughtFactor).HasColumnName("drought_factor");
            e.Property(x => x.FuelLoadTonnesPerHectare).HasColumnName("fuel_load_t_ha");
            e.Property(x => x.PeakFfdi).HasColumnName("peak_ffdi");
            e.Property(x => x.DangerRating).HasColumnName("danger_rating")
                .HasConversion(new ValueConverter<FireDangerRating, string>(v => ToDbValue(v), v => FromDbValue(v)));
            e.Property(x => x.PeakRateOfSpreadMetersPerHour).HasColumnName("peak_rate_of_spread_m_h");
            e.Property(x => x.HeadDistanceMeters).HasColumnName("head_distance_m");
            e.Property(x => x.LengthToBreadth).HasColumnName("length_to_breadth");
            e.Property(x => x.SpreadBearingDegrees).HasColumnName("spread_bearing_deg");
            e.Property(x => x.SpreadArea).HasColumnName("spread_area").HasColumnType("geometry(Polygon,4326)");
            e.Property(x => x.Model).HasColumnName("model");
        });

        model.Entity<IncidentEvent>(e =>
        {
            e.ToTable("incident_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.Property(x => x.Type).HasColumnName("type").HasConversion(SnakeCase<IncidentEventType>());
            e.Property(x => x.Actor).HasColumnName("actor");
            e.Property(x => x.FromStatus).HasColumnName("from_status").HasConversion(status);
            e.Property(x => x.ToStatus).HasColumnName("to_status").HasConversion(status);
            e.Property(x => x.Note).HasColumnName("note");
            e.Property(x => x.Details).HasColumnName("details").HasColumnType("jsonb");
        });

        model.Entity<Resource>(e =>
        {
            e.ToTable("resources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Callsign).HasColumnName("callsign");
            e.Property(x => x.Kind).HasColumnName("kind").HasConversion(SnakeCase<Core.Resources.ResourceKind>());
            e.Property(x => x.BaseName).HasColumnName("base_name");
            e.Property(x => x.BaseLocation).HasColumnName("base_location").HasColumnType("geometry(Point,4326)");
            e.Property(x => x.Status).HasColumnName("status").HasConversion(SnakeCase<Core.Resources.ResourceStatus>());
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.ReservedUntil).HasColumnName("reserved_until");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        model.Entity<AirspaceReservation>(e =>
        {
            e.ToTable("airspace_reservations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.Level).HasColumnName("level").HasConversion(SnakeCase<Core.Resources.AirspaceLevel>());
            e.Property(x => x.Area).HasColumnName("area").HasColumnType("geometry(Polygon,4326)");
            e.Property(x => x.RadiusMeters).HasColumnName("radius_m");
            e.Property(x => x.During).HasColumnName("during").HasColumnType("tstzrange");
            e.Property(x => x.ReservedBy).HasColumnName("reserved_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        });

        model.Entity<JobRun>(e =>
        {
            e.ToTable("job_runs");
            e.HasKey(x => x.Job);
            e.Property(x => x.Job).HasColumnName("job");
            e.Property(x => x.LastStartedAt).HasColumnName("last_started_at");
            e.Property(x => x.LastStartedBy).HasColumnName("last_started_by");
            e.Property(x => x.LastFinishedAt).HasColumnName("last_finished_at");
            e.Property(x => x.LastSucceededAt).HasColumnName("last_succeeded_at");
        });
    }
}

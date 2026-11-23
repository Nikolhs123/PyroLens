-- Spread forecasts per incident and horizon (+1 h, +3 h), from Open-Meteo weather and the McArthur Mk5 model.

CREATE TABLE incident_forecasts (
    id                          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    incident_id                 bigint NOT NULL REFERENCES incidents (id) ON DELETE CASCADE,
    issued_at                   timestamptz NOT NULL,     -- when PyroLens computed it
    weather_from                timestamptz NOT NULL,     -- first forecast hour used (UTC, top of the hour)
    horizon_hours               smallint NOT NULL CHECK (horizon_hours > 0),

    mean_temperature_c          double precision NOT NULL,
    mean_relative_humidity_pct  double precision NOT NULL CHECK (mean_relative_humidity_pct BETWEEN 0 AND 100),
    mean_wind_speed_kmh         double precision NOT NULL CHECK (mean_wind_speed_kmh >= 0),
    mean_wind_from_deg          double precision NOT NULL CHECK (mean_wind_from_deg >= 0 AND mean_wind_from_deg < 360),
    max_wind_gust_kmh           double precision,

    drought_factor              double precision NOT NULL CHECK (drought_factor BETWEEN 1 AND 10),
    fuel_load_t_ha              double precision NOT NULL CHECK (fuel_load_t_ha > 0),
    peak_ffdi                   double precision NOT NULL CHECK (peak_ffdi >= 0),
    danger_rating               text NOT NULL
                                CHECK (danger_rating IN ('low-moderate', 'high', 'very-high', 'severe', 'extreme', 'catastrophic')),
    peak_rate_of_spread_m_h     double precision NOT NULL CHECK (peak_rate_of_spread_m_h >= 0),
    head_distance_m             double precision NOT NULL CHECK (head_distance_m >= 0),
    length_to_breadth           double precision NOT NULL CHECK (length_to_breadth >= 1),
    spread_bearing_deg          double precision NOT NULL CHECK (spread_bearing_deg >= 0 AND spread_bearing_deg < 360),

    -- Convex hull of the current footprint and the spread ellipse.
    spread_area                 geometry(Polygon, 4326) NOT NULL,
    model                       text NOT NULL,

    -- Re-running within the same forecast hour updates the row instead of adding one.
    CONSTRAINT uq_incident_forecasts_hour UNIQUE (incident_id, weather_from, horizon_hours)
);

CREATE INDEX ix_incident_forecasts_latest ON incident_forecasts (incident_id, weather_from DESC);
CREATE INDEX ix_incident_forecasts_spread_area ON incident_forecasts USING gist (spread_area);

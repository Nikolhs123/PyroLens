-- Phase 3: incident timeline (append-only), a simulated fleet with expiring reservations,
-- and airspace reservations that may not overlap, enforced by an exclusion constraint.

CREATE EXTENSION IF NOT EXISTS btree_gist;

-- ---------- Timeline ----------

CREATE TABLE incident_events (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    incident_id  bigint NOT NULL REFERENCES incidents (id),
    occurred_at  timestamptz NOT NULL DEFAULT now(),
    type         text NOT NULL CHECK (type IN (
                     'created', 'status_changed',
                     'resource_reserved', 'resource_assigned', 'resource_released',
                     'airspace_reserved', 'airspace_cancelled')),
    actor        text NOT NULL,
    from_status  text,
    to_status    text,
    note         text CHECK (length(note) <= 500),
    details      jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE INDEX ix_incident_events_timeline ON incident_events (incident_id, occurred_at, id);

-- An audit log that can be edited is not an audit log: reject UPDATE and DELETE outright.
CREATE FUNCTION reject_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '% is append-only', TG_TABLE_NAME USING ERRCODE = 'restrict_violation';
END
$$;

CREATE TRIGGER incident_events_append_only
    BEFORE UPDATE OR DELETE ON incident_events
    FOR EACH ROW EXECUTE FUNCTION reject_change();

INSERT INTO incident_events (incident_id, occurred_at, type, actor, to_status)
SELECT id, created_at, 'created', 'system', 'detected'
FROM incidents;

-- ---------- Resources (simulated fleet) ----------

CREATE TABLE resources (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    callsign        text NOT NULL UNIQUE,
    kind            text NOT NULL CHECK (kind IN ('engine', 'crew', 'helicopter', 'aircraft', 'drone')),
    base_name       text NOT NULL,
    base_location   geometry(Point, 4326) NOT NULL,
    status          text NOT NULL DEFAULT 'available'
                    CHECK (status IN ('available', 'reserved', 'assigned', 'out_of_service')),
    incident_id     bigint REFERENCES incidents (id),
    reserved_until  timestamptz,
    updated_at      timestamptz NOT NULL DEFAULT now(),

    -- Each status carries exactly the data it needs; anything else is a bug the database refuses to store.
    CONSTRAINT ck_resources_state CHECK (
        (status = 'available'      AND incident_id IS NULL     AND reserved_until IS NULL) OR
        (status = 'reserved'       AND incident_id IS NOT NULL AND reserved_until IS NOT NULL) OR
        (status = 'assigned'       AND incident_id IS NOT NULL AND reserved_until IS NULL) OR
        (status = 'out_of_service' AND incident_id IS NULL     AND reserved_until IS NULL))
);

CREATE INDEX ix_resources_base_location ON resources USING gist ((base_location::geography));
CREATE INDEX ix_resources_incident ON resources (incident_id) WHERE incident_id IS NOT NULL;

-- Simulated: real places for realism, invented units. Not the Hellenic Fire Service's actual fleet.
INSERT INTO resources (callsign, kind, base_name, base_location)
SELECT callsign, kind, base_name, ST_SetSRID(ST_MakePoint(lon, lat), 4326)
FROM (VALUES
    ('AIR-01', 'aircraft',   'Elefsina air base',   23.556, 38.065),
    ('AIR-02', 'aircraft',   'Elefsina air base',   23.556, 38.065),
    ('AIR-03', 'aircraft',   'Elefsina air base',   23.556, 38.065),
    ('AIR-04', 'aircraft',   'Thessaloniki airport', 22.971, 40.520),
    ('AIR-05', 'aircraft',   'Nea Anchialos air base', 22.794, 39.220),
    ('HEL-01', 'helicopter', 'Tatoi air base',      23.784, 38.108),
    ('HEL-02', 'helicopter', 'Tatoi air base',      23.784, 38.108),
    ('HEL-03', 'helicopter', 'Thessaloniki airport', 22.971, 40.520),
    ('HEL-04', 'helicopter', 'Heraklion',           25.180, 35.340),
    ('HEL-05', 'helicopter', 'Rhodes',              28.086, 36.405),
    ('DRN-01', 'drone',      'Athens',              23.730, 37.985),
    ('DRN-02', 'drone',      'Thessaloniki',        22.944, 40.640),
    ('DRN-03', 'drone',      'Ioannina',            20.853, 39.665),
    ('DRN-04', 'drone',      'Alexandroupoli',      25.874, 40.848),
    ('ENG-01', 'engine',     'Athens',              23.730, 37.985),
    ('ENG-02', 'engine',     'Athens',              23.730, 37.985),
    ('ENG-03', 'engine',     'Thessaloniki',        22.944, 40.640),
    ('ENG-04', 'engine',     'Larissa',             22.419, 39.639),
    ('ENG-05', 'engine',     'Patras',              21.735, 38.246),
    ('ENG-06', 'engine',     'Tripoli',             22.373, 37.510),
    ('ENG-07', 'engine',     'Ioannina',            20.853, 39.665),
    ('ENG-08', 'engine',     'Kavala',              24.412, 40.937),
    ('ENG-09', 'engine',     'Alexandroupoli',      25.874, 40.848),
    ('ENG-10', 'engine',     'Lamia',               22.434, 38.900),
    ('ENG-11', 'engine',     'Chalkida',            23.595, 38.463),
    ('ENG-12', 'engine',     'Kalamata',            22.114, 37.039),
    ('ENG-13', 'engine',     'Chania',              24.018, 35.514),
    ('ENG-14', 'engine',     'Rhodes',              28.217, 36.434),
    ('ENG-15', 'engine',     'Mytilene',            26.555, 39.105),
    ('ENG-16', 'engine',     'Kozani',              21.789, 40.300),
    ('CRW-01', 'crew',       'Athens',              23.730, 37.985),
    ('CRW-02', 'crew',       'Athens',              23.730, 37.985),
    ('CRW-03', 'crew',       'Thessaloniki',        22.944, 40.640),
    ('CRW-04', 'crew',       'Larissa',             22.419, 39.639),
    ('CRW-05', 'crew',       'Patras',              21.735, 38.246),
    ('CRW-06', 'crew',       'Alexandroupoli',      25.874, 40.848),
    ('CRW-07', 'crew',       'Chalkida',            23.595, 38.463),
    ('CRW-08', 'crew',       'Kozani',              21.789, 40.300)
) AS fleet (callsign, kind, base_name, lon, lat);

-- ---------- Airspace ----------

CREATE TABLE airspace_reservations (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    incident_id   bigint NOT NULL REFERENCES incidents (id),
    -- low: drones, up to about 120 m above ground; high: helicopters and aircraft.
    level         text NOT NULL CHECK (level IN ('low', 'high')),
    area          geometry(Polygon, 4326) NOT NULL,
    radius_m      double precision NOT NULL CHECK (radius_m BETWEEN 200 AND 50000),
    during        tstzrange NOT NULL CHECK (NOT isempty(during) AND NOT lower_inf(during) AND NOT upper_inf(during)),
    reserved_by   text NOT NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    cancelled_at  timestamptz,

    -- Two live reservations in the same band may not overlap in space and time. The database enforces it, so
    -- concurrent requests cannot both win. && on geometry compares bounding boxes: conservative, never permissive.
    CONSTRAINT ex_airspace_no_overlap EXCLUDE USING gist (
        level WITH =,
        area WITH &&,
        during WITH &&
    ) WHERE (cancelled_at IS NULL)
);

CREATE INDEX ix_airspace_incident ON airspace_reservations (incident_id);

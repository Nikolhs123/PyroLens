# How PyroLens works: a guided tour of the code

This guide explains PyroLens from the ground up: what runs where, what Docker is and how it is used here, and how
each part of the code works, in the order the data flows. It assumes you know some programming but not Docker,
PostGIS or the libraries used. Read it with the code open; every section links to the files it talks about.

**Contents**

1. [The big picture](#1-the-big-picture)
2. [A tour of the repository](#2-a-tour-of-the-repository)
3. [Running it on your computer](#3-running-it-on-your-computer)
4. [Docker from zero](#4-docker-from-zero)
5. [Configuration: settings and secrets](#5-configuration-settings-and-secrets)
6. [How a .NET project is put together](#6-how-a-net-project-is-put-together)
7. [The database](#7-the-database)
8. [Phase 1: from satellite pixels to incidents](#8-phase-1-from-satellite-pixels-to-incidents)
9. [Phase 2: weather and spread forecasts](#9-phase-2-weather-and-spread-forecasts)
10. [Phase 3: lifecycle, units and airspace](#10-phase-3-lifecycle-units-and-airspace)
11. [The API](#11-the-api)
12. [The web app (React + TypeScript)](#12-the-web-app-react--typescript)
13. [Tests](#13-tests)
14. [CI/CD and the live deployment](#14-cicd-and-the-live-deployment)
15. [Exercises: learn by changing things](#15-exercises-learn-by-changing-things)
16. [Glossary](#16-glossary)

---

## 1. The big picture

PyroLens is three programs and a database:

```
            NASA FIRMS (fire pixels)      Open-Meteo (weather)
                     │                            │
                     ▼                            ▼
              ┌─────────────────────────────────────────┐
              │ WORKER  (src/PyroLens.Worker)            │   runs on a schedule:
              │  · every 10 min: fetch fires, store,     │   ingestion and forecasts
              │    group into incidents                  │
              │  · every 30 min: weather → forecasts     │
              └───────────────────┬─────────────────────┘
                                  │ SQL
                                  ▼
              ┌─────────────────────────────────────────┐
              │ DATABASE  PostgreSQL + PostGIS           │   stores everything
              └───────────────────▲─────────────────────┘
                                  │ SQL
              ┌───────────────────┴─────────────────────┐
              │ API  (src/PyroLens.Api)                  │   answers HTTP requests,
              │  · /api/... JSON endpoints               │   and serves the web app
              │  · serves the web app's files at /       │
              └───────────────────▲─────────────────────┘
                                  │ HTTP (fetch every 15–60 s)
              ┌───────────────────┴─────────────────────┐
              │ WEB APP  (web/)  runs in your browser    │   the map and the sidebar
              └─────────────────────────────────────────┘
```

Two ideas are worth holding on to from the start:

- **The worker and the API never talk to each other.** They only share the database. The worker writes, the API
  reads (and writes dispatcher actions). That makes each simpler, and you can restart or scale one without the other.
  (In the live demo the API can also run the worker's jobs itself; they still coordinate only through the database,
  see [section 14](#14-cicd-and-the-live-deployment).)
- **The web app is just files** (HTML, JavaScript, CSS). The API sends them to the browser like any web server, then
  the JavaScript running in the browser calls the API for data.

Where they run:

| | On your computer | Live demo |
|---|---|---|
| Database | Docker container `db` | Supabase (free PostgreSQL) |
| Worker jobs | Docker container `worker`, running all the time | The website itself while someone is visiting, plus GitHub Actions on a schedule (one cycle, then stop) |
| API + web app | Docker container `api` on http://localhost:8080 | Render (free web hosting) |

## 2. A tour of the repository

```
PyroLens/
├── PyroLens.sln                  "Solution": the list of .NET projects, opened by Visual Studio / Rider
├── docker-compose.yml            Runs database + API + worker together on your computer
├── render.yaml                   Tells Render how to build and run the live site
├── .env.example                  Template for local secrets (copy to .env, which git ignores)
├── samples/                      A synthetic FIRMS file, so it works without a NASA key
├── src/
│   ├── PyroLens.Core/            Pure logic: no database, no internet (easy to test)
│   │   ├── Detections/           Reading the FIRMS CSV format
│   │   ├── Forecasting/          Fire danger and spread maths, reading Open-Meteo's JSON
│   │   ├── Incidents/            Incident statuses and which can follow which
│   │   └── Resources/            Unit types, travel times, airspace bands
│   ├── PyroLens.Infrastructure/  Everything that talks to the outside world
│   │   ├── Persistence/          Database connection, tables as C# classes, migrations (SQL files)
│   │   ├── Firms/                Downloading fire data
│   │   ├── Weather/              Downloading weather
│   │   ├── Ingestion/            Storing pixels, grouping them into incidents
│   │   ├── Forecasting/          Making forecasts for incidents
│   │   └── Operations/           Dispatcher actions: status, units, airspace
│   ├── PyroLens.Api/             The web server: endpoints, health checks, serves the web app
│   └── PyroLens.Worker/          The background program: ingestion and forecast loops
├── tests/
│   ├── PyroLens.UnitTests/       Fast tests of Core, no database
│   └── PyroLens.IntegrationTests/ Tests against a real PostGIS started in Docker
├── web/                          The React + TypeScript web app
├── deploy/azure/                 An Azure deployment written as code (prepared, not running)
├── tools/                        Script that built the Greek border data
└── .github/workflows/            CI (build, test, publish images) and the scheduled jobs
```

The split between **Core** and **Infrastructure** is deliberate. Core is "what PyroLens knows" (how to read a CSV
row, how fast a fire spreads); Infrastructure is "how PyroLens reaches things" (PostgreSQL, HTTP). Core never
references Infrastructure, so its logic can be tested in milliseconds.

## 3. Running it on your computer

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download) and [Docker Desktop](https://www.docker.com/products/docker-desktop/).

```bash
docker compose up --build        # build the images and start db, api and worker
```

Then open http://localhost:8080. Useful companions:

```bash
docker compose ps                # what is running
docker compose logs -f worker    # follow the worker's log (Ctrl+C to stop following)
docker compose down              # stop everything (the database data is kept)
docker compose down -v           # stop and delete the database data too
```

Without a NASA key the worker reads [`samples/firms_sample_synthetic.csv`](../samples/firms_sample_synthetic.csv).
With a key, put it in `.env` (see [`.env.example`](../.env.example)) and the worker downloads real fires.

To unlock dispatcher actions locally, use the key `local-operator-key` (set in `docker-compose.yml`, for local use only).

## 4. Docker from zero

### The problem Docker solves

PyroLens needs PostgreSQL with the PostGIS extension, the .NET runtime, and (to build the web app) Node.js.
Installing all of that on every computer, at the right versions, is slow and fragile. Docker packages each program
with everything it needs, so it runs the same everywhere: on your laptop, on GitHub's servers, on Render.

### Four words

- **Image**: a frozen, read-only package: an operating system layer (usually a small Linux), the runtime, and your
  program. Like an installer that is already installed. Example: `postgis/postgis:17-3.5` is PostgreSQL 17 with
  PostGIS 3.5.
- **Container**: a running copy of an image. You can run many containers from one image. When a container is
  deleted, anything it wrote inside itself is gone.
- **Volume**: storage that lives outside containers, so data survives restarts. The database keeps its files in the
  volume `pgdata`.
- **Network**: containers started together by Compose can reach each other by name. The API connects to the host
  `db`, which is the database container's name.

### A Dockerfile, line by line

A Dockerfile is the recipe for an image. Here is the API's, [`src/PyroLens.Api/Dockerfile`](../src/PyroLens.Api/Dockerfile),
explained. It is a **multi-stage build**: the first stages have the heavy build tools, and only the results are
copied into a small final image.

```dockerfile
# ---- Stage 1: build the web app (React + TypeScript) into static files ----
# Start from an image that has Node.js, and call this stage "web".
FROM node:22.12.0-alpine AS web
# Work inside the folder /web (created if needed).
WORKDIR /web
# Copy only the dependency list first, and install it. Docker caches this step
# until package.json or package-lock.json change.
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
# Now copy the rest of the web app, then type-check and bundle it into /web/dist.
COPY web/ ./
RUN npm run build

# ---- Stage 2: compile the API ----
# The .NET SDK image has the compiler and tools.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/PyroLens.Api/PyroLens.Api.csproj -c Release -o /app

# ---- Stage 3: the image that actually runs ----
# Only the ASP.NET runtime: much smaller than the SDK.
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
# Take the compiled API from stage "build" and the built web app from stage "web".
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
# Do not run as root: safer.
USER $APP_UID
# Documents the port the app listens on.
EXPOSE 8080
# The command that runs when a container starts.
ENTRYPOINT ["dotnet", "PyroLens.Api.dll"]
```

(In a Dockerfile a `#` starts a comment only at the beginning of a line, which is why every comment above has a line of its own.)

Two things to notice:

- **Layer caching.** Each line creates a layer. If nothing a line depends on changed, Docker reuses the old layer.
  Copying `package.json` before the rest of the web app means `npm ci` is skipped when you only change code.
- **The final image has no compiler and no Node.js**, only what is needed to run. Smaller images download faster
  and leave less to attack.

The worker's [`Dockerfile`](../src/PyroLens.Worker/Dockerfile) is the same idea with two stages, and it also copies
the sample CSV. [`.dockerignore`](../.dockerignore) lists what never goes into a build (compiled output, `node_modules`,
the `.env` with your secrets).

### docker-compose.yml, line by line

Compose starts several containers that belong together. From [`docker-compose.yml`](../docker-compose.yml):

```yaml
services:
  db:
    image: postgis/postgis:17-3.5          # use a ready-made image, no Dockerfile needed
    environment:                            # settings passed to the container
      POSTGRES_DB: pyrolens                 # the image creates this database on first start
      POSTGRES_USER: pyrolens
      POSTGRES_PASSWORD: pyrolens           # local development only
    ports:
      - "5432:5432"                         # your computer's port 5432 → the container's 5432
    volumes:
      - pgdata:/var/lib/postgresql/data     # keep the data in the named volume
    healthcheck:                            # how Compose knows the database is ready
      test: ["CMD-SHELL", "pg_isready -U pyrolens -d pyrolens"]

  api:
    build:                                  # build an image from a Dockerfile...
      context: .                            # ...using the whole repository as the files it can see
      dockerfile: src/PyroLens.Api/Dockerfile
    environment:
      ConnectionStrings__PyroLens: Host=db;Port=5432;...   # "db" is the database container's name
      Operator__Key: local-operator-key
    ports:
      - "8080:8080"                         # open http://localhost:8080
    depends_on:
      db:
        condition: service_healthy          # start only after the health check passes

  worker:
    build: ...
    environment:
      Firms__Mode: ${FIRMS_MODE:-File}      # from .env if set, otherwise "File"
      Firms__MapKey: ${FIRMS_MAP_KEY:-}

volumes:
  pgdata:                                   # declare the named volume
```

Try these to see it for yourself:

```bash
docker compose exec db psql -U pyrolens -d pyrolens     # open a SQL prompt inside the database container
docker compose exec api printenv ConnectionStrings__PyroLens   # see an environment variable inside a container
docker image ls                                          # the images on your computer
```

### Docker in this project, beyond Compose

- **Building the web app without installing Node.js**: `docker run --rm -v "$PWD/web:/app" -w /app node:22.12.0-alpine npm test`
  starts a throwaway Node container, mounts the `web` folder into it (`-v`), runs the tests and deletes the container (`--rm`).
- **Integration tests** start their own PostGIS container through Testcontainers (section 13).
- **CI** builds both images and publishes them to GitHub's container registry, `ghcr.io` (section 14).
- **Render** builds the API image from the same Dockerfile.

## 5. Configuration: settings and secrets

.NET reads settings from several places, later ones overriding earlier ones:

1. `appsettings.json` (committed: safe defaults), e.g. [`src/PyroLens.Worker/appsettings.json`](../src/PyroLens.Worker/appsettings.json)
2. `appsettings.Development.json` when the environment is Development (local `dotnet run`)
3. **user secrets** (local only, outside the repo): `dotnet user-secrets set "Firms:MapKey" "..." --project src/PyroLens.Worker`
4. **environment variables**: the way Docker, Render and GitHub Actions pass settings

A setting's path uses `:` in JSON and `__` (two underscores) in environment variables, because `:` is not allowed
there. So `"Firms": { "Mode": "Api" }` and `Firms__Mode=Api` are the same setting.

In code, a section is bound to a class, e.g. [`Options.cs`](../src/PyroLens.Infrastructure/Options.cs) has `ClusteringOptions`
with defaults, and [`DependencyInjection.cs`](../src/PyroLens.Infrastructure/DependencyInjection.cs) binds it:

```csharp
services.AddOptions<ClusteringOptions>().Bind(configuration.GetSection(ClusteringOptions.Section)).ValidateDataAnnotations();
```

Classes then ask for `IOptions<ClusteringOptions>` in their constructor and read `.Value`.

**Secrets never go in the repository.** The database password and the NASA key live in `.env` (ignored by git),
GitHub repository secrets, and Render environment variables. A lesson from this project: the configuration binder
*adds* list items from settings to a list that already has default values instead of replacing them, so list
defaults are applied after binding instead (`PostConfigure` in `DependencyInjection.cs`).

## 6. How a .NET project is put together

- A **.csproj** file describes one project: its target (`net9.0`), the NuGet packages it uses, and the projects it
  references. `PyroLens.Api.csproj` references `PyroLens.Infrastructure`, which references `PyroLens.Core`.
- **NuGet** packages are libraries: `Npgsql.EntityFrameworkCore.PostgreSQL` (database), `NetTopologySuite` (geometry),
  `Microsoft.Extensions.Http.Resilience` (retries), `xunit` (tests).
- **Dependency injection (DI)**: instead of a class creating what it needs (`new DbContext(...)`), it lists what it
  needs in its constructor and the framework hands it over. All registrations are in
  [`DependencyInjection.cs`](../src/PyroLens.Infrastructure/DependencyInjection.cs). The lifetimes:
  - `AddSingleton`: one instance for the whole program (e.g. `TimeProvider`)
  - `AddScoped`: one instance per HTTP request or per worker cycle (e.g. the database context)
- **`async`/`await`** everywhere: while waiting for the database or the internet, the thread is free to do other
  work. Every such method takes a `CancellationToken` so it can stop early (a request aborted, the app shutting down).
- **Records** (`public sealed record RawDetection(...)`) are small immutable data classes with value equality.
- **`TimeProvider`** instead of `DateTime.UtcNow`: tests can replace the clock (the operations tests move time forward
  to make a reservation expire).

## 7. The database

### Migrations: how the tables are created

The schema is plain SQL in numbered files in
[`src/PyroLens.Infrastructure/Persistence/Migrations`](../src/PyroLens.Infrastructure/Persistence/Migrations):

| File | Creates |
|---|---|
| `0001_detections_and_incidents.sql` | `fire_detections`, `incidents`, PostGIS |
| `0002_areas_of_interest.sql` | The Greek border (+2 km), cut into pieces, and `fire_detections.area_code` |
| `0003_incident_forecasts.sql` | `incident_forecasts` |
| `0004_operations.sql` | `incident_events` (timeline), `resources` (fleet), `airspace_reservations` |
| `0005_job_runs.sql` | `job_runs`: when each background job last started, finished and succeeded, and who started it |

The files are compiled into the DLL (`<EmbeddedResource>` in the .csproj). On start-up,
[`DatabaseMigrator.cs`](../src/PyroLens.Infrastructure/Persistence/DatabaseMigrator.cs):

1. takes a PostgreSQL **advisory lock** (a named lock), so if the API and the worker start together only one migrates;
2. creates `schema_migrations` if missing and reads which files already ran;
3. runs each new file inside a transaction and records it.

Never edit a migration that already ran somewhere; add a new numbered file instead.

### PostGIS in five minutes

PostGIS adds geometry types and functions to PostgreSQL.

- `geometry(Point, 4326)`: a point in **EPSG:4326**, plain latitude/longitude in degrees (what GPS and satellites use).
- **EPSG:2100** is the *Greek Grid*, a flat projection in metres. `ST_Transform(location, 2100)` converts, so distances
  come out in metres. Used by the clustering.
- `geometry` measures on a flat plane; casting to `geography` (`location::geography`) measures on the curved earth in
  metres. Used for "within 2 km".
- A **GIST index** makes spatial questions ("which points are near this one?") fast instead of scanning every row.

Try in `docker compose exec db psql -U pyrolens -d pyrolens`:

```sql
-- the five incidents with most pixels, with their centre as text
SELECT id, status, detection_count, ST_AsText(centroid) FROM incidents ORDER BY detection_count DESC LIMIT 5;

-- how far each fire engine base is from incident 38, in km
SELECT r.callsign, round(ST_Distance(r.base_location::geography, i.centroid::geography) / 1000) AS km
FROM resources r, incidents i WHERE i.id = 38 AND r.kind = 'engine' ORDER BY km;

-- the timeline of incident 38
SELECT occurred_at, type, actor, from_status, to_status FROM incident_events WHERE incident_id = 38 ORDER BY occurred_at;
```

### Reading with EF Core, writing with SQL

[`PyroLensDbContext.cs`](../src/PyroLens.Infrastructure/Persistence/PyroLensDbContext.cs) maps each table to a C#
class (`FireDetection`, `Incident`, `Resource`, ...). Reads are written in LINQ and EF Core turns them into SQL:

```csharp
db.Incidents.Where(i => i.LastDetectedAt >= since).OrderByDescending(i => i.LastDetectedAt).ToListAsync(ct);
```

Bulk writes and PostGIS-heavy work are written as SQL directly with Npgsql (`NpgsqlCommand`), because there the
SQL *is* the point: batch inserts, clustering, locks, exclusion constraints.

## 8. Phase 1: from satellite pixels to incidents

Follow one cycle of [`IngestionService.RunOnceAsync`](../src/PyroLens.Infrastructure/Ingestion/IngestionService.cs).

**1. Fetch.** [`FirmsSources.cs`](../src/PyroLens.Infrastructure/Firms/FirmsSources.cs) has two implementations of
`IFirmsSource`. `FirmsApiSource` calls
`https://firms.modaps.eosdis.nasa.gov/api/area/csv/{key}/{product}/{west,south,east,north}/{days}` for four products
(VIIRS on three satellites, MODIS). `FirmsFileSource` reads the sample file instead. DI picks one based on `Firms:Mode`.
The HTTP client has automatic retries and timeouts (`AddStandardResilienceHandler`) and its default logging is
switched off, because it would print the URL with your key in it.

**2. Parse.** [`FirmsCsvParser.cs`](../src/PyroLens.Core/Detections/FirmsCsvParser.cs) finds columns by header name,
turns each row into a `RawDetection` record, and skips (and counts) rows with impossible values. If NASA answers
with an error text instead of CSV, it throws.

**3. Store, without duplicates.** [`DetectionWriter.cs`](../src/PyroLens.Infrastructure/Ingestion/DetectionWriter.cs)
sends the whole batch in one statement. Each column goes as one array, and `unnest` turns the arrays back into rows:

```sql
INSERT INTO fire_detections (source, instrument, latitude, longitude, acquired_at, ...)
SELECT @source, u.* FROM unnest(@instrument, @latitude, @longitude, @acquired_at, ...) AS u
ON CONFLICT ON CONSTRAINT uq_fire_detections_natural_key DO NOTHING
RETURNING id
```

The unique constraint on *(source, acquired_at, latitude, longitude)* makes the import **idempotent**: running it
twice changes nothing the second time. That is why every poll can safely ask for the last two days.

**4. Label.** In the same transaction two `UPDATE`s run on the new rows: *likely industrial* (heat at the same spot on
each of the previous three days: a refinery, not a fire) and *area code* (inside the Greek border plus 2 km, checked
against `area_of_interest_parts` with `ST_Intersects`).

**5. Group into incidents.** [`IncidentClusterer.cs`](../src/PyroLens.Infrastructure/Ingestion/IncidentClusterer.cs),
in one transaction behind an advisory lock (so two workers never cluster at once):

- **Candidates**: pixels from the last 24 h, in Greece, confident enough, not industrial, not in an incident.
- **Attach**: a candidate within 2 km of an open incident joins it (`ST_DWithin` on geography, nearest first with `<->`).
- **Cluster** the rest with `ST_ClusterDBSCAN(eps := 1500, minpoints := 1)` in EPSG:2100: pixels connected by chains
  of neighbours closer than 1.5 km form one group. Each group becomes a new incident, with a `created` event.
- **Refresh** the centre (`ST_Centroid`), outline (`ST_ConvexHull`), times, count and maximum fire power of every
  incident that changed.

**6. Repeat.** Locally, [`IngestionWorker`](../src/PyroLens.Worker/IngestionWorker.cs) runs this every 10 minutes
through [`PeriodicWorker`](../src/PyroLens.Worker/PeriodicWorker.cs), which applies migrations first and keeps going
if a cycle fails. In the live demo, [`Program.cs`](../src/PyroLens.Worker/Program.cs) is started with
`--once ingestion`, runs one cycle and exits, and the website runs the same cycle when it is due.

Every one of those goes through [`JobRunner.cs`](../src/PyroLens.Infrastructure/Jobs/JobRunner.cs), which first
*claims* the job in the `job_runs` table:

```sql
INSERT INTO job_runs AS j (job, last_started_at, last_started_by) VALUES ('ingestion', now, 'website')
ON CONFLICT (job) DO UPDATE SET last_started_at = EXCLUDED.last_started_at, last_started_by = EXCLUDED.last_started_by
    WHERE j.last_started_at <= now - gap                    -- long enough since the last run started
      AND (j.last_finished_at >= j.last_started_at          -- and that run has finished
           OR j.last_started_at <= now - interval '15 min') -- or it is clearly dead
```

If a row was inserted or updated, this process won and runs the cycle; if not, someone else ran it recently (or is
running it) and it skips. It is the same idea as the optimistic updates in section 10: one statement, so PostgreSQL
decides the race. When the cycle ends, the runner writes `last_finished_at`, and `last_succeeded_at` if it worked.

## 9. Phase 2: weather and spread forecasts

[`ForecastService.cs`](../src/PyroLens.Infrastructure/Forecasting/ForecastService.cs), every 30 minutes:

1. Deletes forecasts older than 7 days (keeps the free database small).
2. Loads the incidents worth forecasting: fires seen in the last 24 hours, plus any a dispatcher is working on.
3. Asks Open-Meteo for 3 hours of weather at each centre, up to 50 places per request
   ([`OpenMeteoClient.cs`](../src/PyroLens.Infrastructure/Weather/OpenMeteoClient.cs), parsed by
   [`OpenMeteoParser.cs`](../src/PyroLens.Core/Forecasting/OpenMeteoParser.cs)).
4. Computes the forecast in pure C#:
   - [`FireBehaviour.cs`](../src/PyroLens.Core/Forecasting/FireBehaviour.cs): the McArthur Mk5 fire danger index
     `FFDI = 2·exp(−0.450 + 0.987·ln(DF) − 0.0345·RH + 0.0338·T + 0.0234·V)`, its rating band, the spread speed
     `0.0012 × FFDI × fuel load` (km/h), and the ellipse shape from the wind.
   - [`SpreadForecaster.cs`](../src/PyroLens.Core/Forecasting/SpreadForecaster.cs): adds up each hour's run in that
     hour's downwind direction and draws the ellipse as a ring of points.
5. Saves +1 h and +3 h rows with an *upsert* (`INSERT … ON CONFLICT … DO UPDATE`): running again in the same hour
   updates the row instead of adding another. The stored area is the convex hull of the burning area and the ellipse.

## 10. Phase 3: lifecycle, units and airspace

All in [`src/PyroLens.Infrastructure/Operations`](../src/PyroLens.Infrastructure/Operations). The common thread:
**let the database guarantee correctness**, because checks in application code can be raced.

**Lifecycle** ([`IncidentLifecycle.cs`](../src/PyroLens.Core/Incidents/IncidentLifecycle.cs) for the rules,
[`IncidentOperations.cs`](../src/PyroLens.Infrastructure/Operations/IncidentOperations.cs) for the change):

```csharp
var updated = await db.Incidents
    .Where(i => i.Id == id && i.Status == expected)              // only if nobody changed it meanwhile
    .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, to), ct);
if (updated == 0) → 404 if it does not exist, otherwise 409 Conflict with the current status
```

This is **optimistic concurrency**: no lock is held while the dispatcher looks at the screen; the `WHERE` checks at the
last moment that the world is still as they saw it.

**Timeline**: every action adds a row to `incident_events`. A trigger in `0004_operations.sql` raises an error on any
`UPDATE` or `DELETE` of that table, so history cannot be rewritten.

**Units** ([`ResourceOperations.cs`](../src/PyroLens.Infrastructure/Operations/ResourceOperations.cs)):
- *Hold* (reserve) for 5 minutes: `UPDATE resources SET status='reserved' … WHERE id=@id AND (status='available' OR
  (status='reserved' AND reserved_until < now))`. Two dispatchers at once: one row updated, the other gets 409.
- *Send* (assign) only while this incident's hold is live; *release* returns it to available.
- Before reserving, the incident row is locked with `SELECT … FOR SHARE`. Closing an incident updates that row, which
  needs the opposite lock, so a reservation and a close cannot interleave.
- A `CHECK` constraint ties each status to exactly the columns it needs (a reserved unit must have an incident and an
  expiry, an available one neither).
- Suggestions sort free units by travel time ([`ResourceKinds.cs`](../src/PyroLens.Core/Resources/ResourceKinds.cs)).

**Airspace** ([`AirspaceOperations.cs`](../src/PyroLens.Infrastructure/Operations/AirspaceOperations.cs)): the table
has an **exclusion constraint**:

```sql
EXCLUDE USING gist (level WITH =, area WITH &&, during WITH &&) WHERE (cancelled_at IS NULL)
```

Read it as: *no two live rows may have the same level AND overlapping areas AND overlapping time ranges.* PostgreSQL
checks it on every insert, even under heavy concurrency. The code simply tries the insert and, if PostgreSQL answers
with error code `23P01`, finds the reservation in the way and returns a 409 that names it.

**Escalation rules** (which fires need attention) show a different design choice: *compute on read* instead of
storing anything. The rules are a pure function in [`Attention.cs`](../src/PyroLens.Core/Incidents/Attention.cs):

```csharp
AttentionRules.Evaluate(facts, now, settings)   // → a list like [NoUnits since 10:02, Spreading 6 pixels in 6 h]
```

`facts` is one record per incident (pixels in the last 6 h, pixels since it was contained, the last two forecast
ratings, units on it, when a dispatcher took it on, when its last unit was released). One SQL query in
[`AttentionService.cs`](../src/PyroLens.Infrastructure/Operations/AttentionService.cs) gathers it for the whole board;
the interesting part is reading the timeline with `max(...) FILTER (WHERE ...)`:

```sql
SELECT max(e.occurred_at) FILTER (WHERE e.type = 'status_changed' AND e.to_status = 'contained') AS contained_at,
       max(e.occurred_at) FILTER (WHERE e.type = 'resource_released')                            AS last_released_at
FROM incident_events e WHERE e.incident_id = i.id
```

Because nothing is stored, a flag can never get stale: *"no units for 20 minutes"* appears at exactly 20 minutes and
disappears the moment a unit is sent. The cost is one extra query per board load, which is cheap with the indexes on
`incident_id`. Keeping the rules apart from the SQL means they are tested in milliseconds
([`AttentionRulesTests.cs`](../tests/PyroLens.UnitTests/AttentionRulesTests.cs)), and the query separately on real data
([`AttentionTests.cs`](../tests/PyroLens.IntegrationTests/AttentionTests.cs)).

## 11. The API

[`Program.cs`](../src/PyroLens.Api/Program.cs) builds the web server in a few steps: register services, configure
JSON (enums as text, geometries as GeoJSON), add health checks and a rate limiter, then the request pipeline
(error handling → rate limiting → static files → endpoints). Migrations run in a background service
([`DatabaseMigrationService.cs`](../src/PyroLens.Api/DatabaseMigrationService.cs)) that retries, so the API starts
even if the database is briefly unreachable.

Endpoints are **minimal APIs**: a route and a method. In [`Endpoints.cs`](../src/PyroLens.Api/Endpoints.cs):

```csharp
api.MapGet("/incidents/{id:long}/forecast", GetForecast);
```

ASP.NET Core fills the method's parameters from the URL (`id`), the query string, the DI container (`PyroLensDbContext`)
and the request body. Results are typed (`TypedResults.Ok(...)`, `NotFound`, `Problem`).

Dispatcher actions are in [`OperationsEndpoints.cs`](../src/PyroLens.Api/OperationsEndpoints.cs). They run through an
**endpoint filter** that compares the `X-Operator-Key` header with the configured key in constant time (so response
time leaks nothing), and through the rate limiter (30 a minute per client).

Health checks: `/health/live` ("the process is up", used by Render) and `/health/ready` ("and it can reach the
database").

The web app's files are served from `wwwroot` with cache headers: `index.html` is always re-checked, files in
`/assets` (whose names contain a hash of their content) are cached for a year.

## 12. The web app (React + TypeScript)

Built with **Vite** (bundler and dev server), **React** (UI as components), **TypeScript** (JavaScript with types,
checked by `tsc`) and **MapLibre GL** (the map, drawn with WebGL from vector tiles).

| File | Role |
|---|---|
| [`main.tsx`](../web/src/main.tsx) | Entry point: loads fonts and styles, renders `<App />` |
| [`App.tsx`](../web/src/App.tsx) | Holds the state (selected fire, theme, operator key) and polls the API |
| [`api.ts`](../web/src/api.ts) | Types that mirror the API, and functions that call it |
| [`usePolling.ts`](../web/src/usePolling.ts) | A hook: load now, then every N seconds; `reload()` after an action |
| [`components/MapView.tsx`](../web/src/components/MapView.tsx) | The map: sources and layers, clicks, zoom, theme switch |
| [`components/FireList.tsx`](../web/src/components/FireList.tsx) · [`FireDetail.tsx`](../web/src/components/FireDetail.tsx) | The sidebar: board and one fire |
| [`places.ts`](../web/src/places.ts) | Names fires after the nearest town ("6 km south of Feres") |
| [`operations.ts`](../web/src/operations.ts) | Status words, log sentences, the forecast sentence |
| [`theme.ts`](../web/src/theme.ts) · [`styles.css`](../web/src/styles.css) | Light/dark theme: colour variables per theme |

How data flows: `App` calls `usePolling(api.incidents, 60s)`. React re-renders when data arrives; `FireList` shows it;
`MapView` pushes it into a MapLibre GeoJSON source with `setData`. Clicking a fire sets `selectedId`, which starts
polling its forecast and timeline.

How the map works: MapLibre loads a **style** (OpenFreeMap's "liberty" for light, "dark" for dark) that describes how
to draw roads, water and labels from vector tiles. PyroLens adds its own **sources** (data) and **layers** (how to draw
that data: circles for fires, fills and lines for areas). When the theme changes, the whole style is replaced, so
PyroLens adds its layers again on the `style.load` event.

Run it with hot reload (needs Node.js locally, the API running on 8080):

```bash
cd web
npm ci
npm run dev          # http://localhost:5173, /api is forwarded to http://localhost:8080
```

## 13. Tests

| Suite | Tool | Needs | What it covers |
|---|---|---|---|
| `tests/PyroLens.UnitTests` | xUnit | nothing | CSV and JSON parsing, fire maths, lifecycle rules, travel times |
| `tests/PyroLens.IntegrationTests` | xUnit + Testcontainers | Docker | The real SQL: ingestion, clustering, borders, forecasts, concurrency |
| `web/src/*.test.ts` | Vitest | Node.js | Sorting, town names, sentences, countdowns |

**Testcontainers**, in [`PostgisFixture.cs`](../tests/PyroLens.IntegrationTests/PostgisFixture.cs), starts a
`postgis/postgis` container when the tests begin, gives every test its own empty database inside it, and removes the
container at the end. The tests therefore run against exactly the database the app uses, not a fake.

A concurrency test, from [`OperationsTests.cs`](../tests/PyroLens.IntegrationTests/OperationsTests.cs): twenty tasks,
each with its own connection, try to reserve the same aircraft at once, and exactly one may succeed:

```csharp
var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i => { ... ReserveAsync(aircraft, ...) }));
Assert.Equal(1, results.Count(r => r.Outcome == Outcome.Ok));
```

```bash
dotnet test tests/PyroLens.UnitTests
dotnet test tests/PyroLens.IntegrationTests        # Docker Desktop must be running
```

## 14. CI/CD and the live deployment

**CI** ([`.github/workflows/ci.yml`](../.github/workflows/ci.yml)) runs on GitHub's servers on every push:

1. `build-and-test`: restore, build, unit tests, integration tests (GitHub's machines have Docker).
2. `web`: install, type-check, test and build the web app.
3. `docker-images`: build the API and worker images; on `main`, push them to `ghcr.io/nikolhs123/pyrolens-api` and
   `pyrolens-worker`, tagged `latest` and with the commit hash.

**The live demo**, all on free plans:

- **Supabase** runs PostgreSQL with PostGIS. The app connects through Supabase's *session pooler*, using the
  `postgresql://…` address exactly as Supabase shows it ([`ConnectionStrings.cs`](../src/PyroLens.Infrastructure/Persistence/ConnectionStrings.cs) converts it).
- **Render** ([`render.yaml`](../render.yaml)) builds the API Dockerfile on every push to `main` and serves
  https://pyrolens.onrender.com. Settings: `ConnectionStrings__PyroLens`, `Firms__MapKey`, and optionally
  `Operator__Key`.
- **The website keeps itself fresh.** With `LiveRefresh__Enabled=true`,
  [`LiveRefreshService.cs`](../src/PyroLens.Api/LiveRefreshService.cs) wakes up every minute and asks `JobRunner` to
  run ingestion if it last started 10 minutes ago or more, and forecasts after 30. A free Render service sleeps after
  15 minutes without visitors, so in practice this means *fetch new fires when someone opens the site*. The sidebar
  polls `/api/status` every 15 seconds while a job runs, shows *"Checking satellites now…"*, and refetches the fires
  the moment the run finishes.
- **GitHub Actions** ([`scheduled-jobs.yml`](../.github/workflows/scheduled-jobs.yml)) runs the worker image with
  `--once ingestion` every 10 minutes and `--once forecast` at :05 and :35, at least in theory: free schedules run when
  GitHub has spare capacity, which can mean hours apart. It covers the time when nobody is visiting. Secrets
  `DATABASE_URL` and `FIRMS_MAP_KEY` come from the repository's settings.

**Azure (prepared, not running)**: [`deploy/azure/main.bicep`](../deploy/azure/main.bicep) describes the same system
on Azure as code (a database with password-free sign-in through a managed identity, a Container App, two scheduled
jobs). It is not deployed because the database would cost money.

## 15. Exercises: learn by changing things

Each takes 10–30 minutes and touches a different part.

1. **Configuration.** Change `Clustering:ClusterDistanceMeters` to `500` in the worker's `appsettings.json`, run
   `docker compose down -v` and `docker compose up --build`. Do you get more incidents? Why?
2. **SQL.** In `psql`, find the incident with the most satellite pixels and list its detections with
   `SELECT acquired_at, frp_mw FROM fire_detections WHERE incident_id = ... ORDER BY acquired_at`.
3. **A unit test.** In `tests/PyroLens.UnitTests/FireBehaviourTests.cs`, add a case: at 40 °C, 10 % humidity,
   50 km/h wind and drought factor 10, which rating should come out? Work it out with the formula, then test it.
4. **An endpoint.** Add `GET /api/resources/{id}` to `OperationsEndpoints.cs` that returns one unit or 404.
5. **The web app.** In `places.ts`, add your home town to `TOWNS` and check a fire near it gets its name.
6. **See the database protect itself.** In `psql`, try
   `UPDATE incident_events SET actor = 'me';` and read the error.
7. **Docker.** Run `docker compose stop db`, open the site, then `docker compose start db`. What do
   `/health/live` and `/health/ready` say in each state?
8. **A new escalation rule.** Add `Intense` to `AttentionRule`: a fire whose strongest pixel (`MaxFrpMegawatts`) is over
   100 MW. Add the fact to `AttentionFacts`, the column to the query in `AttentionService`, a case in `Evaluate`, a unit
   test, and a sentence in `web/src/attention.ts`. Which of those would the compiler remind you of?

## 16. Glossary

| Term | Meaning |
|---|---|
| Advisory lock | A named lock in PostgreSQL that the application takes and releases itself |
| Container | A running copy of a Docker image |
| DBSCAN | A clustering method: points connected by chains of close neighbours form one group |
| DI (dependency injection) | Classes receive what they need through their constructor instead of creating it |
| EF Core | Entity Framework Core: maps C# classes to tables and LINQ to SQL |
| EPSG:4326 / 2100 | Coordinate systems: latitude/longitude in degrees / the Greek Grid in metres |
| Exclusion constraint | A PostgreSQL rule that no two rows may "overlap" by the operators you choose |
| FFDI | Forest Fire Danger Index (McArthur Mk5) |
| FIRMS | NASA's Fire Information for Resource Management System |
| GeoJSON | A JSON format for points, lines and polygons |
| GIST | A PostgreSQL index type for spatial and range data |
| Idempotent | Doing it twice has the same effect as doing it once |
| Image | A packaged, read-only program plus everything it needs to run (Docker) |
| Migration | A numbered SQL file that changes the database schema, run once |
| Optimistic concurrency | Check at write time that nothing changed, instead of locking while the user decides |
| Upsert | Insert, or update if the row already exists |
| Volume | Docker storage that survives containers being removed |

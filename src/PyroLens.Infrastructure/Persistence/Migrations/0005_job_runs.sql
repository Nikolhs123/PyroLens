-- When each background job last ran, whoever ran it: the worker's loops, the GitHub schedule, or the website itself
-- while someone is looking at it. One row per job.
--
-- A run "claims" its row with one conditional INSERT … ON CONFLICT DO UPDATE (see Jobs/JobRunner.cs), so two processes
-- never run the same job at the same time or straight after each other, and the site can say how fresh its data is.
CREATE TABLE job_runs (
    job                text PRIMARY KEY CHECK (job IN ('ingestion', 'forecast')),
    last_started_at    timestamptz NOT NULL,
    last_started_by    text NOT NULL CHECK (length(last_started_by) BETWEEN 1 AND 40),
    -- Older than last_started_at (or NULL) while a run is going.
    last_finished_at   timestamptz,
    -- Kept when a run fails, so the site still knows when data last arrived.
    last_succeeded_at  timestamptz
);

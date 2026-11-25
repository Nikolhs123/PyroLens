import type { JobStatus, Status } from './api';
import { minutesSince, relativeTime } from './format';

export type DataKind = 'satellites' | 'weather';

/** Data counts as late after this many minutes: well past the normal rhythm (fires every 10 min, weather every 30). */
export const LATE_AFTER_MINUTES: Record<DataKind, number> = { satellites: 30, weather: 60 };

const STARTED_BY: Record<string, string> = {
  website: 'this website, when someone opened it',
  schedule: 'the GitHub schedule',
  worker: 'the worker',
};

export interface Freshness {
  /** "Satellites checked 3 min ago", "Checking satellites now…", "weather 12 min ago (late)". */
  text: string;
  late: boolean;
  busy: boolean;
  /** Longer explanation, for a tooltip. */
  detail: string;
}

/**
 * How fresh one kind of data is, in words. Uses the job's last success when the server reports it,
 * otherwise when data last arrived (older servers).
 */
export function describeFreshness(
  kind: DataKind,
  job: JobStatus | null | undefined,
  fallbackIso: string | null | undefined,
  now: number,
): Freshness {
  const at = job?.lastSucceededAt ?? fallbackIso ?? null;
  const by = job ? (STARTED_BY[job.lastStartedBy] ?? job.lastStartedBy) : null;
  const satellites = kind === 'satellites';

  if (job?.running) {
    return {
      text: satellites ? 'Checking satellites now…' : 'updating weather now…',
      late: false,
      busy: true,
      detail: `Started ${relativeTime(job.lastStartedAt, now)} by ${by}.`,
    };
  }

  if (at == null) {
    return {
      text: satellites ? 'Satellites not checked yet' : 'no weather yet',
      late: true,
      busy: false,
      detail: 'Nothing has run yet.',
    };
  }

  const late = minutesSince(at, now) > LATE_AFTER_MINUTES[kind];
  const lastRun = by ? ` Last run by ${by}.` : '';
  return {
    text: `${satellites ? 'Satellites checked' : 'weather'} ${relativeTime(at, now)}${late ? ' (late)' : ''}`,
    late,
    busy: false,
    detail: (late ? 'Older than expected: the scheduled jobs may be behind.' : 'Up to date.') + lastRun,
  };
}

/** True while a job is running or data is late: worth asking the server again soon. */
export function needsAttention(status: Status | undefined, now: number): boolean {
  if (!status) return false;
  return (
    describeFreshness('satellites', status.ingestion, status.lastDetectionIngestedAt, now).late ||
    describeFreshness('weather', status.forecast, status.lastForecastIssuedAt, now).late ||
    Boolean(status.ingestion?.running || status.forecast?.running)
  );
}

/** Changes whenever new fire or weather data has landed, so the lists can be fetched again. */
export function dataStamp(status: Status): string {
  return [
    status.ingestion?.lastSucceededAt ?? status.lastDetectionIngestedAt,
    status.forecast?.lastSucceededAt ?? status.lastForecastIssuedAt,
  ].join('|');
}

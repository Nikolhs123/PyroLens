import { describe, expect, it } from 'vitest';
import type { JobStatus, Status } from './api';
import { dataStamp, describeFreshness, needsAttention } from './freshness';

const now = Date.parse('2026-11-25T21:00:00Z');

const job = (over: Partial<JobStatus> = {}): JobStatus => ({
  lastSucceededAt: '2026-11-25T20:57:00Z',
  lastStartedAt: '2026-11-25T20:56:50Z',
  lastStartedBy: 'website',
  running: false,
  ...over,
});

const status = (over: Partial<Status> = {}): Status => ({
  lastDetectionIngestedAt: '2026-11-25T13:25:00Z',
  lastForecastIssuedAt: '2026-11-25T13:35:00Z',
  activeIncidents: 52,
  ingestion: job(),
  forecast: job({ lastSucceededAt: '2026-11-25T20:40:00Z' }),
  ...over,
});

describe('describeFreshness', () => {
  it('uses the last successful run, not the last new pixel', () => {
    const f = describeFreshness('satellites', job(), '2026-11-25T13:25:00Z', now);
    expect(f.text).toBe('Satellites checked 3 min ago');
    expect(f.late).toBe(false);
    expect(f.detail).toBe('Up to date. Last run by this website, when someone opened it.');
  });

  it('says when a run is going on', () => {
    const f = describeFreshness('satellites', job({ running: true, lastStartedBy: 'schedule' }), null, now);
    expect(f.text).toBe('Checking satellites now…');
    expect(f.busy).toBe(true);
    expect(f.detail).toBe('Started 3 min ago by the GitHub schedule.');
  });

  it('flags late data', () => {
    const f = describeFreshness('weather', job({ lastSucceededAt: '2026-11-25T19:30:00Z' }), null, now);
    expect(f.text).toBe('weather 2 h ago (late)');
    expect(f.late).toBe(true);
  });

  it('falls back to when data arrived on servers without job runs', () => {
    expect(describeFreshness('satellites', null, '2026-11-25T20:50:00Z', now).text).toBe('Satellites checked 10 min ago');
  });

  it('handles nothing at all', () => {
    const f = describeFreshness('weather', undefined, null, now);
    expect(f.text).toBe('no weather yet');
    expect(f.late).toBe(true);
  });

  it('keeps an unknown starter as it is', () => {
    expect(describeFreshness('satellites', job({ lastStartedBy: 'azure' }), null, now).detail).toContain('Last run by azure.');
  });
});

describe('needsAttention', () => {
  it('is quiet when everything is fresh', () => {
    expect(needsAttention(status(), now)).toBe(false);
  });

  it('wants another look while a job runs or data is late', () => {
    expect(needsAttention(status({ forecast: job({ running: true }) }), now)).toBe(true);
    expect(needsAttention(status({ ingestion: job({ lastSucceededAt: '2026-11-25T20:00:00Z' }) }), now)).toBe(true);
  });

  it('is quiet before the first answer', () => {
    expect(needsAttention(undefined, now)).toBe(false);
  });
});

describe('dataStamp', () => {
  it('changes only when new data lands', () => {
    const before = dataStamp(status());
    expect(dataStamp(status({ ingestion: job({ running: true }) }))).toBe(before);
    expect(dataStamp(status({ ingestion: job({ lastSucceededAt: '2026-11-25T21:07:00Z' }) }))).not.toBe(before);
  });
});

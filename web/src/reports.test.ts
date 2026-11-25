import { describe, expect, it } from 'vitest';
import type { Report } from './api';
import { describeReport, nearestFireText, reportPlace, sortReports } from './reports';

const now = Date.parse('2026-11-25T21:00:00Z');

const report = (over: Partial<Report> = {}): Report => ({
  id: 1,
  latitude: 40.842,
  longitude: 26.174,
  kind: 'smoke',
  note: null,
  submittedAt: '2026-11-25T20:48:00Z',
  status: 'new',
  reviewedBy: null,
  reviewedAt: null,
  nearestIncidentId: null,
  nearestIncidentMeters: null,
  ...over,
});

describe('reports in words', () => {
  it('says what was seen and when', () => {
    expect(describeReport(report(), now)).toBe('Smoke reported 12 min ago');
    expect(describeReport(report({ kind: 'other' }), now)).toBe('Something reported 12 min ago');
  });

  it('names the place like the fires', () => {
    expect(reportPlace(report())).toBe('6 km south of Feres');
  });

  it('relates it to the nearest fire', () => {
    expect(nearestFireText(report({ nearestIncidentId: 38, nearestIncidentMeters: 1240 }))).toBe('1.2 km from fire #38');
    expect(nearestFireText(report())).toBe('No fire on the board within 3 km');
  });

  it('lists new reports first, newest first', () => {
    const sorted = sortReports([
      report({ id: 1, status: 'seen', submittedAt: '2026-11-25T20:59:00Z' }),
      report({ id: 2, submittedAt: '2026-11-25T20:00:00Z' }),
      report({ id: 3, submittedAt: '2026-11-25T20:30:00Z' }),
    ]);
    expect(sorted.map((r) => r.id)).toEqual([3, 2, 1]);
  });
});

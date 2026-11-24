import { describe, expect, it } from 'vitest';
import type { TimelineEvent } from './api';
import { STATUS, countdown, describeEvent } from './operations';

const event = (partial: Partial<TimelineEvent>): TimelineEvent => ({
  id: 1,
  occurredAt: '2026-11-24T23:07:18Z',
  type: 'created',
  actor: 'system',
  fromStatus: null,
  toStatus: null,
  note: null,
  details: {},
  ...partial,
});

describe('countdown', () => {
  const now = Date.parse('2026-11-24T23:07:00Z');

  it.each([
    ['2026-11-24T23:11:32Z', '4:32'],
    ['2026-11-24T23:07:05Z', '0:05'],
    ['2026-11-24T23:07:00Z', 'expired'],
    ['2026-11-24T23:00:00Z', 'expired'],
  ])('until %s shows %s', (until, text) => {
    expect(countdown(until, now)).toBe(text);
  });
});

describe('describeEvent', () => {
  it('reads like a log line', () => {
    expect(describeEvent(event({ type: 'statusChanged', fromStatus: 'detected', toStatus: 'confirmed' }))).toBe('DET → CNF');
    expect(describeEvent(event({ type: 'resourceReserved', details: { callsign: 'DRN-04' } }))).toBe('Unit reserved DRN-04');
    expect(describeEvent(event({ type: 'airspaceReserved', details: { level: 'high', radiusMeters: 3000 } }))).toBe(
      'Airspace reserved: HIGH 3 km',
    );
    expect(describeEvent(event({ type: 'created' }))).toBe('Incident opened from satellite detections');
  });
});

describe('STATUS', () => {
  it('gives every status a distinct three-letter code', () => {
    const codes = Object.values(STATUS).map((s) => s.code);
    expect(new Set(codes).size).toBe(codes.length);
    expect(codes.every((c) => c.length === 3)).toBe(true);
  });
});

import { describe, expect, it } from 'vitest';
import type { Forecast, TimelineEvent } from './api';
import { STATUS, countdown, describeEvent, forecastSentence } from './operations';

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
  it('reads like a sentence a person would write in a log', () => {
    expect(describeEvent(event({ type: 'created' }))).toBe('Satellites detected the fire.');
    expect(describeEvent(event({ type: 'statusChanged', actor: 'Nick', fromStatus: 'detected', toStatus: 'confirmed' }))).toBe(
      'Nick confirmed the fire.',
    );
    expect(describeEvent(event({ type: 'statusChanged', actor: 'Nick', fromStatus: 'contained', toStatus: 'active' }))).toBe(
      'Nick reported a flare-up.',
    );
    expect(describeEvent(event({ type: 'statusChanged', actor: 'Nick', fromStatus: 'detected', toStatus: 'closed' }))).toBe(
      'Nick closed it as a false alarm.',
    );
    expect(describeEvent(event({ type: 'resourceAssigned', actor: 'Nick', details: { callsign: 'CRW-06' } }))).toBe('Nick sent CRW-06.');
    expect(describeEvent(event({ type: 'airspaceReserved', actor: 'Nick', details: { level: 'high', radiusMeters: 3000 } }))).toBe(
      'Nick reserved high (aircraft) airspace, 3 km around the fire.',
    );
  });
});

describe('forecastSentence', () => {
  it('turns the forecast into one plain sentence', () => {
    const weather = { meanTemperatureC: 12, meanRelativeHumidityPercent: 62, meanWindSpeedKmh: 13.4, meanWindFromDegrees: 85, maxWindGustKmh: 24 };
    const horizon = (hours: number, head: number) => ({
      horizonHours: hours,
      weather,
      peakFfdi: 3,
      dangerRating: 'lowModerate' as const,
      peakRateOfSpreadMetersPerHour: 40,
      headDistanceMeters: head,
      lengthToBreadth: 1.8,
      spreadBearingDegrees: 262,
      spreadArea: { type: 'Polygon' as const, coordinates: [] },
    });
    const forecast = { horizons: [horizon(1, 35), horizon(3, 120)] } as unknown as Forecast;

    expect(forecastSentence(forecast)).toBe(
      'Low fire danger. Wind 13 km/h from the east could carry it about 120 m west within 3 hours.',
    );
    expect(forecastSentence(null)).toBe('No weather forecast yet.');
  });
});

describe('STATUS', () => {
  it('ranks fires being fought first', () => {
    const order = Object.entries(STATUS).sort((a, b) => b[1].rank - a[1].rank).map(([s]) => s);
    expect(order).toEqual(['active', 'confirmed', 'contained', 'detected', 'closed']);
  });
});

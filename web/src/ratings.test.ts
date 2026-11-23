import { describe, expect, it } from 'vitest';
import type { DangerRating, Incident } from './api';
import { NO_FORECAST, RATINGS, countByRating, ratingStyle, sortIncidents } from './ratings';

const incident = (id: number, rating: DangerRating | null, pixels = 1, lastSeen = '2026-11-23T10:00:00Z'): Incident => ({
  id,
  status: 'detected',
  latitude: 38,
  longitude: 23,
  firstDetectedAt: lastSeen,
  lastDetectedAt: lastSeen,
  detectionCount: pixels,
  maxFrpMegawatts: null,
  footprint: null,
  forecast: rating
    ? { weatherFrom: lastSeen, dangerRating: rating, peakFfdi: 10, headDistanceMeters: 100, spreadBearingDegrees: 0 }
    : null,
});

describe('ratingStyle', () => {
  it('uses the forecast rating, or a neutral style without a forecast', () => {
    expect(ratingStyle(incident(1, 'severe'))).toBe(RATINGS.severe);
    expect(ratingStyle(incident(2, null))).toBe(NO_FORECAST);
  });
});

describe('sortIncidents', () => {
  it('puts the most dangerous first, then larger fires, then the most recent', () => {
    const sorted = sortIncidents([
      incident(1, 'lowModerate', 9),
      incident(2, null, 20),
      incident(3, 'extreme', 1),
      incident(4, 'high', 2, '2026-11-23T09:00:00Z'),
      incident(5, 'high', 2, '2026-11-23T11:00:00Z'),
      incident(6, 'high', 5),
    ]);

    expect(sorted.map((i) => i.id)).toEqual([3, 6, 5, 4, 1, 2]);
  });

  it('does not change the input array', () => {
    const input = [incident(1, 'high'), incident(2, 'severe')];
    sortIncidents(input);
    expect(input.map((i) => i.id)).toEqual([1, 2]);
  });
});

describe('countByRating', () => {
  it('counts per band, most dangerous first, skipping empty bands', () => {
    const counts = countByRating([incident(1, 'high'), incident(2, 'high'), incident(3, 'severe'), incident(4, null)]);

    expect(counts.map((c) => [c.style.label, c.count])).toEqual([
      ['Severe', 1],
      ['High', 2],
      ['No forecast yet', 1],
    ]);
  });
});

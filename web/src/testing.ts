import type { DangerRating, Incident } from './api';

/** An incident for tests; only the fields the UI logic reads matter. */
export const incident = (
  id: number,
  rating: DangerRating | null,
  pixels = 1,
  lastSeen = '2026-11-23T10:00:00Z',
  frp: number | null = null,
): Incident => ({
  id,
  status: 'detected',
  latitude: 38,
  longitude: 23,
  firstDetectedAt: lastSeen,
  lastDetectedAt: lastSeen,
  detectionCount: pixels,
  maxFrpMegawatts: frp,
  footprint: null,
  forecast: rating
    ? { weatherFrom: lastSeen, dangerRating: rating, peakFfdi: 10, headDistanceMeters: 100, spreadBearingDegrees: 0 }
    : null,
});

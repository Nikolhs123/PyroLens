import type { Geometry, Polygon } from 'geojson';

/** Mirrors the API DTOs in src/PyroLens.Api/Endpoints.cs. */
export type DangerRating = 'lowModerate' | 'high' | 'veryHigh' | 'severe' | 'extreme' | 'catastrophic';

export interface ForecastSummary {
  weatherFrom: string;
  dangerRating: DangerRating;
  peakFfdi: number;
  headDistanceMeters: number;
  spreadBearingDegrees: number;
}

export interface Incident {
  id: number;
  status: string;
  latitude: number;
  longitude: number;
  firstDetectedAt: string;
  lastDetectedAt: string;
  detectionCount: number;
  maxFrpMegawatts: number | null;
  footprint: Geometry | null;
  forecast: ForecastSummary | null;
}

export interface Detection {
  id: number;
  source: string;
  instrument: 'modis' | 'viirs';
  latitude: number;
  longitude: number;
  acquiredAt: string;
  confidence: string;
  frpMegawatts: number | null;
  likelyIndustrial: boolean;
  areaCode: string | null;
  incidentId: number | null;
}

export interface Weather {
  meanTemperatureC: number;
  meanRelativeHumidityPercent: number;
  meanWindSpeedKmh: number;
  meanWindFromDegrees: number;
  maxWindGustKmh: number | null;
}

export interface Horizon {
  horizonHours: number;
  weather: Weather;
  peakFfdi: number;
  dangerRating: DangerRating;
  peakRateOfSpreadMetersPerHour: number;
  headDistanceMeters: number;
  lengthToBreadth: number;
  spreadBearingDegrees: number;
  spreadArea: Polygon;
}

export interface Forecast {
  incidentId: number;
  issuedAt: string;
  weatherFrom: string;
  droughtFactor: number;
  fuelLoadTonnesPerHectare: number;
  model: string;
  disclaimer: string;
  attribution: string;
  horizons: Horizon[];
}

export interface Status {
  lastDetectionIngestedAt: string | null;
  lastForecastIssuedAt: string | null;
  activeIncidents: number;
}

/** GET a JSON resource; null when the API answers 404 (e.g. no forecast yet). */
async function getJson<T>(url: string, signal?: AbortSignal): Promise<T | null> {
  const response = await fetch(url, { signal, headers: { Accept: 'application/json' } });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${url} answered ${response.status}`);
  return (await response.json()) as T;
}

export const api = {
  status: (signal?: AbortSignal) => getJson<Status>('/api/status', signal),
  incidents: (signal?: AbortSignal) => getJson<Incident[]>('/api/incidents?hours=48', signal).then((r) => r ?? []),
  detections: (signal?: AbortSignal) => getJson<Detection[]>('/api/detections?hours=24', signal).then((r) => r ?? []),
  forecast: (id: number, signal?: AbortSignal) => getJson<Forecast>(`/api/incidents/${id}/forecast`, signal),
};

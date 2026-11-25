import type { Geometry, Polygon } from 'geojson';

/** Mirrors the API DTOs in src/PyroLens.Api (Endpoints.cs, OperationsEndpoints.cs). */
export type DangerRating = 'lowModerate' | 'high' | 'veryHigh' | 'severe' | 'extreme' | 'catastrophic';
export type IncidentStatus = 'detected' | 'confirmed' | 'active' | 'contained' | 'closed';
export type ResourceKind = 'engine' | 'crew' | 'helicopter' | 'aircraft' | 'drone';
export type ResourceStatus = 'available' | 'reserved' | 'assigned' | 'outOfService';
export type AirspaceLevel = 'low' | 'high';
export type EventType =
  | 'created'
  | 'statusChanged'
  | 'resourceReserved'
  | 'resourceAssigned'
  | 'resourceReleased'
  | 'airspaceReserved'
  | 'airspaceCancelled';

export interface ForecastSummary {
  weatherFrom: string;
  dangerRating: DangerRating;
  peakFfdi: number;
  headDistanceMeters: number;
  spreadBearingDegrees: number;
}

export interface Incident {
  id: number;
  status: IncidentStatus;
  latitude: number;
  longitude: number;
  firstDetectedAt: string;
  lastDetectedAt: string;
  detectionCount: number;
  maxFrpMegawatts: number | null;
  footprint: Geometry | null;
  forecast: ForecastSummary | null;
  /** Allowed next statuses, from the server's lifecycle rules. */
  nextStatuses: IncidentStatus[];
  acceptsResources: boolean;
  /** Why it needs attention right now; empty when it does not (missing on older servers). */
  attention?: Attention[];
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

export type AttentionRule = 'noUnits' | 'spreading' | 'burningAgain' | 'danger' | 'reported';
export type AttentionLevel = 'watch' | 'urgent';

/** One reason a fire needs a dispatcher's attention (escalation rules). Only the fields its rule uses are set. */
export interface Attention {
  rule: AttentionRule;
  level: AttentionLevel;
  /** noUnits: since when no unit has been on it. burningAgain: when it was marked contained. */
  since: string | null;
  pixels: number | null;
  withinHours: number | null;
  from: DangerRating | null;
  to: DangerRating | null;
  /** reported: how many reports from the public. */
  reports?: number | null;
}

/** Who starts a background job: the worker container, the GitHub schedule, or the website itself on a visit. */
export type JobStarter = 'worker' | 'schedule' | 'website';

export interface JobStatus {
  /** When it last finished without failing: how fresh the data is. */
  lastSucceededAt: string | null;
  lastStartedAt: string;
  lastStartedBy: JobStarter | string;
  running: boolean;
}

export interface Status {
  lastDetectionIngestedAt: string | null;
  lastForecastIssuedAt: string | null;
  activeIncidents: number;
  /** Fire data job; null until it has run once (or on an older server). */
  ingestion: JobStatus | null;
  /** Weather and forecast job. */
  forecast: JobStatus | null;
}

export interface Resource {
  id: number;
  callsign: string;
  kind: ResourceKind;
  baseName: string;
  latitude: number;
  longitude: number;
  status: ResourceStatus;
  incidentId: number | null;
  reservedUntil: string | null;
}

export interface Suggestion {
  resource: Resource;
  distanceMeters: number;
  etaMinutes: number;
}

export interface TimelineEvent {
  id: number;
  occurredAt: string;
  type: EventType;
  actor: string;
  fromStatus: IncidentStatus | null;
  toStatus: IncidentStatus | null;
  note: string | null;
  details: Record<string, unknown>;
}

export interface AirspaceReservation {
  id: number;
  incidentId: number;
  level: AirspaceLevel;
  radiusMeters: number;
  startsAt: string;
  endsAt: string;
  reservedBy: string;
  area: Polygon;
}

// ---------- Reports from the public ----------

export type ReportKind = 'smoke' | 'flames' | 'other';
export type ReportStatus = 'new' | 'seen' | 'dismissed';

export interface Report {
  id: number;
  latitude: number;
  longitude: number;
  kind: ReportKind;
  /** Only sent to dispatchers with the operator key. */
  note: string | null;
  submittedAt: string;
  status: ReportStatus;
  reviewedBy: string | null;
  reviewedAt: string | null;
  /** The open fire nearest to the report, if one is within 3 km. */
  nearestIncidentId: number | null;
  nearestIncidentMeters: number | null;
}

/** A fire as the public map shows it: no units, airspace or forecasts. */
export interface PublicFire {
  id: number;
  status: IncidentStatus;
  latitude: number;
  longitude: number;
  dangerRating: DangerRating | null;
  lastSeenAt: string;
}

export interface ReportReceipt {
  id: number | null;
  message: string;
}

/** GET a JSON resource; null when the API answers 404 (e.g. no forecast yet). */
async function getJson<T>(url: string, signal?: AbortSignal, operatorKey?: string | null): Promise<T | null> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (operatorKey) headers['X-Operator-Key'] = operatorKey;
  const response = await fetch(url, { signal, headers });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${url} answered ${response.status}`);
  return (await response.json()) as T;
}

export const api = {
  status: (signal?: AbortSignal) => getJson<Status>('/api/status', signal),
  incidents: (signal?: AbortSignal) => getJson<Incident[]>('/api/incidents?hours=48', signal).then((r) => r ?? []),
  detections: (signal?: AbortSignal) => getJson<Detection[]>('/api/detections?hours=24', signal).then((r) => r ?? []),
  forecast: (id: number, signal?: AbortSignal) => getJson<Forecast>(`/api/incidents/${id}/forecast`, signal),
  timeline: (id: number, signal?: AbortSignal) => getJson<TimelineEvent[]>(`/api/incidents/${id}/events`, signal).then((r) => r ?? []),
  resources: (signal?: AbortSignal) => getJson<Resource[]>('/api/resources', signal).then((r) => r ?? []),
  suggestions: (id: number, signal?: AbortSignal) =>
    getJson<Suggestion[]>(`/api/incidents/${id}/resources/suggestions?limit=6`, signal).then((r) => r ?? []),
  airspace: (signal?: AbortSignal) => getJson<AirspaceReservation[]>('/api/airspace', signal).then((r) => r ?? []),
  /** Reports of the last 24 h, not dismissed. Notes are included only with the operator key. */
  reports: (operatorKey: string | null, signal?: AbortSignal) =>
    getJson<Report[]>('/api/reports?hours=24', signal, operatorKey).then((r) => r ?? []),
  publicFires: (signal?: AbortSignal) => getJson<PublicFire[]>('/api/public/fires', signal).then((r) => r ?? []),
};

// ---------- Dispatcher actions ----------

export type ActionResult<T> = { ok: true; value: T } | { ok: false; status: number; message: string };

/** Sends a dispatcher action with the operator key; turns problem responses into a readable message. */
async function send<T>(method: 'GET' | 'POST' | 'DELETE', url: string, key: string | null, body?: unknown): Promise<ActionResult<T>> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'application/json' };
  if (key) headers['X-Operator-Key'] = key;
  let response: Response;
  try {
    response = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  } catch {
    return { ok: false, status: 0, message: 'The server cannot be reached. Try again in a moment.' };
  }

  if (response.ok) {
    const text = await response.text();
    return { ok: true, value: (text ? JSON.parse(text) : undefined) as T };
  }

  let message = `The server answered ${response.status}.`;
  try {
    const problem = (await response.json()) as { title?: string; detail?: string };
    message = problem.detail ?? problem.title ?? message;
  } catch {
    if (response.status === 429) message = 'Too many actions in a minute. Wait a moment.';
  }
  return { ok: false, status: response.status, message };
}

export const actions = {
  /** 204 when the key is right; 401 wrong key; 403 when the server has no key (read-only). */
  checkKey: (key: string) => send<void>('GET', '/api/operator', key),
  changeStatus: (key: string, id: number, from: IncidentStatus, to: IncidentStatus, note?: string) =>
    send<{ id: number; status: IncidentStatus }>('POST', `/api/incidents/${id}/status`, key, { from, to, note }),
  reserve: (key: string, resourceId: number, incidentId: number) =>
    send<Resource>('POST', `/api/resources/${resourceId}/reserve`, key, { incidentId }),
  assign: (key: string, resourceId: number, incidentId: number) =>
    send<Resource>('POST', `/api/resources/${resourceId}/assign`, key, { incidentId }),
  release: (key: string, resourceId: number, incidentId: number) =>
    send<Resource>('POST', `/api/resources/${resourceId}/release`, key, { incidentId }),
  reserveAirspace: (key: string, incidentId: number, level: AirspaceLevel, radiusMeters: number, durationMinutes: number) =>
    send<AirspaceReservation>('POST', `/api/incidents/${incidentId}/airspace`, key, { level, radiusMeters, durationMinutes }),
  cancelAirspace: (key: string, reservationId: number) =>
    send<AirspaceReservation>('DELETE', `/api/airspace/${reservationId}`, key),
  reviewReport: (key: string, reportId: number, status: 'seen' | 'dismissed') =>
    send<Report>('POST', `/api/reports/${reportId}/review`, key, { status }),
};

/** Anyone can send a report; `website` is the hidden field that only bots fill in. */
export const submitReport = (report: { latitude: number; longitude: number; kind: ReportKind; note?: string; website?: string }) =>
  send<ReportReceipt>('POST', '/api/public/reports', null, report);

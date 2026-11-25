import type { EventType, Forecast, IncidentStatus, ResourceKind, TimelineEvent } from './api';
import { formatDistance } from './format';
import { directionWord } from './places';
import { RATINGS } from './ratings';

/** Words and colours per incident status. */
export const STATUS: Record<IncidentStatus, { label: string; group: string; color: string; rank: number }> = {
  active: { label: 'Active', group: 'Being fought', color: '#d9480f', rank: 4 },
  confirmed: { label: 'Confirmed', group: 'Confirmed, waiting for units', color: '#b7791f', rank: 3 },
  contained: { label: 'Contained', group: 'Contained', color: '#2b6cb0', rank: 2 },
  detected: { label: 'New detection', group: 'New detections', color: '#718096', rank: 1 },
  closed: { label: 'Closed', group: 'Closed', color: '#718096', rank: 0 },
};

/** Text on the button that moves an incident to this status. */
export const ACTION_LABEL: Record<IncidentStatus, string> = {
  detected: 'Detected',
  confirmed: 'Confirm fire',
  active: 'Mark active',
  contained: 'Mark contained',
  closed: 'Close',
};

export const KIND: Record<ResourceKind, { label: string }> = {
  aircraft: { label: 'Water bomber' },
  helicopter: { label: 'Helicopter' },
  drone: { label: 'Drone' },
  engine: { label: 'Fire engine' },
  crew: { label: 'Ground crew' },
};

/** "4:32" until a reservation runs out, or "expired". */
export function countdown(untilIso: string, now: number = Date.now()): string {
  const seconds = Math.floor((Date.parse(untilIso) - now) / 1000);
  if (seconds <= 0) return 'expired';
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

/**
 * One plain sentence about where the fire could go: "Low fire danger. Wind 13 km/h from the east could carry
 * it about 120 m west within 3 hours."
 */
export function forecastSentence(forecast: Forecast | null | undefined): string {
  const threeHours = forecast?.horizons.find((h) => h.horizonHours === 3);
  const oneHour = forecast?.horizons.find((h) => h.horizonHours === 1);
  if (!threeHours || !oneHour) return 'No weather forecast yet.';

  const danger = RATINGS[threeHours.dangerRating].short;
  const wind = oneHour.weather;
  return (
    `${danger} fire danger. Wind ${Math.round(wind.meanWindSpeedKmh)} km/h from the ${directionWord(wind.meanWindFromDegrees)} ` +
    `could carry it about ${formatDistance(threeHours.headDistanceMeters)} ${directionWord(threeHours.spreadBearingDegrees)} within 3 hours.`
  );
}

/** What a status change means, in words. */
function statusPhrase(from: IncidentStatus | null, to: IncidentStatus | null): string {
  if (to === 'confirmed') return 'confirmed the fire';
  if (to === 'active' && from === 'contained') return 'reported a flare-up';
  if (to === 'active') return 'marked the fire active';
  if (to === 'contained') return 'marked the fire contained';
  if (to === 'closed' && from === 'detected') return 'closed it as a false alarm';
  if (to === 'closed') return 'closed the incident';
  return `changed the status to ${to}`;
}

/** One sentence per timeline event: "Nick sent CRW-06." */
export function describeEvent(e: TimelineEvent): string {
  const d = e.details as Record<string, string | number | undefined>;
  const who = e.actor === 'system' ? 'PyroLens' : e.actor;
  const sentences: Record<EventType, () => string> = {
    created: () => 'Satellites detected the fire.',
    statusChanged: () => `${who} ${statusPhrase(e.fromStatus, e.toStatus)}.`,
    resourceReserved: () => `${who} put ${d.callsign} on hold.`,
    resourceAssigned: () => `${who} sent ${d.callsign}.`,
    resourceReleased: () => `${who} released ${d.callsign}.`,
    airspaceReserved: () =>
      `${who} reserved ${d.level === 'low' ? 'low (drone)' : 'high (aircraft)'} airspace, ${Number(d.radiusMeters ?? 0) / 1000} km around the fire.`,
    airspaceCancelled: () => `${who} cancelled an airspace reservation.`,
  };
  return sentences[e.type]();
}

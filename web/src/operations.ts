import type { EventType, IncidentStatus, ResourceKind, TimelineEvent } from './api';

/** Board codes and colours per incident status. */
export const STATUS: Record<IncidentStatus, { code: string; label: string; color: string }> = {
  detected: { code: 'DET', label: 'Detected', color: '#84949b' },
  confirmed: { code: 'CNF', label: 'Confirmed', color: '#ffb000' },
  active: { code: 'ACT', label: 'Active', color: '#ea4f2f' },
  contained: { code: 'CTN', label: 'Contained', color: '#5aa9e6' },
  closed: { code: 'CLS', label: 'Closed', color: '#58676e' },
};

/** Verb on the button that moves an incident to this status. */
export const ACTION_LABEL: Record<IncidentStatus, string> = {
  detected: 'Detected',
  confirmed: 'Confirm',
  active: 'Set active',
  contained: 'Contained',
  closed: 'Close',
};

export const KIND: Record<ResourceKind, { code: string; label: string }> = {
  aircraft: { code: 'AIR', label: 'Water bomber' },
  helicopter: { code: 'HEL', label: 'Helicopter' },
  drone: { code: 'DRN', label: 'Drone' },
  engine: { code: 'ENG', label: 'Fire engine' },
  crew: { code: 'CRW', label: 'Ground crew' },
};

/** "4:32" until a reservation runs out, or "expired". */
export function countdown(untilIso: string, now: number = Date.now()): string {
  const seconds = Math.floor((Date.parse(untilIso) - now) / 1000);
  if (seconds <= 0) return 'expired';
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

const EVENT_LABEL: Record<EventType, string> = {
  created: 'Incident opened from satellite detections',
  statusChanged: 'Status changed',
  resourceReserved: 'Unit reserved',
  resourceAssigned: 'Unit assigned',
  resourceReleased: 'Unit released',
  airspaceReserved: 'Airspace reserved',
  airspaceCancelled: 'Airspace cancelled',
};

/** One readable line per timeline event. */
export function describeEvent(e: TimelineEvent): string {
  const d = e.details as Record<string, string | number | undefined>;
  switch (e.type) {
    case 'statusChanged':
      return `${STATUS[e.fromStatus!].code} → ${STATUS[e.toStatus!].code}`;
    case 'resourceReserved':
    case 'resourceAssigned':
    case 'resourceReleased':
      return `${EVENT_LABEL[e.type]} ${d.callsign ?? ''}`.trim();
    case 'airspaceReserved':
      return `${EVENT_LABEL[e.type]}: ${String(d.level ?? '').toUpperCase()} ${Number(d.radiusMeters ?? 0) / 1000} km`;
    case 'airspaceCancelled':
      return `${EVENT_LABEL[e.type]} #${d.reservationId ?? ''}`;
    default:
      return EVENT_LABEL[e.type];
  }
}

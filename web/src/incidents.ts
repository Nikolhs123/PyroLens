import type { Incident } from './api';
import { ratingStyle } from './ratings';

export type SortKey = 'id' | 'rating' | 'pixels' | 'frp' | 'seen';

/** Direction a column starts in when first clicked: the most useful end first. */
export const DEFAULT_DESCENDING: Record<SortKey, boolean> = {
  id: false,
  rating: true,
  pixels: true,
  frp: true,
  seen: true, // most recently seen first
};

const valueOf = (incident: Incident, key: SortKey): number => {
  switch (key) {
    case 'id':
      return incident.id;
    case 'rating':
      return ratingStyle(incident).rank;
    case 'pixels':
      return incident.detectionCount;
    case 'frp':
      return incident.maxFrpMegawatts ?? -1;
    case 'seen':
      return Date.parse(incident.lastDetectedAt);
  }
};

/**
 * Sorts by one column; ties fall back to danger, then size, then recency, then id,
 * so the order is stable and the most urgent rows rise within equal values.
 */
export function sortIncidents(incidents: Incident[], key: SortKey = 'rating', descending = DEFAULT_DESCENDING[key]): Incident[] {
  const sign = descending ? -1 : 1;
  return [...incidents].sort(
    (a, b) =>
      sign * (valueOf(a, key) - valueOf(b, key)) ||
      ratingStyle(b).rank - ratingStyle(a).rank ||
      b.detectionCount - a.detectionCount ||
      Date.parse(b.lastDetectedAt) - Date.parse(a.lastDetectedAt) ||
      a.id - b.id,
  );
}

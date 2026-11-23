import type { DangerRating, Incident } from './api';

export interface RatingStyle {
  label: string;
  /** Literal colours: Leaflet writes them into SVG attributes, where CSS variables do not resolve. */
  color: string;
  text: string;
  rank: number;
}

/** McArthur bands, coloured from calm to critical. */
export const RATINGS: Record<DangerRating, RatingStyle> = {
  lowModerate: { label: 'Low–moderate', color: '#2e8b6a', text: '#ffffff', rank: 1 },
  high: { label: 'High', color: '#c79a00', text: '#1d1600', rank: 2 },
  veryHigh: { label: 'Very high', color: '#e2700b', text: '#ffffff', rank: 3 },
  severe: { label: 'Severe', color: '#cf3417', text: '#ffffff', rank: 4 },
  extreme: { label: 'Extreme', color: '#94171b', text: '#ffffff', rank: 5 },
  catastrophic: { label: 'Catastrophic', color: '#3d0b18', text: '#ffffff', rank: 6 },
};

export const NO_FORECAST: RatingStyle = { label: 'No forecast yet', color: '#7b6f6a', text: '#ffffff', rank: 0 };

export const ratingStyle = (incident: Incident): RatingStyle =>
  incident.forecast ? RATINGS[incident.forecast.dangerRating] : NO_FORECAST;

/** Most dangerous first; then larger fires; then most recently seen. */
export function sortIncidents(incidents: Incident[]): Incident[] {
  return [...incidents].sort(
    (a, b) =>
      ratingStyle(b).rank - ratingStyle(a).rank ||
      b.detectionCount - a.detectionCount ||
      Date.parse(b.lastDetectedAt) - Date.parse(a.lastDetectedAt),
  );
}

/** How many incidents fall in each rating, in band order, skipping empty bands. */
export function countByRating(incidents: Incident[]): { style: RatingStyle; count: number }[] {
  const styles = [...Object.values(RATINGS), NO_FORECAST].sort((a, b) => b.rank - a.rank);
  return styles
    .map((style) => ({ style, count: incidents.filter((i) => ratingStyle(i) === style).length }))
    .filter((x) => x.count > 0);
}

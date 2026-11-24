import type { DangerRating, Incident } from './api';

export interface RatingStyle {
  label: string;
  /** Short code for dense tables, as on a dispatch board. */
  code: string;
  /** Literal colours: MapLibre paints them in WebGL, where CSS variables do not exist. */
  color: string;
  rank: number;
}

/** McArthur bands, from calm to critical, tuned to read on a dark map. */
export const RATINGS: Record<DangerRating, RatingStyle> = {
  lowModerate: { label: 'Low–moderate', code: 'LM', color: '#4caf7d', rank: 1 },
  high: { label: 'High', code: 'H', color: '#d6c13c', rank: 2 },
  veryHigh: { label: 'Very high', code: 'VH', color: '#f28c28', rank: 3 },
  severe: { label: 'Severe', code: 'S', color: '#ea4f2f', rank: 4 },
  extreme: { label: 'Extreme', code: 'E', color: '#c8173a', rank: 5 },
  catastrophic: { label: 'Catastrophic', code: 'C', color: '#ff3ea5', rank: 6 },
};

export const NO_FORECAST: RatingStyle = { label: 'No forecast yet', code: '–', color: '#5d6b72', rank: 0 };

export const ratingStyle = (incident: Incident): RatingStyle =>
  incident.forecast ? RATINGS[incident.forecast.dangerRating] : NO_FORECAST;

/** How many incidents fall in each rating, most dangerous first, skipping empty bands. */
export function countByRating(incidents: Incident[]): { style: RatingStyle; count: number }[] {
  const styles = [...Object.values(RATINGS), NO_FORECAST].sort((a, b) => b.rank - a.rank);
  return styles
    .map((style) => ({ style, count: incidents.filter((i) => ratingStyle(i) === style).length }))
    .filter((x) => x.count > 0);
}

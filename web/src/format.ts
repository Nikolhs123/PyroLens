const COMPASS = ['N', 'NNE', 'NE', 'ENE', 'E', 'ESE', 'SE', 'SSE', 'S', 'SSW', 'SW', 'WSW', 'W', 'WNW', 'NW', 'NNW'];

/** 16-point compass name for a bearing in degrees. */
export function compass(degrees: number): string {
  const normalized = ((degrees % 360) + 360) % 360;
  return COMPASS[Math.round(normalized / 22.5) % 16];
}

export function formatDistance(meters: number): string {
  if (meters < 1000) return `${Math.round(meters)} m`;
  return `${(meters / 1000).toFixed(meters < 10_000 ? 1 : 0)} km`;
}

/** "just now", "12 min ago", "3 h ago", "2 d ago". */
export function relativeTime(iso: string, now: number = Date.now()): string {
  const minutes = Math.round((now - Date.parse(iso)) / 60_000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `${hours} h ago`;
  return `${Math.round(hours / 24)} d ago`;
}

/** Local time of day, e.g. "14:05". */
export function clockTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

export function formatCoordinates(latitude: number, longitude: number): string {
  return `${latitude.toFixed(3)}°N ${longitude.toFixed(3)}°E`;
}

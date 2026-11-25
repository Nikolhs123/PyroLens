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

/** Age for dense columns: "<1m", "18m", "11h", "2d". */
export function compactAge(iso: string, now: number = Date.now()): string {
  const minutes = Math.floor((now - Date.parse(iso)) / 60_000);
  if (minutes < 1) return '<1m';
  if (minutes < 60) return `${minutes}m`;
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return `${hours}h`;
  return `${Math.floor(hours / 24)}d`;
}

export const minutesSince = (iso: string, now: number = Date.now()): number => (now - Date.parse(iso)) / 60_000;

const pad = (n: number) => String(n).padStart(2, '0');

/** "22:31:04" in UTC. */
export function utcClock(date: Date): string {
  return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "24 Nov 22:05Z": operations convention, always UTC. */
export function utcStamp(iso: string): string {
  const d = new Date(iso);
  return `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]} ${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}Z`;
}

/** "23 Nov, 12:25" in the viewer's own time zone. */
export function localStamp(iso: string): string {
  const d = new Date(iso);
  return `${d.getDate()} ${MONTHS[d.getMonth()]}, ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function formatCoordinates(latitude: number, longitude: number): string {
  return `${latitude.toFixed(3)}°N ${longitude.toFixed(3)}°E`;
}

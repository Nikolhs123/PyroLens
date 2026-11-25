import type { Report, ReportKind } from './api';
import { formatDistance, relativeTime } from './format';
import { describePlace, nearestPlace } from './places';

/** Reports are blue on the map: a colour no fire or danger band uses. Literal, because MapLibre paints in WebGL. */
export const REPORT_COLOR = '#2f7fd8';

export const REPORT_KIND: Record<ReportKind, { label: string; noun: string }> = {
  smoke: { label: 'Smoke', noun: 'Smoke' },
  flames: { label: 'Flames', noun: 'Flames' },
  other: { label: 'Something else', noun: 'Something' },
};

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** "Smoke reported 12 min ago". */
export function describeReport(r: Report, now: number = Date.now()): string {
  return `${REPORT_KIND[r.kind].noun} reported ${relativeTime(r.submittedAt, now)}`;
}

/** "3 km north of Marathon". */
export const reportPlace = (r: Report): string => capitalise(describePlace(nearestPlace(r.latitude, r.longitude)));

/** How the report relates to the fires on the board: "1.2 km from fire #38", or that no fire is near. */
export function nearestFireText(r: Report): string {
  if (r.nearestIncidentId == null) return 'No fire on the board within 3 km';
  return `${formatDistance(r.nearestIncidentMeters ?? 0)} from fire #${r.nearestIncidentId}`;
}

/** New reports first, then newest first. */
export function sortReports(list: Report[]): Report[] {
  return [...list].sort(
    (a, b) => Number(b.status === 'new') - Number(a.status === 'new') || Date.parse(b.submittedAt) - Date.parse(a.submittedAt),
  );
}

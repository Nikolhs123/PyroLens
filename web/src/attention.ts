import type { Attention, AttentionLevel, Incident } from './api';
import { RATINGS } from './ratings';

/** Literal colours: the map paints them in WebGL, where CSS variables do not exist. */
export const ATTENTION_COLORS: Record<AttentionLevel, string> = { urgent: '#e03131', watch: '#e8a33d' };

const RULE_ORDER: Attention['rule'][] = ['noUnits', 'burningAgain', 'danger', 'reported', 'spreading'];

/** "less than a minute", "34 min", "1 h", "2 h 5 min". */
export function duration(fromIso: string, now: number): string {
  const minutes = Math.floor((now - Date.parse(fromIso)) / 60_000);
  if (minutes < 1) return 'less than a minute';
  if (minutes < 60) return `${minutes} min`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;
const dangerWord = (rating: keyof typeof RATINGS) => RATINGS[rating].short.toLowerCase();

/** One reason in plain words: "No units sent for 34 min". Time-based ones are worded from `now`, so they count up. */
export function describeAttention(a: Attention, now: number = Date.now()): string {
  switch (a.rule) {
    case 'noUnits':
      return a.since ? `No units sent for ${duration(a.since, now)}` : 'No units sent';
    case 'spreading':
      return `${plural(a.pixels ?? 0, 'new satellite pixel')} in the last ${plural(a.withinHours ?? 6, 'hour')}`;
    case 'burningAgain':
      return `Still burning: ${plural(a.pixels ?? 0, 'new satellite pixel')} since it was marked contained`;
    case 'danger':
      if (a.from && a.to) return `Fire danger rose from ${dangerWord(a.from)} to ${dangerWord(a.to)}`;
      return a.to ? `${RATINGS[a.to].short} fire danger` : 'High fire danger';
    case 'reported':
      return `${plural(a.reports ?? 1, 'report')} from the public nearby`;
  }
}

/** Most pressing first: urgent before watch, then by rule. */
export function sortAttention(list: Attention[]): Attention[] {
  return [...list].sort(
    (a, b) => Number(b.level === 'urgent') - Number(a.level === 'urgent') || RULE_ORDER.indexOf(a.rule) - RULE_ORDER.indexOf(b.rule),
  );
}

/** The highest level among a fire's reasons, or null when it needs nothing. */
export function attentionLevel(incident: Incident): AttentionLevel | null {
  const list = incident.attention ?? [];
  if (list.length === 0) return null;
  return list.some((a) => a.level === 'urgent') ? 'urgent' : 'watch';
}

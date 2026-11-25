import { describe, expect, it } from 'vitest';
import type { Attention, Incident } from './api';
import { attentionLevel, describeAttention, duration, sortAttention } from './attention';

const now = Date.parse('2026-11-25T21:00:00Z');

const flag = (over: Partial<Attention>): Attention => ({
  rule: 'noUnits',
  level: 'urgent',
  since: null,
  pixels: null,
  withinHours: null,
  from: null,
  to: null,
  ...over,
});

describe('duration', () => {
  it.each([
    ['2026-11-25T20:26:00Z', '34 min'],
    ['2026-11-25T20:00:00Z', '1 h'],
    ['2026-11-25T18:55:00Z', '2 h 5 min'],
    ['2026-11-25T20:59:30Z', 'less than a minute'],
    ['2026-11-25T21:05:00Z', 'less than a minute'],
  ])('%s is %s', (iso, text) => {
    expect(duration(iso, now)).toBe(text);
  });
});

describe('describeAttention', () => {
  it('counts the minutes without units up from the server time', () => {
    expect(describeAttention(flag({ since: '2026-11-25T20:26:00Z' }), now)).toBe('No units sent for 34 min');
    expect(describeAttention(flag({ since: '2026-11-25T20:26:00Z' }), now + 60_000)).toBe('No units sent for 35 min');
  });

  it('words pixels and hours', () => {
    expect(describeAttention(flag({ rule: 'spreading', level: 'watch', pixels: 6, withinHours: 6 }), now)).toBe(
      '6 new satellite pixels in the last 6 hours',
    );
    expect(describeAttention(flag({ rule: 'burningAgain', pixels: 1 }), now)).toBe(
      'Still burning: 1 new satellite pixel since it was marked contained',
    );
  });

  it('says how the danger changed', () => {
    expect(describeAttention(flag({ rule: 'danger', from: 'lowModerate', to: 'veryHigh' }), now)).toBe(
      'Fire danger rose from low to very high',
    );
    expect(describeAttention(flag({ rule: 'danger', to: 'extreme' }), now)).toBe('Extreme fire danger');
  });

  it('counts reports from the public', () => {
    expect(describeAttention(flag({ rule: 'reported', level: 'watch', reports: 1 }), now)).toBe('1 report from the public nearby');
    expect(describeAttention(flag({ rule: 'reported', level: 'watch', reports: 3 }), now)).toBe('3 reports from the public nearby');
  });
});

describe('sortAttention', () => {
  it('puts urgent reasons first', () => {
    const sorted = sortAttention([
      flag({ rule: 'spreading', level: 'watch' }),
      flag({ rule: 'danger', level: 'watch' }),
      flag({ rule: 'noUnits', level: 'urgent' }),
    ]);
    expect(sorted.map((a) => a.rule)).toEqual(['noUnits', 'danger', 'spreading']);
  });
});

describe('attentionLevel', () => {
  const incident = (attention?: Attention[]) => ({ attention }) as Incident;

  it('is the highest level, or null', () => {
    expect(attentionLevel(incident([flag({ level: 'watch' }), flag({ level: 'urgent' })]))).toBe('urgent');
    expect(attentionLevel(incident([flag({ level: 'watch' })]))).toBe('watch');
    expect(attentionLevel(incident([]))).toBeNull();
    expect(attentionLevel(incident(undefined))).toBeNull();
  });
});

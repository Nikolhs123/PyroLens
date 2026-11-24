import { describe, expect, it } from 'vitest';
import { compactAge, compass, formatCoordinates, formatDistance, relativeTime, utcClock, utcStamp } from './format';

describe('compactAge', () => {
  const now = Date.parse('2026-11-24T22:30:00Z');

  it.each([
    ['2026-11-24T22:29:40Z', '<1m'],
    ['2026-11-24T22:12:00Z', '18m'],
    ['2026-11-24T11:00:00Z', '11h'],
    ['2026-11-22T22:30:00Z', '2d'],
  ])('%s is %s', (iso, text) => {
    expect(compactAge(iso, now)).toBe(text);
  });
});

describe('UTC formatting', () => {
  it('shows the clock and stamps in UTC, whatever the browser time zone', () => {
    expect(utcClock(new Date('2026-11-24T22:31:04Z'))).toBe('22:31:04');
    expect(utcStamp('2026-11-24T22:05:02.82+00:00')).toBe('24 Nov 22:05Z');
    expect(utcStamp('2026-01-03T00:00:00+02:00')).toBe('2 Jan 22:00Z');
  });
});

describe('compass', () => {
  it.each([
    [0, 'N'],
    [22.5, 'NNE'],
    [90, 'E'],
    [180, 'S'],
    [262, 'W'],
    [350, 'N'],
    [360, 'N'],
    [-90, 'W'],
    [445, 'E'],
  ])('%s° is %s', (degrees, name) => {
    expect(compass(degrees)).toBe(name);
  });
});

describe('formatDistance', () => {
  it.each([
    [35.4, '35 m'],
    [999, '999 m'],
    [1468, '1.5 km'],
    [12_400, '12 km'],
  ])('%s m reads %s', (meters, text) => {
    expect(formatDistance(meters)).toBe(text);
  });
});

describe('relativeTime', () => {
  const now = Date.parse('2026-11-23T12:00:00Z');

  it.each([
    ['2026-11-23T11:59:40Z', 'just now'],
    ['2026-11-23T11:48:00Z', '12 min ago'],
    ['2026-11-23T09:00:00Z', '3 h ago'],
    ['2026-11-21T12:00:00Z', '2 d ago'],
  ])('%s is %s', (iso, text) => {
    expect(relativeTime(iso, now)).toBe(text);
  });
});

describe('formatCoordinates', () => {
  it('rounds to three decimals', () => {
    expect(formatCoordinates(39.6614875, 22.713675)).toBe('39.661°N 22.714°E');
  });
});

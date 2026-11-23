import { describe, expect, it } from 'vitest';
import { compass, formatCoordinates, formatDistance, relativeTime } from './format';

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

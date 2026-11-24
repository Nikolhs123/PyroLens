import { describe, expect, it } from 'vitest';
import { NO_FORECAST, RATINGS, countByRating, ratingStyle } from './ratings';
import { incident } from './testing';

describe('ratingStyle', () => {
  it('uses the forecast rating, or a neutral style without a forecast', () => {
    expect(ratingStyle(incident(1, 'severe'))).toBe(RATINGS.severe);
    expect(ratingStyle(incident(2, null))).toBe(NO_FORECAST);
  });

  it('gives every band a distinct code and colour, ranked from calm to critical', () => {
    const bands = Object.values(RATINGS);
    expect(new Set(bands.map((b) => b.code)).size).toBe(bands.length);
    expect(new Set(bands.map((b) => b.color)).size).toBe(bands.length);
    expect(bands.map((b) => b.rank)).toEqual([1, 2, 3, 4, 5, 6]);
  });
});

describe('countByRating', () => {
  it('counts per band, most dangerous first, skipping empty bands', () => {
    const counts = countByRating([incident(1, 'high'), incident(2, 'high'), incident(3, 'severe'), incident(4, null)]);

    expect(counts.map((c) => [c.style.code, c.count])).toEqual([
      ['S', 1],
      ['H', 2],
      ['–', 1],
    ]);
  });
});

import { describe, expect, it } from 'vitest';
import { bearing, describePlace, directionWord, distanceKm, nearestPlace } from './places';

describe('distance and bearing', () => {
  it('measures Athens to Thessaloniki at about 300 km, north-northwest', () => {
    expect(distanceKm(37.984, 23.728, 40.64, 22.944)).toBeCloseTo(303, -1);
    expect(directionWord(bearing(37.984, 23.728, 40.64, 22.944))).toBe('north');
  });

  it.each([
    [0, 'north'],
    [44, 'northeast'],
    [180, 'south'],
    [225, 'southwest'],
    [350, 'north'],
    [-90, 'west'],
  ])('%s° reads %s', (deg, word) => {
    expect(directionWord(deg)).toBe(word);
  });
});

describe('nearestPlace', () => {
  it('names a fire by the closest town and where it lies from it', () => {
    // Real incident 38 on 23 Nov 2026, just east of Feres in Evros.
    const place = nearestPlace(40.842, 26.174);
    expect(place.town).toBe('Feres');
    expect(place.direction).toBe('south');
    expect(describePlace(place)).toBe('6 km south of Feres');
  });

  it('says "near" when the fire is at the town', () => {
    expect(describePlace(nearestPlace(40.995, 22.88))).toBe('near Kilkis');
  });
});

import { describe, expect, it } from 'vitest';
import { bboxOf, padBBox } from './geo';

describe('bboxOf', () => {
  it('bounds a point, a line and a polygon', () => {
    expect(bboxOf({ type: 'Point', coordinates: [23.7, 38.1] })).toEqual([23.7, 38.1, 23.7, 38.1]);
    expect(bboxOf({ type: 'LineString', coordinates: [[23.7, 38.1], [23.6, 38.3]] })).toEqual([23.6, 38.1, 23.7, 38.3]);
    expect(
      bboxOf({ type: 'Polygon', coordinates: [[[22.71, 39.659], [22.717, 39.66], [22.713, 39.665], [22.71, 39.659]]] }),
    ).toEqual([22.71, 39.659, 22.717, 39.665]);
  });

  it('returns null when there is nothing to bound', () => {
    expect(bboxOf(null)).toBeNull();
    expect(bboxOf(undefined)).toBeNull();
    expect(bboxOf({ type: 'MultiPoint', coordinates: [] })).toBeNull();
  });
});

describe('padBBox', () => {
  it('widens a single point to the minimum span, centred', () => {
    const [w, s, e, n] = padBBox([23, 38, 23, 38], 0.02);
    expect([w, s, e, n].map((v) => +v.toFixed(3))).toEqual([22.99, 37.99, 23.01, 38.01]);
  });

  it('leaves boxes that are already large enough alone', () => {
    expect(padBBox([20, 35, 21, 36], 0.02)).toEqual([20, 35, 21, 36]);
  });
});

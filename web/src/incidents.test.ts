import { describe, expect, it } from 'vitest';
import { sortIncidents } from './incidents';
import { incident } from './testing';

const sample = [
  incident(1, 'lowModerate', 9, '2026-11-23T10:00:00Z', 4),
  incident(2, null, 20, '2026-11-23T10:00:00Z', 30),
  incident(3, 'extreme', 1, '2026-11-23T10:00:00Z', null),
  incident(4, 'high', 2, '2026-11-23T09:00:00Z', 12),
  incident(5, 'high', 2, '2026-11-23T11:00:00Z', 12),
  incident(6, 'high', 5, '2026-11-23T10:00:00Z', 1),
];
const ids = (list: { id: number }[]) => list.map((i) => i.id);

describe('sortIncidents', () => {
  it('defaults to most dangerous first, then larger, then most recent', () => {
    expect(ids(sortIncidents(sample))).toEqual([3, 6, 5, 4, 1, 2]);
  });

  it('sorts by pixels, breaking ties by danger', () => {
    expect(ids(sortIncidents(sample, 'pixels'))).toEqual([2, 1, 6, 5, 4, 3]);
  });

  it('sorts by strongest pixel, with unknown FRP last', () => {
    expect(ids(sortIncidents(sample, 'frp'))).toEqual([2, 5, 4, 1, 6, 3]);
  });

  it('sorts by most recently seen first', () => {
    expect(ids(sortIncidents(sample, 'seen')).slice(0, 2)).toEqual([5, 3]);
    expect(ids(sortIncidents(sample, 'seen')).at(-1)).toBe(4);
  });

  it('sorts by id ascending, and reverses on request', () => {
    expect(ids(sortIncidents(sample, 'id'))).toEqual([1, 2, 3, 4, 5, 6]);
    expect(ids(sortIncidents(sample, 'id', true))).toEqual([6, 5, 4, 3, 2, 1]);
  });

  it('does not change the input array', () => {
    const input = [incident(1, 'high'), incident(2, 'severe')];
    sortIncidents(input);
    expect(ids(input)).toEqual([1, 2]);
  });
});

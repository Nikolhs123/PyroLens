import type { Geometry, Position } from 'geojson';

/** [west, south, east, north] in degrees. */
export type BBox = [number, number, number, number];

function positions(geometry: Geometry): Position[] {
  switch (geometry.type) {
    case 'Point':
      return [geometry.coordinates];
    case 'MultiPoint':
    case 'LineString':
      return geometry.coordinates;
    case 'MultiLineString':
    case 'Polygon':
      return geometry.coordinates.flat();
    case 'MultiPolygon':
      return geometry.coordinates.flat(2);
    case 'GeometryCollection':
      return geometry.geometries.flatMap(positions);
  }
}

/** Bounding box of any GeoJSON geometry, or null when there is nothing to bound. */
export function bboxOf(geometry: Geometry | null | undefined): BBox | null {
  if (!geometry) return null;
  const points = positions(geometry);
  if (points.length === 0) return null;

  let [west, south, east, north] = [Infinity, Infinity, -Infinity, -Infinity];
  for (const [lng, lat] of points) {
    west = Math.min(west, lng);
    east = Math.max(east, lng);
    south = Math.min(south, lat);
    north = Math.max(north, lat);
  }
  return [west, south, east, north];
}

/** Widens a box to at least `minSpan` degrees each way, so a single point does not zoom to street level. */
export function padBBox([west, south, east, north]: BBox, minSpan: number): BBox {
  const growX = Math.max(0, minSpan - (east - west)) / 2;
  const growY = Math.max(0, minSpan - (north - south)) / 2;
  return [west - growX, south - growY, east + growX, north + growY];
}

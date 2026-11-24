import type { Feature, FeatureCollection, Point } from 'geojson';
import maplibregl, { type GeoJSONSource, type LngLatBoundsLike, type Map as MapLibreMap } from 'maplibre-gl';
import { useEffect, useRef, useState } from 'react';
import type { Detection, Forecast, Incident } from '../api';
import { bboxOf, padBBox } from '../geo';
import { ratingStyle } from '../ratings';

/** Free vector tiles, no key: https://openfreemap.org */
const STYLE_URL = 'https://tiles.openfreemap.org/styles/dark';

export const BURNING_COLOR = '#ff5a1f';
export const SELECTED_COLOR = '#ffb000';
const PIXEL_COLOR = '#ff8a3d';
const GREECE: LngLatBoundsLike = [
  [19.4, 34.8],
  [28.3, 41.8],
];
const PADDING = 24;
const EMPTY: FeatureCollection = { type: 'FeatureCollection', features: [] };

/** Marker size grows with the number of satellite pixels, gently. */
const radiusFor = (incident: Incident) => 4 + 2 * Math.sqrt(incident.detectionCount);

interface Props {
  incidents: Incident[];
  detections: Detection[] | null;
  selected: Incident | null;
  forecast: Forecast | null;
  onSelect: (id: number) => void;
}

export function MapView({ incidents, detections, selected, forecast, onSelect }: Props) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;
  const hadSelection = useRef(false);
  const [ready, setReady] = useState(false);

  // Create the map once.
  useEffect(() => {
    const element = container.current!;
    const map = new maplibregl.Map({
      container: element,
      style: STYLE_URL,
      bounds: GREECE,
      fitBoundsOptions: { padding: PADDING },
      attributionControl: false,
      dragRotate: false,
      pitchWithRotate: false,
    });
    map.touchZoomRotate.disableRotation();
    map.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right');
    map.addControl(
      new maplibregl.AttributionControl({ compact: true, customAttribution: 'Fires: NASA FIRMS · Weather: Open-Meteo' }),
      'bottom-right',
    );

    map.on('load', () => {
      addLayers(map);
      wireInteractions(map, (id) => onSelectRef.current(id));
      setReady(true);
    });

    // The map measures its container when created. If the stylesheet arrived after the script (common on a
    // cold-started host) that size is 0×0; re-measure on every size change and fit Greece once it is real.
    const hasSize = () => element.clientWidth > 0 && element.clientHeight > 0;
    let sized = hasSize();
    const observer = new ResizeObserver(() => {
      map.resize();
      if (!sized && hasSize()) {
        sized = true;
        map.fitBounds(GREECE, { padding: PADDING, animate: false });
      }
    });
    observer.observe(element);

    mapRef.current = map;
    return () => {
      observer.disconnect();
      map.remove();
      mapRef.current = null;
      setReady(false);
    };
  }, []);

  // Incidents and their burning footprints.
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    const points: Feature<Point>[] = incidents.map((i) => {
      const style = ratingStyle(i);
      return {
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [i.longitude, i.latitude] },
        properties: {
          id: i.id,
          color: style.color,
          radius: radiusFor(i),
          selected: i.id === selected?.id,
          label: `#${i.id} · ${style.code} · ${i.detectionCount} px`,
        },
      };
    });
    source(map, 'incidents').setData({ type: 'FeatureCollection', features: points });

    source(map, 'footprints').setData({
      type: 'FeatureCollection',
      features: incidents
        .filter((i) => i.footprint && i.footprint.type !== 'Point')
        .map((i) => ({ type: 'Feature', geometry: i.footprint!, properties: { id: i.id } })),
    });
  }, [ready, incidents, selected?.id]);

  // Spread areas of the selected incident.
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    const color = selected ? ratingStyle(selected).color : BURNING_COLOR;
    source(map, 'forecast').setData(
      forecast && selected && forecast.incidentId === selected.id
        ? {
            type: 'FeatureCollection',
            features: forecast.horizons.map((h) => ({
              type: 'Feature',
              geometry: h.spreadArea,
              properties: { horizon: h.horizonHours, color },
            })),
          }
        : EMPTY,
    );
  }, [ready, forecast, selected]);

  // Raw satellite pixels.
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    source(map, 'pixels').setData({
      type: 'FeatureCollection',
      features: (detections ?? []).map((d) => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [d.longitude, d.latitude] },
        properties: {},
      })),
    });
  }, [ready, detections]);

  // Zoom to the selection (and its +3 h area once loaded); back to Greece when cleared.
  const forecastKey = forecast && selected && forecast.incidentId === selected.id ? forecast.issuedAt : null;
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    if (!selected) {
      if (hadSelection.current) map.fitBounds(GREECE, { padding: PADDING, duration: 600 });
      hadSelection.current = false;
      return;
    }
    hadSelection.current = true;

    const threeHours = forecastKey ? forecast!.horizons.find((h) => h.horizonHours === 3)?.spreadArea : undefined;
    const box = bboxOf(threeHours ?? selected.footprint) ?? [selected.longitude, selected.latitude, selected.longitude, selected.latitude];
    const [west, south, east, north] = padBBox(box, 0.02);
    map.fitBounds(
      [
        [west, south],
        [east, north],
      ],
      { padding: 80, maxZoom: 14, duration: 600 },
    );
    // Re-fit when the selection or its forecast changes, not on every poll of the same data.
  }, [ready, selected?.id, forecastKey]);

  return <div ref={container} className="map" role="region" aria-label="Map of fire incidents in Greece" />;
}

const source = (map: MapLibreMap, id: string) => map.getSource(id) as GeoJSONSource;

/**
 * Layers from bottom to top: forecast fills, burning footprints, forecast outlines, pixels, incident markers.
 * The outlines sit above the footprints: a spread area is the footprint plus the ellipse, so its edge often
 * runs along the burning area and would otherwise be hidden by it.
 */
function addLayers(map: MapLibreMap) {
  for (const id of ['forecast', 'footprints', 'pixels', 'incidents']) {
    map.addSource(id, { type: 'geojson', data: EMPTY });
  }

  map.addLayer({
    id: 'forecast-fill',
    type: 'fill',
    source: 'forecast',
    paint: { 'fill-color': ['get', 'color'], 'fill-opacity': ['case', ['==', ['get', 'horizon'], 1], 0.3, 0.12] },
  });
  map.addLayer({
    id: 'footprints-fill',
    type: 'fill',
    source: 'footprints',
    filter: ['==', ['geometry-type'], 'Polygon'],
    paint: { 'fill-color': BURNING_COLOR, 'fill-opacity': 0.4 },
  });
  map.addLayer({
    id: 'footprints-line',
    type: 'line',
    source: 'footprints',
    paint: { 'line-color': BURNING_COLOR, 'line-width': 1.5 },
  });
  map.addLayer({
    id: 'forecast-3h-line',
    type: 'line',
    source: 'forecast',
    filter: ['==', ['get', 'horizon'], 3],
    paint: { 'line-color': ['get', 'color'], 'line-width': 2, 'line-dasharray': [3, 2] },
  });
  map.addLayer({
    id: 'forecast-1h-line',
    type: 'line',
    source: 'forecast',
    filter: ['==', ['get', 'horizon'], 1],
    paint: { 'line-color': ['get', 'color'], 'line-width': 2 },
  });
  map.addLayer({
    id: 'pixels',
    type: 'circle',
    source: 'pixels',
    paint: { 'circle-radius': 2.5, 'circle-color': PIXEL_COLOR, 'circle-opacity': 0.9 },
  });
  map.addLayer({
    id: 'incidents-selected',
    type: 'circle',
    source: 'incidents',
    filter: ['==', ['get', 'selected'], true],
    paint: {
      'circle-radius': ['+', ['get', 'radius'], 5],
      'circle-color': 'rgba(0, 0, 0, 0)',
      'circle-stroke-color': SELECTED_COLOR,
      'circle-stroke-width': 2,
    },
  });
  map.addLayer({
    id: 'incidents',
    type: 'circle',
    source: 'incidents',
    paint: {
      'circle-radius': ['get', 'radius'],
      'circle-color': ['get', 'color'],
      'circle-opacity': 0.92,
      'circle-stroke-color': '#0a0e11',
      'circle-stroke-width': 1.5,
    },
  });
}

/** Hover label and click-to-select on incident markers. */
function wireInteractions(map: MapLibreMap, select: (id: number) => void) {
  const popup = new maplibregl.Popup({ closeButton: false, closeOnClick: false, offset: 12, className: 'pl-popup' });

  map.on('mouseenter', 'incidents', (e) => {
    map.getCanvas().style.cursor = 'pointer';
    const feature = e.features?.[0];
    if (!feature) return;
    const [lng, lat] = (feature.geometry as Point).coordinates;
    popup.setLngLat([lng, lat]).setText(String(feature.properties?.label ?? '')).addTo(map);
  });
  map.on('mouseleave', 'incidents', () => {
    map.getCanvas().style.cursor = '';
    popup.remove();
  });
  map.on('click', 'incidents', (e) => {
    const id = Number(e.features?.[0]?.properties?.id);
    if (Number.isFinite(id)) select(id);
  });
}

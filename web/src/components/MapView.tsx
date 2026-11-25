import type { Feature, FeatureCollection, Point } from 'geojson';
import maplibregl, { type GeoJSONSource, type LngLatBoundsLike, type Map as MapLibreMap } from 'maplibre-gl';
import { useEffect, useRef, useState } from 'react';
import type { AirspaceReservation, Detection, Forecast, Incident, Resource } from '../api';
import { ATTENTION_COLORS, attentionLevel, describeAttention, sortAttention } from '../attention';
import { bboxOf, padBBox } from '../geo';
import { describePlace, nearestPlace } from '../places';
import { ratingStyle } from '../ratings';
import type { Theme } from '../theme';
import { AIRSPACE_COLORS } from './AirspaceTab';

/**
 * Free vector tiles, no key (https://openfreemap.org). "liberty" is the bright, OpenStreetMap-like look of the
 * first version of the map; "dark" goes with the dark theme.
 */
const STYLES: Record<Theme, string> = {
  light: 'https://tiles.openfreemap.org/styles/liberty',
  dark: 'https://tiles.openfreemap.org/styles/dark',
};

/** Overlay colours that depend on the basemap underneath. */
const PAINT: Record<Theme, { outline: string; selected: string; label: string; halo: string }> = {
  light: { outline: '#ffffff', selected: '#d98e00', label: '#7a4d00', halo: '#ffffff' },
  dark: { outline: '#0a0e11', selected: '#ffb000', label: '#ffb000', halo: '#0a0e11' },
};

export const BURNING_COLOR = '#ff5a1f';
export const SELECTED_COLOR = '#e3a008';
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
  resources: Resource[];
  airspace: AirspaceReservation[];
  theme: Theme;
  onSelect: (id: number) => void;
}

export function MapView({ incidents, detections, selected, forecast, resources, airspace, theme, onSelect }: Props) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;
  const themeRef = useRef(theme);
  themeRef.current = theme;
  const hadSelection = useRef(false);
  // Counts loaded styles: our sources and layers are added again after every style change (theme switch),
  // and the data effects below re-run when it changes.
  const [styleVersion, setStyleVersion] = useState(0);
  const ready = styleVersion > 0;

  // Create the map once.
  useEffect(() => {
    const element = container.current!;
    const map = new maplibregl.Map({
      container: element,
      style: STYLES[themeRef.current],
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

    // Fires for the first style and after every setStyle: a new style has none of our layers.
    map.on('style.load', () => {
      addLayers(map, themeRef.current);
      setStyleVersion((v) => v + 1);
    });
    // Listeners bound to layer ids survive style changes, so these are added once.
    map.once('load', () => wireInteractions(map, (id) => onSelectRef.current(id)));

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
      setStyleVersion(0);
    };
  }, []);

  // Switch basemap with the theme; the 'style.load' handler above restores our layers.
  const appliedTheme = useRef(theme);
  useEffect(() => {
    const map = mapRef.current;
    if (!map || appliedTheme.current === theme) return;
    appliedTheme.current = theme;
    // diff: false forces a full reload. With the default diff, MapLibre would remove our layers (they are not in
    // the new style) without firing 'style.load', so they would never come back.
    map.setStyle(STYLES[theme], { diff: false });
  }, [theme]);

  // Incidents and their burning footprints.
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    const points: Feature<Point>[] = incidents.map((i) => {
      const style = ratingStyle(i);
      const reason = sortAttention(i.attention ?? [])[0];
      return {
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [i.longitude, i.latitude] },
        properties: {
          id: i.id,
          color: style.color,
          radius: radiusFor(i),
          selected: i.id === selected?.id,
          attention: attentionLevel(i) ?? 'none',
          label:
            `${capitalise(describePlace(nearestPlace(i.latitude, i.longitude)))} · ${style.short} danger` +
            (reason ? ` · ${describeAttention(reason)}` : ''),
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
  }, [styleVersion, incidents, selected?.id]);

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
  }, [styleVersion, forecast, selected]);

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
  }, [styleVersion, detections]);

  // Live airspace reservations, all incidents.
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    source(map, 'airspace').setData({
      type: 'FeatureCollection',
      features: airspace.map((a) => ({
        type: 'Feature',
        geometry: a.area,
        properties: { color: AIRSPACE_COLORS[a.level], level: a.level },
      })),
    });
  }, [styleVersion, airspace]);

  // Units committed to the selected incident: their base, and a line to the fire.
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    const units = selected ? resources.filter((r) => r.incidentId === selected.id) : [];
    source(map, 'units').setData({
      type: 'FeatureCollection',
      features: units.flatMap((r): Feature[] => [
        {
          type: 'Feature',
          geometry: { type: 'LineString', coordinates: [[r.longitude, r.latitude], [selected!.longitude, selected!.latitude]] },
          properties: { status: r.status },
        },
        {
          type: 'Feature',
          geometry: { type: 'Point', coordinates: [r.longitude, r.latitude] },
          properties: { status: r.status, label: `${r.callsign}${r.status === 'reserved' ? ' (on hold)' : ''}` },
        },
      ]),
    });
  }, [styleVersion, resources, selected]);

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

/**
 * Our GeoJSON source, or a stand-in that ignores data while a style change is in progress (the new style has no
 * sources until 'style.load', and that handler bumps styleVersion so every effect pushes its data again).
 */
const source = (map: MapLibreMap, id: string): Pick<GeoJSONSource, 'setData'> =>
  (map.getSource(id) as GeoJSONSource | undefined) ?? { setData: () => undefined as never };

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/**
 * Layers from bottom to top: forecast fills, burning footprints, forecast outlines, pixels, incident markers.
 * The outlines sit above the footprints: a spread area is the footprint plus the ellipse, so its edge often
 * runs along the burning area and would otherwise be hidden by it.
 */
function addLayers(map: MapLibreMap, theme: Theme) {
  const paint = PAINT[theme];
  for (const id of ['airspace', 'forecast', 'footprints', 'units', 'pixels', 'incidents']) {
    map.addSource(id, { type: 'geojson', data: EMPTY });
  }

  // Airspace first: kilometre-wide circles underneath everything else.
  map.addLayer({
    id: 'airspace-fill',
    type: 'fill',
    source: 'airspace',
    paint: { 'fill-color': ['get', 'color'], 'fill-opacity': 0.06 },
  });
  map.addLayer({
    id: 'airspace-line',
    type: 'line',
    source: 'airspace',
    paint: { 'line-color': ['get', 'color'], 'line-width': 1.5, 'line-dasharray': [1, 2], 'line-opacity': 0.9 },
  });

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
    id: 'units-line',
    type: 'line',
    source: 'units',
    filter: ['==', ['geometry-type'], 'LineString'],
    paint: {
      'line-color': paint.selected,
      'line-width': 1.2,
      'line-opacity': ['case', ['==', ['get', 'status'], 'assigned'], 0.85, 0.45],
      'line-dasharray': [4, 3],
    },
  });
  map.addLayer({
    id: 'units-point',
    type: 'circle',
    source: 'units',
    filter: ['==', ['geometry-type'], 'Point'],
    paint: { 'circle-radius': 4, 'circle-color': paint.selected, 'circle-stroke-color': paint.outline, 'circle-stroke-width': 1.5 },
  });
  map.addLayer({
    id: 'units-label',
    type: 'symbol',
    source: 'units',
    filter: ['==', ['geometry-type'], 'Point'],
    layout: {
      'text-field': ['get', 'label'],
      'text-font': ['Noto Sans Regular'],
      'text-size': 11,
      'text-offset': [0, 1.1],
      'text-anchor': 'top',
      'text-allow-overlap': true,
    },
    paint: { 'text-color': paint.label, 'text-halo-color': paint.halo, 'text-halo-width': 1.5 },
  });
  map.addLayer({
    id: 'pixels',
    type: 'circle',
    source: 'pixels',
    paint: { 'circle-radius': 2.5, 'circle-color': PIXEL_COLOR, 'circle-opacity': 0.9 },
  });
  // Fires that need attention: a red (urgent) or amber (watch) ring just outside the marker.
  map.addLayer({
    id: 'incidents-attention',
    type: 'circle',
    source: 'incidents',
    filter: ['!=', ['get', 'attention'], 'none'],
    paint: {
      'circle-radius': ['+', ['get', 'radius'], 2],
      'circle-color': 'rgba(0, 0, 0, 0)',
      'circle-stroke-color': ['match', ['get', 'attention'], 'urgent', ATTENTION_COLORS.urgent, ATTENTION_COLORS.watch],
      'circle-stroke-width': 2.5,
    },
  });
  map.addLayer({
    id: 'incidents-selected',
    type: 'circle',
    source: 'incidents',
    filter: ['==', ['get', 'selected'], true],
    paint: {
      'circle-radius': ['+', ['get', 'radius'], 5],
      'circle-color': 'rgba(0, 0, 0, 0)',
      'circle-stroke-color': paint.selected,
      'circle-stroke-width': 2.5,
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
      'circle-stroke-color': paint.outline,
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

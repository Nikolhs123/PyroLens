import type { Feature, Point } from 'geojson';
import maplibregl, { type Map as MapLibreMap } from 'maplibre-gl';
import { useEffect, useRef } from 'react';
import type { AirspaceReservation, Detection, Forecast, Incident, Report, Resource } from '../api';
import { ATTENTION_COLORS, attentionLevel, describeAttention, sortAttention } from '../attention';
import { bboxOf, padBBox } from '../geo';
import { EMPTY, GREECE, PADDING, PAINT, source, useBaseMap } from '../mapBase';
import { describePlace, nearestPlace } from '../places';
import { ratingStyle } from '../ratings';
import { REPORT_COLOR, describeReport } from '../reports';
import type { Theme } from '../theme';
import { AIRSPACE_COLORS } from './AirspaceTab';

export const BURNING_COLOR = '#ff5a1f';
export const SELECTED_COLOR = '#e3a008';
const PIXEL_COLOR = '#ff8a3d';

/** Marker size grows with the number of satellite pixels, gently. */
const radiusFor = (incident: Incident) => 4 + 2 * Math.sqrt(incident.detectionCount);

interface Props {
  incidents: Incident[];
  detections: Detection[] | null;
  selected: Incident | null;
  forecast: Forecast | null;
  resources: Resource[];
  airspace: AirspaceReservation[];
  reports: Report[];
  selectedReport: Report | null;
  theme: Theme;
  onSelect: (id: number) => void;
  onSelectReport: (id: number) => void;
}

export function MapView({
  incidents, detections, selected, forecast, resources, airspace, reports, selectedReport, theme, onSelect, onSelectReport,
}: Props) {
  const onSelectRef = useRef(onSelect);
  onSelectRef.current = onSelect;
  const onSelectReportRef = useRef(onSelectReport);
  onSelectReportRef.current = onSelectReport;
  const hadSelection = useRef(false);
  const { container, mapRef, styleVersion, ready } = useBaseMap(theme, addLayers, (map) =>
    wireInteractions(map, (id) => onSelectRef.current(id), (id) => onSelectReportRef.current(id)),
  );

  // Reports from the public (last 24 h, not dismissed).
  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;

    source(map, 'reports').setData({
      type: 'FeatureCollection',
      features: reports.map((r) => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [r.longitude, r.latitude] },
        properties: { id: r.id, status: r.status, selected: r.id === selectedReport?.id, label: describeReport(r) },
      })),
    });
  }, [styleVersion, reports, selectedReport?.id]);

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

    if (selectedReport) {
      hadSelection.current = true;
      map.easeTo({ center: [selectedReport.longitude, selectedReport.latitude], zoom: Math.max(map.getZoom(), 11), duration: 600 });
      return;
    }

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
  }, [ready, selected?.id, forecastKey, selectedReport?.id]);

  return <div ref={container} className="map" role="region" aria-label="Map of fire incidents in Greece" />;
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/**
 * Layers from bottom to top: forecast fills, burning footprints, forecast outlines, pixels, incident markers.
 * The outlines sit above the footprints: a spread area is the footprint plus the ellipse, so its edge often
 * runs along the burning area and would otherwise be hidden by it.
 */
function addLayers(map: MapLibreMap, theme: Theme) {
  const paint = PAINT[theme];
  for (const id of ['airspace', 'forecast', 'footprints', 'units', 'pixels', 'reports', 'incidents']) {
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
  // Reports from the public: blue dots, faded once a dispatcher has seen them.
  map.addLayer({
    id: 'reports-selected',
    type: 'circle',
    source: 'reports',
    filter: ['==', ['get', 'selected'], true],
    paint: { 'circle-radius': 11, 'circle-color': 'rgba(0, 0, 0, 0)', 'circle-stroke-color': paint.selected, 'circle-stroke-width': 2.5 },
  });
  map.addLayer({
    id: 'reports',
    type: 'circle',
    source: 'reports',
    paint: {
      'circle-radius': 6,
      'circle-color': REPORT_COLOR,
      'circle-opacity': ['case', ['==', ['get', 'status'], 'new'], 1, 0.45],
      'circle-stroke-color': paint.outline,
      'circle-stroke-width': 2,
    },
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

/** Hover label and click-to-select on fire and report markers. */
function wireInteractions(map: MapLibreMap, selectFire: (id: number) => void, selectReport: (id: number) => void) {
  const popup = new maplibregl.Popup({ closeButton: false, closeOnClick: false, offset: 12, className: 'pl-popup' });

  for (const [layer, select] of [['incidents', selectFire], ['reports', selectReport]] as const) {
    map.on('mouseenter', layer, (e) => {
      map.getCanvas().style.cursor = 'pointer';
      const feature = e.features?.[0];
      if (!feature) return;
      const [lng, lat] = (feature.geometry as Point).coordinates;
      popup.setLngLat([lng, lat]).setText(String(feature.properties?.label ?? '')).addTo(map);
    });
    map.on('mouseleave', layer, () => {
      map.getCanvas().style.cursor = '';
      popup.remove();
    });
    map.on('click', layer, (e) => {
      const id = Number(e.features?.[0]?.properties?.id);
      if (Number.isFinite(id)) select(id);
    });
  }
}

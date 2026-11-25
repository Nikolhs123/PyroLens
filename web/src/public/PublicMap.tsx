import type { Point } from 'geojson';
import maplibregl, { type Map as MapLibreMap, type Marker } from 'maplibre-gl';
import { useEffect, useRef } from 'react';
import type { PublicFire } from '../api';
import { PAINT, source, useBaseMap } from '../mapBase';
import { STATUS } from '../operations';
import { describePlace, nearestPlace } from '../places';
import { REPORT_COLOR } from '../reports';
import type { Theme } from '../theme';

export interface Spot {
  latitude: number;
  longitude: number;
}

/** Satellite detections nobody has confirmed yet: a faint ring, so they read as "maybe". */
export const DETECTION_COLOR = '#c2551c';

interface Props {
  fires: PublicFire[];
  theme: Theme;
  /** While true, a tap on the map chooses the place of a report. */
  picking: boolean;
  spot: Spot | null;
  onSpot: (spot: Spot) => void;
}

const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** The public map: confirmed fires as solid dots, satellite detections as rings, and the pin of a report being written. */
export function PublicMap({ fires, theme, picking, spot, onSpot }: Props) {
  const pickingRef = useRef(picking);
  pickingRef.current = picking;
  const onSpotRef = useRef(onSpot);
  onSpotRef.current = onSpot;
  const marker = useRef<Marker | null>(null);

  const { container, mapRef, styleVersion, ready } = useBaseMap(theme, addLayers, (map) => wire(map), 'Fires: NASA FIRMS');

  function wire(map: MapLibreMap) {
    const popup = new maplibregl.Popup({ closeButton: false, closeOnClick: false, offset: 12, className: 'pl-popup' });
    for (const layer of ['fires-official', 'fires-detected']) {
      map.on('mouseenter', layer, (e) => {
        const feature = e.features?.[0];
        if (!feature || pickingRef.current) return;
        const [lng, lat] = (feature.geometry as Point).coordinates;
        popup.setLngLat([lng, lat]).setText(String(feature.properties?.label ?? '')).addTo(map);
      });
      map.on('mouseleave', layer, () => popup.remove());
    }
    map.on('click', (e) => {
      if (pickingRef.current) onSpotRef.current({ latitude: e.lngLat.lat, longitude: e.lngLat.lng });
    });
  }

  useEffect(() => {
    const map = mapRef.current;
    if (!ready || !map) return;
    source(map, 'fires').setData({
      type: 'FeatureCollection',
      features: fires.map((f) => ({
        type: 'Feature',
        geometry: { type: 'Point', coordinates: [f.longitude, f.latitude] },
        properties: {
          official: f.status !== 'detected',
          color: STATUS[f.status].color,
          label:
            f.status === 'detected'
              ? `${capitalise(describePlace(nearestPlace(f.latitude, f.longitude)))} · seen by satellite, not confirmed`
              : `${capitalise(describePlace(nearestPlace(f.latitude, f.longitude)))} · ${STATUS[f.status].label}`,
        },
      })),
    });
  }, [styleVersion, fires]);

  // Crosshair while choosing a place.
  useEffect(() => {
    const canvas = mapRef.current?.getCanvas();
    if (canvas) canvas.style.cursor = picking ? 'crosshair' : '';
  }, [picking, ready]);

  // The report's pin: draggable, so a rough tap can be corrected.
  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    if (!spot) {
      marker.current?.remove();
      marker.current = null;
      return;
    }
    if (!marker.current) {
      marker.current = new maplibregl.Marker({ color: REPORT_COLOR, draggable: true }).setLngLat([spot.longitude, spot.latitude]).addTo(map);
      marker.current.on('dragend', () => {
        const { lat, lng } = marker.current!.getLngLat();
        onSpotRef.current({ latitude: lat, longitude: lng });
      });
      map.easeTo({ center: [spot.longitude, spot.latitude], zoom: Math.max(map.getZoom(), 11), duration: 600 });
    } else {
      marker.current.setLngLat([spot.longitude, spot.latitude]);
    }
  }, [spot, ready]);

  return <div ref={container} className="map" role="region" aria-label="Map of fires in Greece" />;
}

function addLayers(map: MapLibreMap, theme: Theme) {
  const paint = PAINT[theme];
  map.addSource('fires', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
  map.addLayer({
    id: 'fires-detected',
    type: 'circle',
    source: 'fires',
    filter: ['==', ['get', 'official'], false],
    paint: {
      'circle-radius': 5,
      'circle-color': DETECTION_COLOR,
      'circle-opacity': 0.18,
      'circle-stroke-color': DETECTION_COLOR,
      'circle-stroke-width': 1.5,
      'circle-stroke-opacity': 0.75,
    },
  });
  map.addLayer({
    id: 'fires-official',
    type: 'circle',
    source: 'fires',
    filter: ['==', ['get', 'official'], true],
    paint: {
      'circle-radius': 8,
      'circle-color': ['get', 'color'],
      'circle-stroke-color': paint.outline,
      'circle-stroke-width': 2,
    },
  });
}

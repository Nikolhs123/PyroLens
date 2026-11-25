import type { FeatureCollection } from 'geojson';
import maplibregl, { type GeoJSONSource, type LngLatBoundsLike, type Map as MapLibreMap } from 'maplibre-gl';
import { useEffect, useRef, useState } from 'react';
import type { Theme } from './theme';

/**
 * Free vector tiles, no key (https://openfreemap.org). "liberty" is the bright, OpenStreetMap-like look;
 * "dark" goes with the dark theme.
 */
export const STYLES: Record<Theme, string> = {
  light: 'https://tiles.openfreemap.org/styles/liberty',
  dark: 'https://tiles.openfreemap.org/styles/dark',
};

/** Overlay colours that depend on the basemap underneath. */
export const PAINT: Record<Theme, { outline: string; selected: string; label: string; halo: string }> = {
  light: { outline: '#ffffff', selected: '#d98e00', label: '#7a4d00', halo: '#ffffff' },
  dark: { outline: '#0a0e11', selected: '#ffb000', label: '#ffb000', halo: '#0a0e11' },
};

export const GREECE: LngLatBoundsLike = [
  [19.4, 34.8],
  [28.3, 41.8],
];
export const PADDING = 24;
export const EMPTY: FeatureCollection = { type: 'FeatureCollection', features: [] };

/**
 * Our GeoJSON source, or a stand-in that ignores data while a style change is in progress (the new style has no
 * sources until 'style.load', and that handler bumps styleVersion so every effect pushes its data again).
 */
export const source = (map: MapLibreMap, id: string): Pick<GeoJSONSource, 'setData'> =>
  (map.getSource(id) as GeoJSONSource | undefined) ?? { setData: () => undefined as never };

/**
 * A MapLibre map of Greece that follows the theme. `addLayers` runs for the first style and again after every theme
 * switch (a new style has none of our layers); `onFirstLoad` runs once, e.g. to wire clicks on layer ids.
 * `styleVersion` changes each time the layers are re-added, so data effects can depend on it and push their data again.
 */
export function useBaseMap(
  theme: Theme,
  addLayers: (map: MapLibreMap, theme: Theme) => void,
  onFirstLoad?: (map: MapLibreMap) => void,
  attribution = 'Fires: NASA FIRMS · Weather: Open-Meteo',
) {
  const container = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MapLibreMap | null>(null);
  const themeRef = useRef(theme);
  themeRef.current = theme;
  const addLayersRef = useRef(addLayers);
  addLayersRef.current = addLayers;
  const onFirstLoadRef = useRef(onFirstLoad);
  onFirstLoadRef.current = onFirstLoad;
  const [styleVersion, setStyleVersion] = useState(0);

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
    map.addControl(new maplibregl.AttributionControl({ compact: true, customAttribution: attribution }), 'bottom-right');

    map.on('style.load', () => {
      addLayersRef.current(map, themeRef.current);
      setStyleVersion((v) => v + 1);
    });
    // Listeners bound to layer ids survive style changes, so these are added once.
    map.once('load', () => onFirstLoadRef.current?.(map));

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
    // Created once; the attribution text is fixed per map.
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

  return { container, mapRef, styleVersion, ready: styleVersion > 0 };
}

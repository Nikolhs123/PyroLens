import L from 'leaflet';
import { useEffect, useRef } from 'react';
import { CircleMarker, GeoJSON, MapContainer, TileLayer, Tooltip, useMap } from 'react-leaflet';
import type { Detection, Forecast, Incident } from '../api';
import { ratingStyle } from '../ratings';

/** Where satellites see fire now; forecasts use the danger rating colour instead. */
export const BURNING_COLOR = '#8a1c0c';

const GREECE: L.LatLngBoundsExpression = [
  [34.8, 19.4],
  [41.8, 28.3],
];

interface Props {
  incidents: Incident[];
  detections: Detection[] | null;
  selected: Incident | null;
  forecast: Forecast | null;
  onSelect: (id: number | null) => void;
}

/** Marker size grows with the number of satellite pixels, gently. */
const radiusFor = (incident: Incident) => 6 + 2.2 * Math.sqrt(incident.detectionCount);

export function MapView({ incidents, detections, selected, forecast, onSelect }: Props) {
  const oneHour = forecast?.horizons.find((h) => h.horizonHours === 1);
  const threeHours = forecast?.horizons.find((h) => h.horizonHours === 3);
  const forecastColor = selected ? ratingStyle(selected).color : '#cf3417';

  return (
    <MapContainer bounds={GREECE} className="map" zoomControl aria-label="Map of fire incidents in Greece">
      <TileLayer
        url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors · Fires: NASA FIRMS · Weather: Open-Meteo'
        maxZoom={18}
      />

      {threeHours && (
        <GeoJSON
          key={`3h-${forecast!.incidentId}-${forecast!.issuedAt}`}
          data={threeHours.spreadArea}
          style={{ color: forecastColor, weight: 2, dashArray: '6 6', fillColor: forecastColor, fillOpacity: 0.12 }}
          interactive={false}
        />
      )}
      {oneHour && (
        <GeoJSON
          key={`1h-${forecast!.incidentId}-${forecast!.issuedAt}`}
          data={oneHour.spreadArea}
          style={{ color: forecastColor, weight: 2, fillColor: forecastColor, fillOpacity: 0.3 }}
          interactive={false}
        />
      )}

      {incidents
        .filter((i) => i.footprint && i.footprint.type !== 'Point')
        .map((i) => (
          <GeoJSON
            key={`fp-${i.id}-${i.lastDetectedAt}`}
            data={i.footprint!}
            style={{ color: BURNING_COLOR, weight: 2, fillColor: BURNING_COLOR, fillOpacity: 0.45 }}
            interactive={false}
          />
        ))}

      {detections?.map((d) => (
        <CircleMarker
          key={`d-${d.id}`}
          center={[d.latitude, d.longitude]}
          radius={2.5}
          pathOptions={{ color: '#ff5a1f', weight: 0, fillOpacity: 0.85 }}
          interactive={false}
        />
      ))}

      {incidents.map((i) => {
        const style = ratingStyle(i);
        const isSelected = selected?.id === i.id;
        return (
          <CircleMarker
            key={`i-${i.id}`}
            center={[i.latitude, i.longitude]}
            radius={radiusFor(i)}
            pathOptions={{
              color: isSelected ? '#111111' : '#ffffff',
              weight: isSelected ? 3 : 1.5,
              fillColor: style.color,
              fillOpacity: 0.9,
            }}
            eventHandlers={{ click: () => onSelect(isSelected ? null : i.id) }}
          >
            <Tooltip direction="top" offset={[0, -radiusFor(i)]}>
              #{i.id} · {style.label} · {i.detectionCount} px
            </Tooltip>
          </CircleMarker>
        );
      })}

      <KeepSized />
      <FitToSelection selected={selected} forecast={forecast} />
    </MapContainer>
  );
}

/**
 * Leaflet measures its container once, when it is created. If the stylesheet arrives after the script
 * (common on a cold-started host), that measurement is 0×0 and the map is stuck at zoom 0 with every marker
 * on one pixel. Re-measure whenever the container changes size, and fit Greece once it has a real size.
 */
function KeepSized() {
  const map = useMap();

  useEffect(() => {
    const container = map.getContainer();
    const hasSize = () => container.clientWidth > 0 && container.clientHeight > 0;
    let fitted = hasSize();

    const observer = new ResizeObserver(() => {
      map.invalidateSize();
      if (!fitted && hasSize()) {
        map.fitBounds(GREECE);
        fitted = true;
      }
    });
    observer.observe(container);
    return () => observer.disconnect();
  }, [map]);

  return null;
}

/** Zooms to the selected incident (and its +3 h area once loaded); back to Greece when cleared. */
function FitToSelection({ selected, forecast }: { selected: Incident | null; forecast: Forecast | null }) {
  const map = useMap();
  const hadSelection = useRef(false);
  const forecastKey = forecast && selected && forecast.incidentId === selected.id ? forecast.issuedAt : null;

  useEffect(() => {
    // The container may have been resized by the layout since Leaflet measured it;
    // animating with a stale (zero) size produces NaN coordinates.
    map.invalidateSize();

    if (!selected) {
      // Only zoom back out after a selection is cleared; the initial view is set by MapContainer.
      if (hadSelection.current) map.flyToBounds(GREECE, { duration: 0.6 });
      hadSelection.current = false;
      return;
    }
    hadSelection.current = true;

    const threeHours = forecastKey ? forecast!.horizons.find((h) => h.horizonHours === 3) : undefined;
    const shape = threeHours?.spreadArea ?? selected.footprint;
    const bounds = shape ? L.geoJSON(shape).getBounds() : null;

    if (bounds && bounds.isValid()) {
      map.flyToBounds(bounds.pad(0.6), { maxZoom: 14, duration: 0.6 });
    } else {
      map.flyTo([selected.latitude, selected.longitude], 13, { duration: 0.6 });
    }
    // Re-fit when the selection or its forecast changes, not on every poll of the same data.
  }, [map, selected?.id, forecastKey]);

  return null;
}

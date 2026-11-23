import { type CSSProperties, useState } from 'react';
import { api } from './api';
import { IncidentDetail } from './components/IncidentDetail';
import { IncidentList } from './components/IncidentList';
import { BURNING_COLOR, MapView } from './components/MapView';
import { relativeTime } from './format';
import { ratingStyle } from './ratings';
import { usePolling } from './usePolling';

const REFRESH_MS = 60_000;

/** Legend and table keys use the same colours as the map layers. */
export const keyColors = (forecastColor: string) =>
  ({ '--key-forecast': forecastColor, '--key-now': BURNING_COLOR }) as CSSProperties;

export function App() {
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showPixels, setShowPixels] = useState(false);

  const incidents = usePolling((s) => api.incidents(s), REFRESH_MS, 'incidents');
  const status = usePolling((s) => api.status(s), REFRESH_MS, 'status');
  const detections = usePolling((s) => api.detections(s), REFRESH_MS, 'detections', showPixels);
  const forecast = usePolling((s) => api.forecast(selectedId!, s), REFRESH_MS, selectedId, selectedId != null);

  const list = incidents.data ?? [];
  const selected = list.find((i) => i.id === selectedId) ?? null;
  // undefined = still loading, null = none yet (or the request failed), otherwise this incident's forecast.
  const selectedForecast =
    forecast.error != null ? null : forecast.data === undefined ? undefined : forecast.data;

  return (
    <div className="app">
      <aside className="panel" aria-label="Incidents">
        <header className="brand">
          <h1>PyroLens</h1>
          <p className="status" aria-live="polite">
            <span className={incidents.error ? 'dot dot-error' : 'dot'} aria-hidden="true" />
            {incidents.error
              ? 'Cannot reach the API. Retrying every minute.'
              : status.data?.lastDetectionIngestedAt
                ? `${list.length} active · satellite data ${relativeTime(status.data.lastDetectionIngestedAt)}`
                : 'Loading…'}
          </p>
        </header>

        <label className="toggle">
          <input id="show-pixels" type="checkbox" checked={showPixels} onChange={(e) => setShowPixels(e.target.checked)} />
          Show satellite pixels (last 24 h)
        </label>

        <div className="panel-body">
          {selected ? (
            <IncidentDetail incident={selected} forecast={selectedForecast} onClose={() => setSelectedId(null)} />
          ) : incidents.data === undefined && !incidents.error ? (
            <p className="muted">Loading incidents…</p>
          ) : (
            <IncidentList incidents={list} onSelect={setSelectedId} />
          )}
        </div>

        <footer className="panel-footer">
          Demo project, not an emergency service. In an emergency call <strong>112</strong>.
        </footer>
      </aside>

      <main className="map-wrap">
        <MapView
          incidents={list}
          detections={showPixels ? (detections.data ?? []) : null}
          selected={selected}
          forecast={selected ? (selectedForecast ?? null) : null}
          onSelect={setSelectedId}
        />
        {selected && (
          <div className="map-legend" aria-hidden="true" style={keyColors(ratingStyle(selected).color)}>
            <span><i className="key key-now" /> burning now</span>
            <span><i className="key key-1h" /> could reach within 1 h</span>
            <span><i className="key key-3h" /> could reach within 3 h</span>
          </div>
        )}
      </main>
    </div>
  );
}

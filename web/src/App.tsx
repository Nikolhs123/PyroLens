import { type CSSProperties, useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { IncidentDetail } from './components/IncidentDetail';
import { IncidentTable } from './components/IncidentTable';
import { Legend } from './components/Legend';
import { MapView } from './components/MapView';
import { RatingTag } from './components/RatingTag';
import { StatusBar } from './components/StatusBar';
import { countByRating, ratingStyle } from './ratings';
import { usePolling } from './usePolling';

const REFRESH_MS = 60_000;

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
  const selectedForecast = forecast.error != null ? null : forecast.data;

  const toggle = useCallback((id: number) => setSelectedId((current) => (current === id ? null : id)), []);

  // Esc clears the selection; P toggles the pixel layer (outside text fields).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.key === 'Escape') setSelectedId(null);
      if (e.key === 'p' || e.key === 'P') setShowPixels((v) => !v);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const counts = countByRating(list);

  return (
    <div className="app">
      <StatusBar status={status.data ?? undefined} offline={incidents.error != null} active={list.length} />

      <aside className="console" aria-label="Incidents">
        <div className="console-head">
          <div className="bands" aria-label="Incidents by danger rating">
            {list.length > 0 ? (
              <>
                <div className="band-bar" aria-hidden="true">
                  {counts.map(({ style, count }) => (
                    <span key={style.code} style={{ flexGrow: count, background: style.color }} />
                  ))}
                </div>
                <ul className="band-counts">
                  {counts.map(({ style, count }) => (
                    <li key={style.code} title={style.label}>
                      <i style={{ background: style.color } as CSSProperties} />
                      {style.code} <strong>{count}</strong>
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <span className="dim">{incidents.data === undefined && !incidents.error ? 'LOADING…' : 'NO INCIDENTS'}</span>
            )}
          </div>
          <label className="switch" title="Toggle with P">
            <input id="show-pixels" type="checkbox" checked={showPixels} onChange={(e) => setShowPixels(e.target.checked)} />
            <span>PIXELS 24H</span>
            <kbd>P</kbd>
          </label>
        </div>

        {incidents.error && list.length === 0 ? (
          <div className="table-empty">
            <p>API UNREACHABLE</p>
            <p className="dim">Retrying every minute. A free server may take up to a minute to wake up.</p>
          </div>
        ) : (
          incidents.data !== undefined && (
            <IncidentTable incidents={list} selectedId={selectedId} onToggle={toggle} onMove={setSelectedId} />
          )
        )}

        {selected && <IncidentDetail incident={selected} forecast={selectedForecast} onClose={() => setSelectedId(null)} />}

        <footer className="console-foot">
          <span>
            <kbd>↑</kbd>
            <kbd>↓</kbd> select · <kbd>Esc</kbd> clear · <kbd>P</kbd> pixels
          </span>
          <span className="dim">Times in UTC</span>
        </footer>
      </aside>

      <main className="map-wrap">
        <MapView
          incidents={list}
          detections={showPixels ? (detections.data ?? []) : null}
          selected={selected}
          forecast={selected ? (selectedForecast ?? null) : null}
          onSelect={toggle}
        />
        {/* Top-left overlay, clear of the zoom buttons (top right) and the attribution (bottom right). */}
        <div className="map-overlay">
          <Legend selected={selected} />
          {selected && (
            <div className="map-tag" style={{ borderColor: ratingStyle(selected).color }}>
              INC {selected.id} · <RatingTag incident={selected} long />
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

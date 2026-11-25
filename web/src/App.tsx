import { useCallback, useEffect, useState } from 'react';
import { api } from './api';
import { FireDetail } from './components/FireDetail';
import { FireList } from './components/FireList';
import { Legend } from './components/Legend';
import { MapView } from './components/MapView';
import { SidebarHeader } from './components/SidebarHeader';
import { type Theme, applyTheme, initialTheme } from './theme';
import { usePolling } from './usePolling';

const REFRESH_MS = 60_000;
const OPS_REFRESH_MS = 15_000;
const KEY_STORAGE = 'pyrolens.operatorKey';

// sessionStorage can be unavailable (private mode, blocked storage); the app works without it.
const readKey = () => {
  try {
    return sessionStorage.getItem(KEY_STORAGE);
  } catch {
    return null;
  }
};
const writeKey = (key: string | null) => {
  try {
    if (key) sessionStorage.setItem(KEY_STORAGE, key);
    else sessionStorage.removeItem(KEY_STORAGE);
  } catch {
    // Kept in memory only.
  }
};

export function App() {
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [showPixels, setShowPixels] = useState(false);
  const [operatorKey, setOperatorKey] = useState<string | null>(readKey);
  const [theme, setTheme] = useState<Theme>(initialTheme);

  useEffect(() => applyTheme(theme), [theme]);

  const incidents = usePolling((s) => api.incidents(s), REFRESH_MS, 'incidents');
  const status = usePolling((s) => api.status(s), REFRESH_MS, 'status');
  const detections = usePolling((s) => api.detections(s), REFRESH_MS, 'detections', showPixels);
  const forecast = usePolling((s) => api.forecast(selectedId!, s), REFRESH_MS, selectedId, selectedId != null);
  const resources = usePolling((s) => api.resources(s), OPS_REFRESH_MS, 'resources');
  const airspace = usePolling((s) => api.airspace(s), OPS_REFRESH_MS, 'airspace');
  const timeline = usePolling((s) => api.timeline(selectedId!, s), OPS_REFRESH_MS, selectedId, selectedId != null);

  const list = incidents.data ?? [];
  const selected = list.find((i) => i.id === selectedId) ?? null;
  // undefined = still loading, null = none yet (or the request failed), otherwise this fire's forecast.
  const selectedForecast = forecast.error != null ? null : forecast.data;

  const toggle = useCallback((id: number) => setSelectedId((current) => (current === id ? null : id)), []);

  const unlock = (key: string) => {
    writeKey(key);
    setOperatorKey(key);
  };
  const lock = () => {
    writeKey(null);
    setOperatorKey(null);
  };

  // After a dispatcher action, everything it can touch is fetched again at once.
  const refreshAfterAction = () => {
    incidents.reload();
    resources.reload();
    airspace.reload();
    timeline.reload();
  };

  // Esc goes back to the list; P toggles the pixel layer (outside form fields).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      if (target?.closest('input, select, textarea') || e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.key === 'Escape') setSelectedId(null);
      if (e.key === 'p' || e.key === 'P') setShowPixels((v) => !v);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const liveAirspace = airspace.data ?? [];

  return (
    <div className="app">
      <aside className="sidebar" aria-label="Fires">
        <SidebarHeader
          status={status.data ?? undefined}
          offline={incidents.error != null && list.length === 0}
          fires={list.length}
          theme={theme}
          onToggleTheme={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
          operatorKey={operatorKey}
          onUnlock={unlock}
          onLock={lock}
        />

        <div className="sb-body">
          {selected ? (
            <FireDetail
              incident={selected}
              forecast={selectedForecast}
              resources={resources.data ?? []}
              airspace={liveAirspace}
              timeline={timeline.data}
              operatorKey={operatorKey}
              onChanged={refreshAfterAction}
              onBack={() => setSelectedId(null)}
            />
          ) : incidents.data === undefined ? (
            <p className="hint padded">{incidents.error ? 'Waiting for the server. A free server can take up to a minute to wake up.' : 'Loading fires…'}</p>
          ) : (
            <FireList incidents={list} onOpen={setSelectedId} />
          )}
        </div>

        <footer className="sb-foot">Demo project, not an emergency service. In an emergency call 112.</footer>
      </aside>

      <main className="map-wrap">
        <MapView
          incidents={list}
          detections={showPixels ? (detections.data ?? []) : null}
          selected={selected}
          forecast={selected ? (selectedForecast ?? null) : null}
          resources={resources.data ?? []}
          airspace={liveAirspace}
          theme={theme}
          onSelect={toggle}
        />
        <div className="map-overlay">
          <Legend selected={selected} showAirspace={liveAirspace.length > 0} showPixels={showPixels} onTogglePixels={setShowPixels} />
        </div>
      </main>
    </div>
  );
}

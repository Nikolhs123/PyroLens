import { useEffect, useState } from 'react';
import { type IncidentStatus, type PublicFire, api } from '../api';
import { Icon } from '../components/Icon';
import { relativeTime } from '../format';
import { STATUS } from '../operations';
import { describePlace, nearestPlace } from '../places';
import { RATINGS } from '../ratings';
import { REPORT_COLOR } from '../reports';
import { type Theme, applyTheme, initialTheme } from '../theme';
import { usePolling } from '../usePolling';
import { DETECTION_COLOR, PublicMap, type Spot } from './PublicMap';
import { ReportForm } from './ReportForm';

const REFRESH_MS = 60_000;
const ORDER: IncidentStatus[] = ['active', 'confirmed', 'contained'];
const PUBLIC_WORD: Partial<Record<IncidentStatus, string>> = {
  active: 'Being fought',
  confirmed: 'Confirmed',
  contained: 'Contained',
};
const capitalise = (text: string) => text.charAt(0).toUpperCase() + text.slice(1);

/** The page for the public: what is burning, and a way to report what you see. */
export function PublicApp() {
  const [theme, setTheme] = useState<Theme>(initialTheme);
  const [reporting, setReporting] = useState(false);
  const [spot, setSpot] = useState<Spot | null>(null);
  useEffect(() => applyTheme(theme), [theme]);
  useEffect(() => {
    document.title = 'PyroLens · Fires in Greece';
  }, []);

  const fires = usePolling((s) => api.publicFires(s), REFRESH_MS, 'public-fires');
  const list = fires.data ?? [];
  const official = list
    .filter((f) => f.status !== 'detected')
    .sort((a, b) => ORDER.indexOf(a.status) - ORDER.indexOf(b.status) || Date.parse(b.lastSeenAt) - Date.parse(a.lastSeenAt));
  const detections = list.length - official.length;

  const closeReport = () => {
    setReporting(false);
    setSpot(null);
  };

  return (
    <div className="app public">
      <aside className="sidebar" aria-label="Fires in Greece">
        <header className="sb-head">
          <div className="brand-row">
            <span className="logo" aria-hidden="true">
              <Icon name="flame" size={22} />
            </span>
            <h1>PyroLens</h1>
            <button
              type="button"
              className="icon-button"
              onClick={() => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))}
              aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
              title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
            >
              <Icon name={theme === 'dark' ? 'sun' : 'moon'} />
            </button>
          </div>
          <p className="lede">
            {fires.data === undefined
              ? 'Loading fires…'
              : official.length === 0
                ? 'No fires confirmed by dispatchers right now'
                : `${official.length} ${official.length === 1 ? 'fire' : 'fires'} being dealt with in Greece`}
          </p>
          <p className="fresh">Updated every minute. A demo project, not an official source.</p>
        </header>

        <div className="sb-body">
          {reporting ? (
            <ReportForm spot={spot} onSpot={setSpot} onClose={closeReport} />
          ) : (
            <div className="public-body">
              <section className="report-call">
                <p>Can you see smoke or flames?</p>
                <button type="button" className="button primary big" onClick={() => setReporting(true)}>
                  Report a fire
                </button>
                <p className="hint">If anyone is in danger, call 112 first.</p>
              </section>

              <section aria-labelledby="official-title">
                <h2 id="official-title" className="public-h2">
                  Fires the dispatchers are handling
                </h2>
                {official.length === 0 ? (
                  <p className="hint">None right now.</p>
                ) : (
                  <ul className="public-list">
                    {official.map((f) => (
                      <PublicFireRow key={f.id} fire={f} />
                    ))}
                  </ul>
                )}
              </section>

              <section aria-labelledby="detections-title">
                <h2 id="detections-title" className="public-h2">
                  Seen by satellite, not confirmed
                </h2>
                <p className="hint">
                  {detections === 1 ? '1 hot spot' : `${detections} hot spots`} in the last two days. Many are farm burning or
                  industry; dispatchers check each one.
                </p>
              </section>
            </div>
          )}
        </div>

        <footer className="sb-foot">
          In an emergency call 112. <a href="/">Dispatcher view</a>
        </footer>
      </aside>

      <main className="map-wrap">
        <PublicMap fires={list} theme={theme} picking={reporting} spot={spot} onSpot={setSpot} />
        <div className="map-overlay">
          <div className="legend" aria-label="Map key">
            <ul className="legend-row">
              {ORDER.map((s) => (
                <li key={s}>
                  <i className="legend-dot" style={{ background: STATUS[s].color }} /> {PUBLIC_WORD[s]}
                </li>
              ))}
              <li>
                <i className="legend-ring solid" style={{ borderColor: DETECTION_COLOR }} /> Satellite, not confirmed
              </li>
              {reporting && (
                <li>
                  <i className="legend-dot" style={{ background: REPORT_COLOR }} /> Your report
                </li>
              )}
            </ul>
          </div>
        </div>
      </main>
    </div>
  );
}

function PublicFireRow({ fire: f }: { fire: PublicFire }) {
  const place = capitalise(describePlace(nearestPlace(f.latitude, f.longitude)));
  return (
    <li className="public-fire">
      <span className="fire-dot" style={{ background: STATUS[f.status].color }} aria-hidden="true" />
      <span className="fire-main">
        <span className="fire-title">{place}</span>
        <span className="fire-sub">
          {PUBLIC_WORD[f.status]}
          {f.dangerRating && <> · {RATINGS[f.dangerRating].short.toLowerCase()} fire danger</>} · last seen {relativeTime(f.lastSeenAt)}
        </span>
      </span>
    </li>
  );
}

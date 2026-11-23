import type { Incident } from '../api';
import { formatCoordinates, relativeTime } from '../format';
import { countByRating, ratingStyle, sortIncidents } from '../ratings';
import { RatingPill } from './RatingPill';

interface Props {
  incidents: Incident[];
  onSelect: (id: number) => void;
}

export function IncidentList({ incidents, onSelect }: Props) {
  if (incidents.length === 0) {
    return (
      <div className="empty">
        <p className="empty-title">No active incidents</p>
        <p>Nothing has burned in Greece in the last 48 hours, or the worker has not run yet. The map refreshes every minute.</p>
      </div>
    );
  }

  const sorted = sortIncidents(incidents);

  return (
    <>
      <ul className="rating-summary" aria-label="Incidents by danger rating">
        {countByRating(incidents).map(({ style, count }) => (
          <li key={style.label}>
            <span className="swatch" style={{ background: style.color }} aria-hidden="true" />
            {style.label} <strong>{count}</strong>
          </li>
        ))}
      </ul>

      <ol className="incident-list">
        {sorted.map((i) => (
          <li key={i.id}>
            <button type="button" className="incident-row" onClick={() => onSelect(i.id)}>
              <span className="incident-stripe" style={{ background: ratingStyle(i).color }} aria-hidden="true" />
              <span className="incident-main">
                <span className="incident-title">
                  Incident #{i.id}
                  <RatingPill incident={i} small />
                </span>
                <span className="incident-meta">
                  {i.detectionCount} {i.detectionCount === 1 ? 'pixel' : 'pixels'} · last seen {relativeTime(i.lastDetectedAt)}
                  {i.maxFrpMegawatts != null && <> · {Math.round(i.maxFrpMegawatts)} MW</>}
                </span>
                <span className="incident-coords">{formatCoordinates(i.latitude, i.longitude)}</span>
              </span>
            </button>
          </li>
        ))}
      </ol>
    </>
  );
}

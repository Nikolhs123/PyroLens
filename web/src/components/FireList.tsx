import type { Incident, IncidentStatus } from '../api';
import { relativeTime } from '../format';
import { sortIncidents } from '../incidents';
import { STATUS } from '../operations';
import { nearestPlace } from '../places';
import { ratingStyle } from '../ratings';

const GROUP_ORDER: IncidentStatus[] = ['active', 'confirmed', 'contained', 'detected'];

interface Props {
  incidents: Incident[];
  onOpen: (id: number) => void;
}

/** The board: fires grouped by what is happening to them, most dangerous first within each group. */
export function FireList({ incidents, onOpen }: Props) {
  if (incidents.length === 0) {
    return (
      <div className="empty">
        <p>No fires on the board.</p>
        <p className="hint">Nothing has been detected in Greece in the last two days, or the first satellite check has not run yet.</p>
      </div>
    );
  }

  const now = Date.now();

  return (
    <div className="fire-list">
      {GROUP_ORDER.map((status) => {
        const fires = sortIncidents(incidents.filter((i) => i.status === status));
        if (fires.length === 0) return null;
        return (
          <section key={status} className="group" aria-labelledby={`group-${status}`}>
            <h2 id={`group-${status}`}>
              <span className="group-mark" style={{ background: STATUS[status].color }} aria-hidden="true" />
              {STATUS[status].group}
              <span className="group-count">{fires.length}</span>
            </h2>
            <ul>
              {fires.map((i) => {
                const place = nearestPlace(i.latitude, i.longitude);
                const danger = ratingStyle(i);
                return (
                  <li key={i.id}>
                    <button type="button" className="fire-row" onClick={() => onOpen(i.id)}>
                      <span className="fire-dot" style={{ background: danger.color }} aria-hidden="true" />
                      <span className="fire-main">
                        <span className="fire-title">{place.town}</span>
                        <span className="fire-sub">
                          {place.distanceKm < 3 ? 'In the area' : `${Math.round(place.distanceKm)} km ${place.direction}`} ·{' '}
                          {i.detectionCount} {i.detectionCount === 1 ? 'pixel' : 'pixels'} · {relativeTime(i.lastDetectedAt, now)}
                        </span>
                      </span>
                      <span className="fire-danger" title={`Fire danger: ${danger.label}`}>
                        {danger.short}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>
        );
      })}
    </div>
  );
}

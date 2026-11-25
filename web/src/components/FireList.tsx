import type { ReactNode } from 'react';
import type { Incident, IncidentStatus } from '../api';
import { ATTENTION_COLORS, attentionLevel, describeAttention, sortAttention } from '../attention';
import { relativeTime } from '../format';
import { sortIncidents } from '../incidents';
import { STATUS } from '../operations';
import { nearestPlace } from '../places';
import { ratingStyle } from '../ratings';
import { useNow } from '../usePolling';

const GROUP_ORDER: IncidentStatus[] = ['active', 'confirmed', 'contained', 'detected'];

interface Props {
  incidents: Incident[];
  onOpen: (id: number) => void;
}

/**
 * The board: first the fires that need attention (escalation rules), then the rest grouped by what is happening to
 * them, most dangerous first within each group. A fire appears once.
 */
export function FireList({ incidents, onOpen }: Props) {
  // Ticks so "No units sent for 34 min" counts up between refreshes.
  const now = useNow(30_000);

  if (incidents.length === 0) {
    return (
      <div className="empty">
        <p>No fires on the board.</p>
        <p className="hint">Nothing has been detected in Greece in the last two days, or the first satellite check has not run yet.</p>
      </div>
    );
  }

  // Urgent before watch; Array.sort is stable, so the danger order from sortIncidents holds within each level.
  const flagged = sortIncidents(incidents.filter((i) => attentionLevel(i) != null)).sort(
    (a, b) => Number(attentionLevel(b) === 'urgent') - Number(attentionLevel(a) === 'urgent'),
  );
  const rest = incidents.filter((i) => attentionLevel(i) == null);

  return (
    <div className="fire-list">
      {flagged.length > 0 && (
        <Group id="attention" title="Needs attention" color={ATTENTION_COLORS.urgent} count={flagged.length}>
          {flagged.map((i) => (
            <FireRow key={i.id} incident={i} now={now} onOpen={onOpen} withReason />
          ))}
        </Group>
      )}
      {GROUP_ORDER.map((status) => {
        const fires = sortIncidents(rest.filter((i) => i.status === status));
        if (fires.length === 0) return null;
        return (
          <Group key={status} id={status} title={STATUS[status].group} color={STATUS[status].color} count={fires.length}>
            {fires.map((i) => (
              <FireRow key={i.id} incident={i} now={now} onOpen={onOpen} />
            ))}
          </Group>
        );
      })}
    </div>
  );
}

function Group({ id, title, color, count, children }: { id: string; title: string; color: string; count: number; children: ReactNode }) {
  return (
    <section className={`group group-${id}`} aria-labelledby={`group-${id}`}>
      <h2 id={`group-${id}`}>
        <span className="group-mark" style={{ background: color }} aria-hidden="true" />
        {title}
        <span className="group-count">{count}</span>
      </h2>
      <ul>{children}</ul>
    </section>
  );
}

function FireRow({ incident: i, now, onOpen, withReason = false }: { incident: Incident; now: number; onOpen: (id: number) => void; withReason?: boolean }) {
  const place = nearestPlace(i.latitude, i.longitude);
  const danger = ratingStyle(i);
  const level = attentionLevel(i);
  const reasons = sortAttention(i.attention ?? []);

  return (
    <li>
      <button
        type="button"
        className={withReason && level ? `fire-row flagged ${level}` : 'fire-row'}
        onClick={() => onOpen(i.id)}
      >
        <span className="fire-dot" style={{ background: danger.color }} aria-hidden="true" />
        <span className="fire-main">
          <span className="fire-title">{place.town}</span>
          {withReason && reasons.length > 0 ? (
            <span className="fire-reason">
              {describeAttention(reasons[0], now)}
              {reasons.length > 1 && <span className="fire-more"> · {reasons.length - 1} more</span>}
              <span className="fire-sub"> · {STATUS[i.status].label}</span>
            </span>
          ) : (
            <span className="fire-sub">
              {place.distanceKm < 3 ? 'In the area' : `${Math.round(place.distanceKm)} km ${place.direction}`} ·{' '}
              {i.detectionCount} {i.detectionCount === 1 ? 'pixel' : 'pixels'} · {relativeTime(i.lastDetectedAt, now)}
            </span>
          )}
        </span>
        <span className="fire-danger" title={`Fire danger: ${danger.label}`}>
          {danger.short}
        </span>
      </button>
    </li>
  );
}

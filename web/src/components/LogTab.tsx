import type { TimelineEvent } from '../api';
import { utcStamp } from '../format';
import { describeEvent } from '../operations';

/** The incident's append-only timeline, newest first. */
export function LogTab({ events }: { events: TimelineEvent[] | undefined }) {
  if (events === undefined) return <p className="dim">Loading…</p>;

  return (
    <div className="tab-body">
      <ol className="log">
        {[...events].reverse().map((e) => (
          <li key={e.id}>
            <time>{utcStamp(e.occurredAt)}</time>
            <span className="log-what">
              {describeEvent(e)}
              {e.note && <span className="log-note"> “{e.note}”</span>}
            </span>
            <span className="log-who">{e.actor}</span>
          </li>
        ))}
      </ol>
      <p className="fine">Append-only: the database rejects any change to past entries.</p>
    </div>
  );
}

import type { TimelineEvent } from '../api';
import { localStamp } from '../format';
import { describeEvent } from '../operations';

/** The fire's history, newest first. */
export function LogTab({ events }: { events: TimelineEvent[] | undefined }) {
  if (events === undefined) return <p className="hint">Loading…</p>;

  return (
    <div className="stack">
      <ol className="log">
        {[...events].reverse().map((e) => (
          <li key={e.id}>
            <time dateTime={e.occurredAt}>{localStamp(e.occurredAt)}</time>
            <p>
              {describeEvent(e)}
              {e.note && <span className="log-note"> “{e.note}”</span>}
            </p>
          </li>
        ))}
      </ol>
      <p className="hint">Entries cannot be edited or deleted afterwards; the database refuses it.</p>
    </div>
  );
}

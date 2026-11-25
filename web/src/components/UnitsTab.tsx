import { useState } from 'react';
import { type Incident, type Resource, actions, api } from '../api';
import { KIND, countdown } from '../operations';
import { useNow, usePolling } from '../usePolling';
import { KindIcon } from './Icon';

interface Props {
  incident: Incident;
  resources: Resource[];
  operatorKey: string | null;
  /** Report the outcome of an action; shared data is refreshed after it. */
  onResult: (ok: boolean, message: string) => void;
}

export function UnitsTab({ incident, resources, operatorKey, onResult }: Props) {
  const now = useNow(1000);
  const [busy, setBusy] = useState<number | null>(null);
  const suggestions = usePolling((s) => api.suggestions(incident.id, s), 30_000, incident.id);

  const onFire = resources.filter((r) => r.incidentId === incident.id && (r.status === 'reserved' || r.status === 'assigned'));

  const run = async (resourceId: number, done: string, action: () => ReturnType<typeof actions.reserve>) => {
    setBusy(resourceId);
    const result = await action();
    setBusy(null);
    suggestions.reload();
    onResult(result.ok, result.ok ? `${result.value.callsign} ${done}.` : result.message);
  };

  return (
    <div className="stack">
      <section>
        <h3>On this fire</h3>
        {onFire.length === 0 ? (
          <p className="hint">No units yet.</p>
        ) : (
          <ul className="units">
            {onFire.map((r) => {
              const left = r.reservedUntil ? countdown(r.reservedUntil, now) : null;
              return (
                <li key={r.id}>
                  <KindIcon kind={r.kind} />
                  <span className="unit-main">
                    <span className="unit-name">
                      <span className="callsign">{r.callsign}</span> {KIND[r.kind].label}
                    </span>
                    <span className={r.status === 'assigned' ? 'unit-state sent' : 'unit-state held'}>
                      {r.status === 'assigned' ? `Sent from ${r.baseName}` : left === 'expired' ? 'Hold ran out' : `On hold, ${left} left to send`}
                    </span>
                  </span>
                  {operatorKey && (
                    <span className="unit-actions">
                      {r.status === 'reserved' && left !== 'expired' && (
                        <button
                          type="button"
                          className="button primary small"
                          disabled={busy === r.id}
                          onClick={() => run(r.id, 'is on its way', () => actions.assign(operatorKey, r.id, incident.id))}
                        >
                          Send
                        </button>
                      )}
                      <button
                        type="button"
                        className="button small"
                        disabled={busy === r.id}
                        onClick={() => run(r.id, 'released', () => actions.release(operatorKey, r.id, incident.id))}
                      >
                        Release
                      </button>
                    </span>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <section>
        <h3>Closest available</h3>
        {operatorKey && <p className="hint">Hold a unit for 5 minutes while you decide, then send it.</p>}
        {!incident.acceptsResources && <p className="hint">Confirm the fire before sending units.</p>}
        {suggestions.data === undefined ? (
          <p className="hint">Looking for units…</p>
        ) : (
          <ul className="units">
            {suggestions.data.map(({ resource: r, distanceMeters, etaMinutes }) => (
              <li key={r.id}>
                <KindIcon kind={r.kind} />
                <span className="unit-main">
                  <span className="unit-name">
                    <span className="callsign">{r.callsign}</span> {KIND[r.kind].label}
                  </span>
                  <span className="unit-state">
                    {r.baseName}, {Math.round(distanceMeters / 1000)} km away, about {etaMinutes} min
                  </span>
                </span>
                {operatorKey && (
                  <span className="unit-actions">
                    <button
                      type="button"
                      className="button small"
                      disabled={!incident.acceptsResources || busy === r.id}
                      onClick={() => run(r.id, 'is on hold for 5 minutes', () => actions.reserve(operatorKey, r.id, incident.id))}
                    >
                      Hold
                    </button>
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      <p className="hint">The fleet is simulated, at real Greek bases. Travel times use a straight line for aircraft and roads (×1.3) for vehicles.</p>
    </div>
  );
}

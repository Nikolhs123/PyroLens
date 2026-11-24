import { useState } from 'react';
import { type Incident, type Resource, actions, api } from '../api';
import { KIND, countdown } from '../operations';
import { useNow, usePolling } from '../usePolling';

interface Props {
  incident: Incident;
  resources: Resource[];
  operatorKey: string | null;
  /** Report the outcome of an action; refresh shared data after it. */
  onResult: (ok: boolean, message: string) => void;
}

export function UnitsTab({ incident, resources, operatorKey, onResult }: Props) {
  const now = useNow(1000);
  const [busy, setBusy] = useState<number | null>(null);
  const suggestions = usePolling((s) => api.suggestions(incident.id, s), 30_000, incident.id);

  const committed = resources.filter((r) => r.incidentId === incident.id && (r.status === 'reserved' || r.status === 'assigned'));

  const run = async (resourceId: number, verb: string, action: () => ReturnType<typeof actions.reserve>) => {
    setBusy(resourceId);
    const result = await action();
    setBusy(null);
    suggestions.reload();
    onResult(result.ok, result.ok ? `${result.value.callsign} ${verb}.` : result.message);
  };

  return (
    <div className="tab-body">
      <h3 className="rule">
        <span>COMMITTED</span>
        <span className="dim">{committed.length}</span>
      </h3>
      {committed.length === 0 ? (
        <p className="dim">No units on this incident.</p>
      ) : (
        <table className="grid">
          <tbody>
            {committed.map((r) => {
              const expiresIn = r.reservedUntil ? countdown(r.reservedUntil, now) : null;
              return (
                <tr key={r.id}>
                  <th scope="row">{r.callsign}</th>
                  <td className="dim">{KIND[r.kind].label}</td>
                  <td>
                    {r.status === 'assigned' ? (
                      <span className="unit-state assigned">ASSIGNED</span>
                    ) : (
                      <span className="unit-state reserved" title="Hold expires; assign it before then">
                        HOLD {expiresIn}
                      </span>
                    )}
                  </td>
                  <td className="num actions">
                    {operatorKey && r.status === 'reserved' && expiresIn !== 'expired' && (
                      <button
                        type="button"
                        className="btn btn-primary"
                        disabled={busy === r.id}
                        onClick={() => run(r.id, 'assigned', () => actions.assign(operatorKey, r.id, incident.id))}
                      >
                        ASSIGN
                      </button>
                    )}
                    {operatorKey && (
                      <button
                        type="button"
                        className="btn"
                        disabled={busy === r.id}
                        onClick={() => run(r.id, 'released', () => actions.release(operatorKey, r.id, incident.id))}
                      >
                        RELEASE
                      </button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}

      <h3 className="rule">
        <span>NEAREST AVAILABLE</span>
        <span className="dim">by ETA</span>
      </h3>
      {!incident.acceptsResources && <p className="dim">Confirm the incident to commit units to it.</p>}
      {suggestions.data === undefined ? (
        <p className="dim">Loading…</p>
      ) : (
        <table className="grid">
          <thead>
            <tr>
              <th scope="col">UNIT</th>
              <th scope="col">BASE</th>
              <th scope="col" className="num">DIST</th>
              <th scope="col" className="num">ETA</th>
              {operatorKey && <th scope="col" />}
            </tr>
          </thead>
          <tbody>
            {suggestions.data.map(({ resource: r, distanceMeters, etaMinutes }) => (
              <tr key={r.id}>
                <th scope="row" title={KIND[r.kind].label}>
                  {r.callsign}
                </th>
                <td className="dim">{r.baseName}</td>
                <td className="num">{Math.round(distanceMeters / 1000)} km</td>
                <td className="num">{etaMinutes}′</td>
                {operatorKey && (
                  <td className="num actions">
                    <button
                      type="button"
                      className="btn"
                      disabled={!incident.acceptsResources || busy === r.id}
                      onClick={() => run(r.id, 'reserved for 5 min', () => actions.reserve(operatorKey, r.id, incident.id))}
                    >
                      RESERVE
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p className="fine">Simulated fleet at real Greek bases. ETA: straight line for aircraft, ×1.3 by road for ground units.</p>
    </div>
  );
}

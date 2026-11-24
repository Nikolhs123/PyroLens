import { type FormEvent, useState } from 'react';
import { type AirspaceLevel, type AirspaceReservation, type Incident, actions } from '../api';
import { utcStamp } from '../format';

export const AIRSPACE_COLORS: Record<AirspaceLevel, string> = { low: '#3fc1d6', high: '#a78bfa' };

interface Props {
  incident: Incident;
  reservations: AirspaceReservation[];
  operatorKey: string | null;
  onResult: (ok: boolean, message: string) => void;
}

export function AirspaceTab({ incident, reservations, operatorKey, onResult }: Props) {
  const [level, setLevel] = useState<AirspaceLevel>('high');
  const [radiusKm, setRadiusKm] = useState(3);
  const [minutes, setMinutes] = useState(120);
  const [busy, setBusy] = useState(false);

  const mine = reservations.filter((r) => r.incidentId === incident.id);

  const reserve = async (e: FormEvent) => {
    e.preventDefault();
    if (!operatorKey) return;
    setBusy(true);
    const result = await actions.reserveAirspace(operatorKey, incident.id, level, radiusKm * 1000, minutes);
    setBusy(false);
    onResult(result.ok, result.ok ? `${level.toUpperCase()} airspace reserved, ${radiusKm} km.` : result.message);
  };

  const cancel = async (id: number) => {
    if (!operatorKey) return;
    const result = await actions.cancelAirspace(operatorKey, id);
    onResult(result.ok, result.ok ? `Airspace reservation ${id} cancelled.` : result.message);
  };

  return (
    <div className="tab-body">
      <h3 className="rule">
        <span>RESERVED</span>
        <span className="dim">{mine.length}</span>
      </h3>
      {mine.length === 0 ? (
        <p className="dim">No airspace reserved for this incident.</p>
      ) : (
        <table className="grid">
          <tbody>
            {mine.map((r) => (
              <tr key={r.id}>
                <th scope="row">
                  <span className="lvl" style={{ borderColor: AIRSPACE_COLORS[r.level], color: AIRSPACE_COLORS[r.level] }}>
                    {r.level.toUpperCase()}
                  </span>
                </th>
                <td className="num">{r.radiusMeters / 1000} km</td>
                <td>
                  {utcStamp(r.startsAt)} → {utcStamp(r.endsAt).split(' ').at(-1)}
                </td>
                <td className="num actions">
                  {operatorKey && (
                    <button type="button" className="btn" onClick={() => cancel(r.id)}>
                      CANCEL
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {operatorKey && (
        <form className="air-form" onSubmit={reserve}>
          <h3 className="rule">
            <span>NEW RESERVATION</span>
          </h3>
          {!incident.acceptsResources && <p className="dim">Confirm the incident to reserve airspace for it.</p>}
          <div className="air-fields">
            <label>
              BAND
              <select value={level} onChange={(e) => setLevel(e.target.value as AirspaceLevel)}>
                <option value="high">HIGH · aircraft</option>
                <option value="low">LOW · drones</option>
              </select>
            </label>
            <label>
              RADIUS
              <select value={radiusKm} onChange={(e) => setRadiusKm(Number(e.target.value))}>
                {[1, 2, 3, 5, 10].map((km) => (
                  <option key={km} value={km}>
                    {km} km
                  </option>
                ))}
              </select>
            </label>
            <label>
              FROM NOW FOR
              <select value={minutes} onChange={(e) => setMinutes(Number(e.target.value))}>
                {[30, 60, 120, 240, 480].map((m) => (
                  <option key={m} value={m}>
                    {m < 60 ? `${m} min` : `${m / 60} h`}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit" className="btn btn-primary" disabled={busy || !incident.acceptsResources}>
              RESERVE
            </button>
          </div>
        </form>
      )}
      <p className="fine">
        Two reservations in the same band may not overlap in space and time anywhere. PostgreSQL enforces this with an
        exclusion constraint, so simultaneous requests cannot both succeed.
      </p>
    </div>
  );
}

import { type FormEvent, useState } from 'react';
import { type AirspaceLevel, type AirspaceReservation, type Incident, actions } from '../api';
import { localStamp } from '../format';

export const AIRSPACE_COLORS: Record<AirspaceLevel, string> = { low: '#1aa3b8', high: '#8b6be8' };
const LEVEL_NAME: Record<AirspaceLevel, string> = { low: 'Low airspace (drones)', high: 'High airspace (aircraft)' };

interface Props {
  incident: Incident;
  reservations: AirspaceReservation[];
  operatorKey: string | null;
  onResult: (ok: boolean, message: string) => void;
}

const clock = (iso: string) => localStamp(iso).split(', ')[1];

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
    onResult(result.ok, result.ok ? `${LEVEL_NAME[level]} reserved, ${radiusKm} km around the fire.` : result.message);
  };

  const cancel = async (id: number) => {
    if (!operatorKey) return;
    const result = await actions.cancelAirspace(operatorKey, id);
    onResult(result.ok, result.ok ? 'Reservation cancelled.' : result.message);
  };

  return (
    <div className="stack">
      <section>
        <h3>Reserved for this fire</h3>
        {mine.length === 0 ? (
          <p className="hint">Nothing reserved.</p>
        ) : (
          <ul className="reservations">
            {mine.map((r) => (
              <li key={r.id}>
                <span className="ring" style={{ borderColor: AIRSPACE_COLORS[r.level] }} aria-hidden="true" />
                <span className="unit-main">
                  <span className="unit-name">{LEVEL_NAME[r.level]}</span>
                  <span className="unit-state">
                    {r.radiusMeters / 1000} km around the fire, {clock(r.startsAt)} to {clock(r.endsAt)}, by {r.reservedBy}
                  </span>
                </span>
                {operatorKey && (
                  <button type="button" className="button small" onClick={() => cancel(r.id)}>
                    Cancel
                  </button>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>

      {operatorKey && (
        <form className="sentence-form" onSubmit={reserve}>
          <h3>Reserve airspace</h3>
          {!incident.acceptsResources && <p className="hint">Confirm the fire before reserving airspace.</p>}
          <p>
            Keep{' '}
            <select aria-label="Airspace band" value={level} onChange={(e) => setLevel(e.target.value as AirspaceLevel)}>
              <option value="high">high airspace (aircraft)</option>
              <option value="low">low airspace (drones)</option>
            </select>{' '}
            clear within{' '}
            <select aria-label="Radius" value={radiusKm} onChange={(e) => setRadiusKm(Number(e.target.value))}>
              {[1, 2, 3, 5, 10].map((km) => (
                <option key={km} value={km}>
                  {km} km
                </option>
              ))}
            </select>{' '}
            for{' '}
            <select aria-label="Duration" value={minutes} onChange={(e) => setMinutes(Number(e.target.value))}>
              {[30, 60, 120, 240, 480].map((m) => (
                <option key={m} value={m}>
                  {m < 60 ? `${m} minutes` : m === 60 ? '1 hour' : `${m / 60} hours`}
                </option>
              ))}
            </select>{' '}
            from now.
          </p>
          <button type="submit" className="button primary" disabled={busy || !incident.acceptsResources}>
            Reserve
          </button>
        </form>
      )}

      <p className="hint">
        Two reservations in the same band can never overlap in place and time. The database enforces this, so two
        dispatchers asking at the same moment cannot both get it.
      </p>
    </div>
  );
}

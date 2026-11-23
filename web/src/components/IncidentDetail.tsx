import type { CSSProperties } from 'react';
import type { Forecast, Incident } from '../api';
import { clockTime, compass, formatCoordinates, formatDistance, relativeTime } from '../format';
import { RATINGS } from '../ratings';
import { RatingPill } from './RatingPill';

interface Props {
  incident: Incident;
  forecast: Forecast | null | undefined;
  onClose: () => void;
}

export function IncidentDetail({ incident, forecast, onClose }: Props) {
  const threeHours = forecast?.horizons.find((h) => h.horizonHours === 3);
  const weather = forecast?.horizons.find((h) => h.horizonHours === 1)?.weather;

  return (
    <article className="detail" aria-labelledby="detail-title">
      <button type="button" className="back" onClick={onClose}>
        ← All incidents
      </button>

      <header className="detail-header">
        <h2 id="detail-title">Incident #{incident.id}</h2>
        <p className="muted">{formatCoordinates(incident.latitude, incident.longitude)}</p>
      </header>

      <dl className="facts">
        <div>
          <dt>Pixels</dt>
          <dd>{incident.detectionCount}</dd>
        </div>
        <div>
          <dt>Strongest pixel</dt>
          <dd>{incident.maxFrpMegawatts != null ? `${incident.maxFrpMegawatts.toFixed(1)} MW` : '–'}</dd>
        </div>
        <div>
          <dt>First seen</dt>
          <dd>{relativeTime(incident.firstDetectedAt)}</dd>
        </div>
        <div>
          <dt>Last seen</dt>
          <dd>{relativeTime(incident.lastDetectedAt)}</dd>
        </div>
      </dl>

      <section className="forecast" aria-labelledby="forecast-title">
        <h3 id="forecast-title">Spread forecast</h3>

        {forecast === undefined && <p className="muted">Loading forecast…</p>}
        {forecast === null && (
          <p className="muted">No forecast yet. Forecasts run every 30 minutes for incidents seen in the last 24 hours.</p>
        )}

        {forecast && threeHours && weather && (
          <>
            <div className="danger">
              <RatingPill rating={threeHours.dangerRating} />
              <span>
                Fire danger index <strong>{threeHours.peakFfdi.toFixed(1)}</strong>
              </span>
            </div>

            <div className="weather">
              <WindArrow towardsDegrees={weather.meanWindFromDegrees + 180} />
              <p>
                Wind <strong>{Math.round(weather.meanWindSpeedKmh)} km/h</strong> from {compass(weather.meanWindFromDegrees)}
                {weather.maxWindGustKmh != null && <>, gusts {Math.round(weather.maxWindGustKmh)}</>}
                <br />
                {Math.round(weather.meanTemperatureC)} °C · humidity {Math.round(weather.meanRelativeHumidityPercent)} %
              </p>
            </div>

            <table className="horizons" style={{ '--key-forecast': RATINGS[threeHours.dangerRating].color } as CSSProperties}>
              <thead>
                <tr>
                  <th scope="col">Within</th>
                  <th scope="col">Head run</th>
                  <th scope="col">Speed</th>
                  <th scope="col">Towards</th>
                </tr>
              </thead>
              <tbody>
                {forecast.horizons.map((h) => (
                  <tr key={h.horizonHours}>
                    <th scope="row">
                      <span className={h.horizonHours === 1 ? 'key key-1h' : 'key key-3h'} aria-hidden="true" />
                      {h.horizonHours} h
                    </th>
                    <td>{formatDistance(h.headDistanceMeters)}</td>
                    <td>{Math.round(h.peakRateOfSpreadMetersPerHour)} m/h</td>
                    <td>{compass(h.spreadBearingDegrees)}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <p className="fine">
              Weather from {clockTime(forecast.weatherFrom)}, computed {relativeTime(forecast.issuedAt)}. Drought factor{' '}
              {forecast.droughtFactor}, fuel load {forecast.fuelLoadTonnesPerHectare} t/ha.
            </p>
            <p className="disclaimer">{forecast.disclaimer}</p>
          </>
        )}
      </section>
    </article>
  );
}

/** Arrow pointing where the wind blows to. */
function WindArrow({ towardsDegrees }: { towardsDegrees: number }) {
  return (
    <svg className="wind-arrow" viewBox="0 0 40 40" role="img" aria-label={`Wind blowing towards ${compass(towardsDegrees)}`}>
      <circle cx="20" cy="20" r="18" />
      <g transform={`rotate(${towardsDegrees} 20 20)`}>
        <line x1="20" y1="31" x2="20" y2="11" />
        <polygon points="20,6 14.5,14 25.5,14" />
      </g>
    </svg>
  );
}

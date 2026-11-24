import type { CSSProperties } from 'react';
import type { Forecast, Incident } from '../api';
import { compass, formatCoordinates, formatDistance, utcStamp } from '../format';
import { ratingStyle } from '../ratings';
import { RatingTag } from './RatingTag';

interface Props {
  incident: Incident;
  /** undefined while loading, null when there is none yet. */
  forecast: Forecast | null | undefined;
  onClose: () => void;
}

export function IncidentDetail({ incident, forecast, onClose }: Props) {
  const threeHours = forecast?.horizons.find((h) => h.horizonHours === 3);
  const weather = forecast?.horizons.find((h) => h.horizonHours === 1)?.weather;

  return (
    <section className="detail" aria-labelledby="detail-title">
      <header className="detail-head">
        <h2 id="detail-title">INC {incident.id}</h2>
        <span className="coords">{formatCoordinates(incident.latitude, incident.longitude)}</span>
        <button type="button" className="close" onClick={onClose} title="Close (Esc)" aria-label="Close incident">
          ✕
        </button>
      </header>

      <dl className="kv">
        <div>
          <dt>PIXELS</dt>
          <dd>{incident.detectionCount}</dd>
        </div>
        <div>
          <dt>MAX FRP</dt>
          <dd>{incident.maxFrpMegawatts != null ? `${incident.maxFrpMegawatts.toFixed(1)} MW` : '–'}</dd>
        </div>
        <div>
          <dt>FIRST SEEN</dt>
          <dd>{utcStamp(incident.firstDetectedAt)}</dd>
        </div>
        <div>
          <dt>LAST SEEN</dt>
          <dd>{utcStamp(incident.lastDetectedAt)}</dd>
        </div>
      </dl>

      <h3 className="rule">
        <span>SPREAD FORECAST</span>
        {forecast && <span className="dim">WX {utcStamp(forecast.weatherFrom)}</span>}
      </h3>

      {forecast === undefined && <p className="dim">Loading forecast…</p>}
      {forecast === null && <p className="dim">No forecast yet. Forecasts run every 30 min for incidents seen in the last 24 h.</p>}

      {forecast && threeHours && weather && (
        <>
          <div className="danger">
            <span className="ffdi">{threeHours.peakFfdi.toFixed(1)}</span>
            <span className="ffdi-label">
              FFDI
              <RatingTag rating={threeHours.dangerRating} long />
            </span>
          </div>

          <div className="wind">
            <WindArrow towardsDegrees={weather.meanWindFromDegrees + 180} />
            <dl className="kv kv-inline">
              <div>
                <dt>WIND</dt>
                <dd>
                  {compass(weather.meanWindFromDegrees)} {Math.round(weather.meanWindSpeedKmh)}
                  {weather.maxWindGustKmh != null && <span className="dim"> G{Math.round(weather.maxWindGustKmh)}</span>} km/h
                </dd>
              </div>
              <div>
                <dt>TEMP</dt>
                <dd>{Math.round(weather.meanTemperatureC)} °C</dd>
              </div>
              <div>
                <dt>RH</dt>
                <dd>{Math.round(weather.meanRelativeHumidityPercent)} %</dd>
              </div>
            </dl>
          </div>

          {/* Same colour as the spread areas on the map. */}
          <table className="horizons" style={{ '--key-forecast': ratingStyle(incident).color } as CSSProperties}>
            <thead>
              <tr>
                <th scope="col">WITHIN</th>
                <th scope="col" className="num">HEAD RUN</th>
                <th scope="col" className="num">ROS</th>
                <th scope="col" className="num">TOWARDS</th>
              </tr>
            </thead>
            <tbody>
              {forecast.horizons.map((h) => (
                <tr key={h.horizonHours}>
                  <th scope="row">
                    <span className={h.horizonHours === 1 ? 'key key-1h' : 'key key-3h'} aria-hidden="true" />+{h.horizonHours}H
                  </th>
                  <td className="num">{formatDistance(h.headDistanceMeters)}</td>
                  <td className="num">{Math.round(h.peakRateOfSpreadMetersPerHour)} m/h</td>
                  <td className="num">{compass(h.spreadBearingDegrees)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          <p className="fine">
            {forecast.disclaimer} Drought factor {forecast.droughtFactor}, fuel load {forecast.fuelLoadTonnesPerHectare} t/ha.
          </p>
        </>
      )}
    </section>
  );
}

/** Points where the wind blows to. */
function WindArrow({ towardsDegrees }: { towardsDegrees: number }) {
  return (
    <svg className="wind-arrow" viewBox="0 0 40 40" role="img" aria-label={`Wind blowing towards ${compass(towardsDegrees)}`}>
      <circle cx="20" cy="20" r="18" />
      <line x1="20" y1="2" x2="20" y2="6" className="tick" />
      <g transform={`rotate(${towardsDegrees} 20 20)`}>
        <line x1="20" y1="31" x2="20" y2="11" />
        <polygon points="20,6 15,14 25,14" />
      </g>
    </svg>
  );
}

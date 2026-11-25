import type { CSSProperties } from 'react';
import type { Forecast, Incident } from '../api';
import { formatDistance, localStamp } from '../format';
import { directionWord } from '../places';
import { RATINGS, ratingStyle } from '../ratings';

interface Props {
  incident: Incident;
  /** undefined while loading, null when there is none yet. */
  forecast: Forecast | null | undefined;
}

export function ForecastTab({ incident, forecast }: Props) {
  const threeHours = forecast?.horizons.find((h) => h.horizonHours === 3);
  const weather = forecast?.horizons.find((h) => h.horizonHours === 1)?.weather;

  if (forecast === undefined) return <p className="hint">Loading the forecast…</p>;
  if (forecast === null || !threeHours || !weather) {
    return <p className="hint">No forecast yet. Forecasts are made every 30 minutes for fires on the board.</p>;
  }

  const danger = RATINGS[threeHours.dangerRating];

  return (
    <div className="stack">
      <dl className="facts">
        <div>
          <dt>Fire danger</dt>
          <dd>
            <span className="danger-dot" style={{ background: danger.color }} aria-hidden="true" />
            {danger.label} <span className="hint">(index {threeHours.peakFfdi.toFixed(1)})</span>
          </dd>
        </div>
        <div>
          <dt>Wind</dt>
          <dd>
            <WindArrow towardsDegrees={weather.meanWindFromDegrees + 180} />
            From the {directionWord(weather.meanWindFromDegrees)}, {Math.round(weather.meanWindSpeedKmh)} km/h
            {weather.maxWindGustKmh != null && <>, gusts {Math.round(weather.maxWindGustKmh)}</>}
          </dd>
        </div>
        <div>
          <dt>Temperature</dt>
          <dd>{Math.round(weather.meanTemperatureC)} °C</dd>
        </div>
        <div>
          <dt>Humidity</dt>
          <dd>{Math.round(weather.meanRelativeHumidityPercent)} %</dd>
        </div>
      </dl>

      {/* Same colour as the spread areas on the map. */}
      <ul className="spread" style={{ '--forecast-color': ratingStyle(incident).color } as CSSProperties}>
        {forecast.horizons.map((h) => (
          <li key={h.horizonHours}>
            <span className={h.horizonHours === 1 ? 'swatch swatch-1h' : 'swatch swatch-3h'} aria-hidden="true" />
            <span>
              Within {h.horizonHours === 1 ? '1 hour' : `${h.horizonHours} hours`}: about <strong>{formatDistance(h.headDistanceMeters)}</strong>{' '}
              {directionWord(h.spreadBearingDegrees)}
              <span className="hint"> at up to {Math.round(h.peakRateOfSpreadMetersPerHour)} m/h</span>
            </span>
          </li>
        ))}
      </ul>

      <p className="hint">
        Weather for {localStamp(forecast.weatherFrom)} onwards, from Open-Meteo. {forecast.disclaimer}
      </p>
    </div>
  );
}

/** Points where the wind blows to. */
function WindArrow({ towardsDegrees }: { towardsDegrees: number }) {
  return (
    <svg className="wind-arrow" viewBox="0 0 24 24" aria-hidden="true">
      <g transform={`rotate(${towardsDegrees} 12 12)`}>
        <path d="M12 20V5M7 10l5-5 5 5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </g>
    </svg>
  );
}

import type { CSSProperties } from 'react';
import type { Incident } from '../api';
import { RATINGS, ratingStyle } from '../ratings';
import { AIRSPACE_COLORS } from './AirspaceTab';
import { BURNING_COLOR, SELECTED_COLOR } from './MapView';

interface Props {
  selected: Incident | null;
  showAirspace: boolean;
  showPixels: boolean;
  onTogglePixels: (on: boolean) => void;
}

/** Map key, and the one map setting: show raw satellite pixels. */
export function Legend({ selected, showAirspace, showPixels, onTogglePixels }: Props) {
  const colors = {
    '--forecast-color': selected ? ratingStyle(selected).color : BURNING_COLOR,
    '--burning-color': BURNING_COLOR,
  } as CSSProperties;

  return (
    <div className="legend" style={colors} aria-label="Map key">
      <p className="legend-title">Fire danger</p>
      <ul className="legend-row">
        {Object.values(RATINGS).map((r) => (
          <li key={r.code}>
            <i className="legend-dot" style={{ background: r.color }} />
            {r.short}
          </li>
        ))}
      </ul>
      {selected && (
        <ul className="legend-row">
          <li>
            <i className="swatch swatch-now" /> Burning now
          </li>
          <li>
            <i className="swatch swatch-1h" /> 1 h
          </li>
          <li>
            <i className="swatch swatch-3h" /> 3 h
          </li>
          <li>
            <i className="legend-dot" style={{ background: SELECTED_COLOR }} /> Unit
          </li>
        </ul>
      )}
      {showAirspace && (
        <ul className="legend-row">
          <li>
            <i className="legend-ring" style={{ borderColor: AIRSPACE_COLORS.low }} /> Drone airspace
          </li>
          <li>
            <i className="legend-ring" style={{ borderColor: AIRSPACE_COLORS.high }} /> Aircraft airspace
          </li>
        </ul>
      )}
      <label className="legend-toggle" title="Shortcut: P">
        <input type="checkbox" checked={showPixels} onChange={(e) => onTogglePixels(e.target.checked)} />
        Satellite pixels, last 24 h
      </label>
    </div>
  );
}

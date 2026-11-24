import type { CSSProperties } from 'react';
import type { Incident } from '../api';
import { NO_FORECAST, RATINGS, ratingStyle } from '../ratings';
import { AIRSPACE_COLORS } from './AirspaceTab';
import { BURNING_COLOR, SELECTED_COLOR } from './MapView';

/** Map key: rating colours always; burning area, spread areas, units and airspace while an incident is open. */
export function Legend({ selected, showAirspace }: { selected: Incident | null; showAirspace: boolean }) {
  const keyColors = {
    '--key-forecast': selected ? ratingStyle(selected).color : BURNING_COLOR,
    '--key-now': BURNING_COLOR,
  } as CSSProperties;

  return (
    <div className="legend" style={keyColors} aria-label="Map key">
      <div className="legend-row">
        {[...Object.values(RATINGS), NO_FORECAST].map((r) => (
          <span key={r.code} title={r.label}>
            <i className="dot" style={{ background: r.color }} />
            {r.code}
          </span>
        ))}
      </div>
      {selected && (
        <div className="legend-row">
          <span>
            <i className="key key-now" /> BURNING
          </span>
          <span>
            <i className="key key-1h" /> +1H
          </span>
          <span>
            <i className="key key-3h" /> +3H
          </span>
          <span>
            <i className="dot" style={{ background: SELECTED_COLOR }} /> UNIT
          </span>
        </div>
      )}
      {showAirspace && (
        <div className="legend-row">
          <span>
            <i className="ring" style={{ borderColor: AIRSPACE_COLORS.low }} /> AIR LOW
          </span>
          <span>
            <i className="ring" style={{ borderColor: AIRSPACE_COLORS.high }} /> AIR HIGH
          </span>
        </div>
      )}
    </div>
  );
}

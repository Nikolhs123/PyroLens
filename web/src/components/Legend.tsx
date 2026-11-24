import type { CSSProperties } from 'react';
import type { Incident } from '../api';
import { NO_FORECAST, RATINGS, ratingStyle } from '../ratings';
import { BURNING_COLOR } from './MapView';

/** Map key: rating colours always; burning area and spread areas while an incident is open. */
export function Legend({ selected }: { selected: Incident | null }) {
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
        </div>
      )}
    </div>
  );
}

import { useEffect, useState } from 'react';
import type { Status } from '../api';
import { compactAge, minutesSince, utcClock } from '../format';

interface Props {
  status: Status | undefined;
  offline: boolean;
  active: number;
}

/** Top strip of the console: clock, data freshness and link state at a glance. */
export function StatusBar({ status, offline, active }: Props) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  return (
    <header className="statusbar">
      <span className="brand">PYROLENS</span>
      <span className="cell">GR</span>
      <span className="cell" aria-label="Coordinated universal time">
        <time className="clock">{utcClock(now)}</time> UTC
      </span>
      {/* Ingestion runs every 10 min and forecasts every 30 min; colour flags data that has gone stale. */}
      <Freshness label="FIRMS" title="Satellite detections" iso={status?.lastDetectionIngestedAt} now={now} warnAfter={30} badAfter={120} />
      <Freshness label="WX" title="Weather forecasts" iso={status?.lastForecastIssuedAt} now={now} warnAfter={60} badAfter={180} />
      <span className="cell">
        <strong>{active}</strong> ACTIVE
      </span>
      <span className={offline ? 'cell state bad' : 'cell state ok'}>
        <i aria-hidden="true" />
        {offline ? 'API OFFLINE' : 'API OK'}
      </span>
      <span className="spacer" />
      <span className="cell notice">DEMO · NOT AN EMERGENCY SERVICE · EMERGENCY 112</span>
    </header>
  );
}

interface FreshnessProps {
  label: string;
  title: string;
  iso: string | null | undefined;
  now: Date;
  warnAfter: number;
  badAfter: number;
}

function Freshness({ label, title, iso, now, warnAfter, badAfter }: FreshnessProps) {
  if (!iso) {
    return (
      <span className="cell state bad" title={`${title}: none yet`}>
        <i aria-hidden="true" />
        {label} —
      </span>
    );
  }

  const age = minutesSince(iso, now.getTime());
  const level = age > badAfter ? 'bad' : age > warnAfter ? 'warn' : 'ok';
  return (
    <span className={`cell state ${level}`} title={`${title}: last update ${compactAge(iso, now.getTime())} ago`}>
      <i aria-hidden="true" />
      {label} {compactAge(iso, now.getTime())}
    </span>
  );
}

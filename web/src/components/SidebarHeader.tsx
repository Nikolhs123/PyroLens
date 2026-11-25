import type { Status } from '../api';
import { minutesSince, relativeTime } from '../format';
import type { Theme } from '../theme';
import { useNow } from '../usePolling';
import { Icon } from './Icon';
import { OperatorControl } from './OperatorControl';

interface Props {
  status: Status | undefined;
  offline: boolean;
  fires: number;
  theme: Theme;
  onToggleTheme: () => void;
  operatorKey: string | null;
  onUnlock: (key: string) => void;
  onLock: () => void;
}

/** Brand, a one-line summary of the situation, and how fresh the data is, in words. */
export function SidebarHeader({ status, offline, fires, theme, onToggleTheme, operatorKey, onUnlock, onLock }: Props) {
  const now = useNow(30_000);

  return (
    <header className="sb-head">
      <div className="brand-row">
        <span className="logo" aria-hidden="true">
          <Icon name="flame" size={22} />
        </span>
        <h1>PyroLens</h1>
        <button
          type="button"
          className="icon-button"
          onClick={onToggleTheme}
          title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
          aria-label={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
        >
          <Icon name={theme === 'dark' ? 'sun' : 'moon'} />
        </button>
      </div>

      <p className="lede">
        {offline ? 'The server is not answering. Retrying every minute.' : `${fires} ${fires === 1 ? 'fire' : 'fires'} on the board in Greece`}
      </p>
      <p className="fresh">
        <Freshness label="Satellite data" iso={status?.lastDetectionIngestedAt} now={now} lateAfter={30} />
        <span aria-hidden="true"> · </span>
        <Freshness label="weather" iso={status?.lastForecastIssuedAt} now={now} lateAfter={60} />
      </p>

      <OperatorControl operatorKey={operatorKey} onUnlock={onUnlock} onLock={onLock} />
    </header>
  );
}

/** "Satellite data 18 min ago", flagged as late when the scheduled jobs fall behind. */
function Freshness({ label, iso, now, lateAfter }: { label: string; iso: string | null | undefined; now: number; lateAfter: number }) {
  if (!iso) return <span className="late">{label}: none yet</span>;
  const late = minutesSince(iso, now) > lateAfter;
  return (
    <span className={late ? 'late' : undefined} title={late ? 'Older than expected: the scheduled jobs may be behind.' : undefined}>
      {label} {relativeTime(iso, now)}
      {late && ' (late)'}
    </span>
  );
}

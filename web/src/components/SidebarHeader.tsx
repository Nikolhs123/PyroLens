import type { Status } from '../api';
import { type Freshness, describeFreshness } from '../freshness';
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
  const now = useNow(15_000);

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
      {status && (
        <p className="fresh" aria-live="polite">
          <FreshnessText f={describeFreshness('satellites', status.ingestion, status.lastDetectionIngestedAt, now)} />
          <span aria-hidden="true"> · </span>
          <FreshnessText f={describeFreshness('weather', status.forecast, status.lastForecastIssuedAt, now)} />
        </p>
      )}

      <OperatorControl operatorKey={operatorKey} onUnlock={onUnlock} onLock={onLock} />
    </header>
  );
}

function FreshnessText({ f }: { f: Freshness }) {
  return (
    <span className={f.busy ? 'busy' : f.late ? 'late' : undefined} title={f.detail}>
      {f.text}
    </span>
  );
}

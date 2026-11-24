import { type FormEvent, useState } from 'react';
import { actions } from '../api';

interface Props {
  operatorKey: string | null;
  onUnlock: (key: string) => void;
  onLock: () => void;
}

/** Status-strip control: read-only by default; the operator key unlocks dispatcher actions for this tab. */
export function OperatorControl({ operatorKey, onUnlock, onLock }: Props) {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);

  if (operatorKey) {
    return (
      <button type="button" className="cell op op-on" onClick={onLock} title="Dispatcher actions enabled. Click to lock.">
        <i aria-hidden="true" />
        OPERATOR
      </button>
    );
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!value.trim()) return;
    setChecking(true);
    const result = await actions.checkKey(value.trim());
    setChecking(false);
    if (result.ok) {
      onUnlock(value.trim());
      setValue('');
      setError(null);
      setOpen(false);
    } else {
      setError(
        result.status === 401 ? 'Wrong key.' : result.status === 403 ? 'This server is read-only: no operator key is configured.' : result.message,
      );
    }
  };

  return (
    <div className="op-wrap">
      <button type="button" className="cell op" onClick={() => setOpen((o) => !o)} aria-expanded={open} title="Unlock dispatcher actions">
        <i aria-hidden="true" />
        READ-ONLY
      </button>
      {open && (
        <form className="op-form" onSubmit={submit}>
          <label htmlFor="operator-key">OPERATOR KEY</label>
          <input
            id="operator-key"
            type="password"
            autoComplete="off"
            autoFocus
            value={value}
            onChange={(e) => setValue(e.target.value)}
          />
          <div className="op-buttons">
            <button type="submit" className="btn btn-primary" disabled={checking || !value.trim()}>
              {checking ? 'CHECKING…' : 'UNLOCK'}
            </button>
            <button type="button" className="btn" onClick={() => setOpen(false)}>
              CANCEL
            </button>
          </div>
          {error && <p className="op-error" role="alert">{error}</p>}
          <p className="dim">Kept for this browser tab only. Visitors without the key see everything but cannot change it.</p>
        </form>
      )}
    </div>
  );
}

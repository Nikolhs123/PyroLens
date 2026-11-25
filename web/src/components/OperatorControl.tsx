import { type FormEvent, useState } from 'react';
import { actions } from '../api';
import { Icon } from './Icon';

interface Props {
  operatorKey: string | null;
  onUnlock: (key: string) => void;
  onLock: () => void;
}

/** View-only by default; the operator key unlocks dispatcher actions for this browser tab. */
export function OperatorControl({ operatorKey, onUnlock, onLock }: Props) {
  const [open, setOpen] = useState(false);
  const [value, setValue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [checking, setChecking] = useState(false);

  if (operatorKey) {
    return (
      <div className="op-row on">
        <Icon name="unlock" size={16} />
        <span>Operator mode: you can change fires, units and airspace.</span>
        <button type="button" className="link" onClick={onLock}>
          Lock
        </button>
      </div>
    );
  }

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const key = value.trim();
    if (!key) return;
    setChecking(true);
    const result = await actions.checkKey(key);
    setChecking(false);
    if (result.ok) {
      onUnlock(key);
      setValue('');
      setError(null);
      setOpen(false);
    } else {
      setError(
        result.status === 401
          ? 'That key is not right.'
          : result.status === 403
            ? 'This server is view-only: no operator key is set up.'
            : result.message,
      );
    }
  };

  return (
    <div className="op">
      <div className="op-row">
        <Icon name="lock" size={16} />
        <span>View only</span>
        <button type="button" className="link" onClick={() => setOpen((o) => !o)} aria-expanded={open}>
          {open ? 'Cancel' : 'Unlock operator mode'}
        </button>
      </div>
      {open && (
        <form className="op-form" onSubmit={submit}>
          <label htmlFor="operator-key">Operator key</label>
          <div className="op-input">
            <input id="operator-key" type="password" autoComplete="off" autoFocus value={value} onChange={(e) => setValue(e.target.value)} />
            <button type="submit" className="button primary" disabled={checking || !value.trim()}>
              {checking ? 'Checking…' : 'Unlock'}
            </button>
          </div>
          {error && (
            <p className="op-error" role="alert">
              {error}
            </p>
          )}
          <p className="hint">Kept for this browser tab only. Everyone else can look but not change anything.</p>
        </form>
      )}
    </div>
  );
}

import { useCallback, useEffect, useRef, useState } from 'react';

export interface Polled<T> {
  data: T | undefined;
  error: string | null;
  /** When the last successful load finished (ms since epoch). */
  updatedAt: number | null;
  /** Load again now, e.g. right after an action changed the data. */
  reload: () => void;
}

/**
 * Loads now and then every `intervalMs`, cancelling the previous request when `key` changes
 * or the component unmounts. Pass `enabled = false` to pause (e.g. a hidden layer).
 */
export function usePolling<T>(
  load: (signal: AbortSignal) => Promise<T>,
  intervalMs: number,
  key: string | number | null,
  enabled = true,
): Polled<T> {
  const [state, setState] = useState<Omit<Polled<T>, 'reload'>>({ data: undefined, error: null, updatedAt: null });
  const loadRef = useRef(load);
  loadRef.current = load;
  const runRef = useRef<() => void>(() => {});

  useEffect(() => {
    if (!enabled) return;
    let controller = new AbortController();

    const run = async () => {
      controller.abort();
      controller = new AbortController();
      try {
        const data = await loadRef.current(controller.signal);
        setState({ data, error: null, updatedAt: Date.now() });
      } catch (e) {
        if ((e as Error).name === 'AbortError') return;
        setState((s) => ({ ...s, error: (e as Error).message }));
      }
    };
    runRef.current = () => void run();

    // A new key (e.g. another incident) must never show the previous key's data.
    setState({ data: undefined, error: null, updatedAt: null });
    void run();
    const timer = window.setInterval(run, intervalMs);
    return () => {
      window.clearInterval(timer);
      controller.abort();
      runRef.current = () => {};
    };
  }, [key, intervalMs, enabled]);

  const reload = useCallback(() => runRef.current(), []);
  return { ...state, reload };
}

/** Current time, refreshed every `intervalMs` while mounted (for countdowns and ages). */
export function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}

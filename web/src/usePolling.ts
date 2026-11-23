import { useEffect, useRef, useState } from 'react';

export interface Polled<T> {
  data: T | undefined;
  error: string | null;
  /** When the last successful load finished (ms since epoch). */
  updatedAt: number | null;
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
  const [state, setState] = useState<Polled<T>>({ data: undefined, error: null, updatedAt: null });
  const loadRef = useRef(load);
  loadRef.current = load;

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

    // A new key (e.g. another incident) must never show the previous key's data.
    setState({ data: undefined, error: null, updatedAt: null });
    void run();
    const timer = window.setInterval(run, intervalMs);
    return () => {
      window.clearInterval(timer);
      controller.abort();
    };
  }, [key, intervalMs, enabled]);

  return state;
}

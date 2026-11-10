import { useCallback, useEffect, useState } from "react";
export function usePolling<T>(
  load: (signal: AbortSignal) => Promise<T>,
  key: string,
  enabled = true,
) {
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState<{
    key: string;
    data: T | null;
    error: string | null;
    loading: boolean;
    updated: number | null;
  }>({ key, data: null, error: null, loading: true, updated: null });
  const refresh = useCallback(() => setRevision((n) => n + 1), []);
  useEffect(() => {
    if (!enabled) return;
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    const controller = new AbortController();
    setState((previous) => ({
      key,
      data: previous.key === key ? previous.data : null,
      error: null,
      loading: true,
      updated: previous.key === key ? previous.updated : null,
    }));
    const poll = async () => {
      try {
        const data = await load(controller.signal);
        if (active)
          setState({
            key,
            data,
            error: null,
            loading: false,
            updated: Date.now(),
          });
      } catch (error) {
        if (active)
          setState((previous) => ({
            ...previous,
            key,
            loading: false,
            error:
              error instanceof Error
                ? error.message
                : "Unable to load data. Please retry.",
          }));
      } finally {
        if (active) timer = setTimeout(poll, 4000);
      }
    };
    void poll();
    return () => {
      active = false;
      controller.abort();
      clearTimeout(timer);
    };
  }, [load, key, enabled, revision]);
  const current = state.key === key && enabled;
  return {
    data: current ? state.data : null,
    error: current ? state.error : null,
    loading: enabled && (!current || state.loading),
    updated: current ? state.updated : null,
    refresh,
  };
}

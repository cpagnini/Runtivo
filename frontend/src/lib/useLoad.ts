"use client";

import { useCallback, useEffect, useState } from "react";
import { errorMessage } from "./api";

/** Carica dati all'ingresso e dopo ogni reload(); con enabled=false non fa nulla. */
export function useLoad<T>(load: () => Promise<T>, key: string, enabled = true) {
  const [tick, setTick] = useState(0);
  const [result, setResult] = useState<{ data?: T; error?: string } | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    load().then(
      (data) => !cancelled && setResult({ data }),
      (e) => !cancelled && setResult({ error: errorMessage(e) }),
    );
    return () => {
      cancelled = true;
    };
    // `load` cambia a ogni render: la rilettura dipende solo da key e tick.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, tick, enabled]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  return { data: result?.data, error: result?.error, loading: enabled && result === null, reload };
}

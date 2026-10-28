"use client";

import { useEffect } from "react";

/** Registra il service worker solo in produzione: in sviluppo complicherebbe la ricarica delle pagine. */
export function PwaRegister() {
  useEffect(() => {
    if (process.env.NODE_ENV === "production" && "serviceWorker" in navigator) {
      navigator.serviceWorker.register("/sw.js").catch(() => {});
    }
  }, []);
  return null;
}

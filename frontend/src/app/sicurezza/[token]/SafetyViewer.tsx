"use client";

import { api, formatWhen, runStatusLabel, type SafetyView } from "@/lib/api";
import { useLoad } from "@/lib/useLoad";
import { Card, ErrorText, Loading } from "@/components/ui";

/** Pagina pubblica, in sola lettura, per la persona di fiducia con cui qualcuno ha condiviso un'uscita. */
export function SafetyViewer({ token }: { token: string }) {
  const { data, error, loading } = useLoad(() => api<SafetyView>(`/safety/${encodeURIComponent(token)}`), `safety-${token}`);

  if (loading) return <Loading />;
  if (error || !data)
    return (
      <div className="stack">
        <h1>Link non valido</h1>
        <ErrorText>{error ?? "Questo link non è valido o è scaduto."}</ErrorText>
        <p className="muted">Può essere scaduto, essere stato revocato da chi l&apos;ha condiviso oppure non essere corretto.</p>
      </div>
    );

  const cancelled = data.status === "Cancelled";
  return (
    <div className="stack">
      <h1>
        {data.sharedBy} ha condiviso con te un&apos;uscita
      </h1>
      {cancelled && <p className="banner">Questa uscita è stata annullata.</p>}
      <Card>
        <div className="stack">
          <p>
            <strong>{formatWhen(data.startsAtUtc, data.timeZoneId)}</strong>
          </p>
          <p>
            Ritrovo: {data.placeName}, {data.city} (luogo pubblico)
          </p>
          <p className="muted small">Stato: {runStatusLabel[data.status]}</p>
          {!cancelled && data.participants.length > 0 && <p>Corrono insieme: {data.participants.join(", ")}</p>}
        </div>
      </Card>
      <p className="muted">
        Se dopo l&apos;orario previsto non hai notizie di {data.sharedBy}, prova a contattare la persona. Questo link smette di funzionare il{" "}
        {new Date(data.expiresAt).toLocaleString("it-IT", { dateStyle: "long", timeStyle: "short" })} o prima, se chi l&apos;ha condiviso lo revoca.
      </p>
    </div>
  );
}

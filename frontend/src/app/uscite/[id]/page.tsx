"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import {
  api,
  errorMessage,
  formatDistance,
  formatPaceRange,
  formatWhen,
  participantStatusLabel,
  runStatusLabel,
  type Run,
} from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { Card, ErrorText, Loading } from "@/components/ui";

function RunDetails({ run, myId, reload }: { run: Run; myId: string; reload: () => void }) {
  // Letto qui, dove il componente esiste solo dopo il caricamento: durante il pre-rendering non c'è un "adesso".
  const [now] = useState(() => Date.now());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const started = new Date(run.startsAtUtc).getTime() <= now;
  const open = run.status !== "Cancelled" && !started;
  const waiting = run.participants.filter((p) => p.status === "Pending").length;

  async function act(path: string, confirmText?: string) {
    if (confirmText && !confirm(confirmText)) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/runs/${run.id}/${path}`, { method: "POST" });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
      reload();
    }
  }

  async function block(userId: string, name: string) {
    if (!confirm(`Bloccare ${name}? Uscirai da questa uscita e non vi vedrete più a vicenda.`)) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/users/${userId}/block`, { method: "POST" });
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
      reload();
    }
  }

  return (
    <div className="stack">
      <p className="muted small">
        <Link href="/compagni">← Compagni</Link>
      </p>
      <div className="row">
        <h1>Uscita spontanea</h1>
        <span className="badge">{runStatusLabel[run.status]}</span>
      </div>

      {run.status === "Cancelled" && <p className="banner">Questa uscita è stata annullata. {run.cancelReason}</p>}
      {run.status === "Confirmed" && !started && <p role="status">Tutti hanno confermato: ci vediamo lì!</p>}
      {run.status === "Proposed" && open && <p className="muted">{waiting > 0 ? `In attesa della risposta di ${waiting} ${waiting === 1 ? "persona" : "persone"}.` : "In attesa di conferma."}</p>}
      {started && run.status !== "Cancelled" && <p className="muted">L&apos;uscita è già iniziata.</p>}

      <Card>
        <div className="stack">
          <p>
            <strong>{formatWhen(run.startsAtUtc, run.timeZoneId)}</strong>
          </p>
          <p>
            Ritrovo: {run.place.name}, {run.place.city}
          </p>
          {(run.distanceMeters || run.paceMinSecPerKm) && (
            <p className="muted">
              {[run.distanceMeters ? formatDistance(run.distanceMeters) : "", formatPaceRange(run.paceMinSecPerKm, run.paceMaxSecPerKm)].filter(Boolean).join(" · ")}
            </p>
          )}
        </div>
      </Card>

      <h2>Chi corre</h2>
      <ul className="list">
        {run.participants.map((p) => (
          <li key={p.userId} className="card row">
            <span>
              <strong>{p.displayName}</strong>
              {p.status === "Declined" || p.status === "Left" ? <span className="muted"> · {participantStatusLabel[p.status]}</span> : null}
              {p.status === "Pending" && <span className="muted small"> · {participantStatusLabel.Pending}</span>}
              {p.status === "Confirmed" && <span className="badge"> Conferma</span>}
            </span>
            {open && p.userId !== myId && (p.status === "Pending" || p.status === "Confirmed") && (
              <button className="link-button small" onClick={() => block(p.userId, p.displayName)} disabled={busy}>
                Blocca
              </button>
            )}
          </li>
        ))}
      </ul>

      <ErrorText>{error}</ErrorText>

      {open && run.myStatus === "Pending" && (
        <div className="row wrap">
          <button className="button" disabled={busy} onClick={() => act("confirm")}>
            Confermo
          </button>
          <button className="button secondary" disabled={busy} onClick={() => act("decline", "Rifiutare questa uscita?")}>
            Non posso
          </button>
        </div>
      )}
      {open && run.myStatus === "Confirmed" && (
        <button className="button secondary" disabled={busy} onClick={() => act("decline", "Vuoi lasciare questa uscita? Gli altri ne saranno informati dalla pagina.")}>
          Non posso più
        </button>
      )}
      {open && run.isOrganizer && (
        <button className="button danger" disabled={busy} onClick={() => act("cancel", "Annullare l'uscita per tutti?")}>
          Annulla l&apos;uscita
        </button>
      )}

      <Card>
        <div className="stack">
          <h2>Prima di andare</h2>
          <ul className="reasons">
            <li>Ci si vede in un luogo pubblico, di giorno o ben illuminato.</li>
            <li>Dì a qualcuno dove vai e con chi, e porta il telefono.</li>
            <li>Se qualcosa non ti convince, non andare: puoi lasciare l&apos;uscita e bloccare la persona in qualsiasi momento.</li>
          </ul>
        </div>
      </Card>
    </div>
  );
}

function RunPage() {
  const { id } = useParams<{ id: string }>();
  const user = useRequireLogin();
  const { data, error, loading, reload } = useLoad(() => api<Run>(`/runs/${id}`), `run-${id}`, !!user);

  if (!user || loading) return <Loading />;
  if (error || !data) return <ErrorText>{error ?? "Uscita non trovata."}</ErrorText>;
  return <RunDetails run={data} myId={user.id} reload={reload} />;
}

export default function RunDetailPage() {
  return (
    <Suspense fallback={<Loading />}>
      <RunPage />
    </Suspense>
  );
}

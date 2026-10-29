"use client";

import { useState } from "react";
import Link from "next/link";
import {
  adesioni,
  api,
  errorMessage,
  formatDistance,
  formatPaceRange,
  formatWhen,
  type Group,
  type RunEvent,
} from "@/lib/api";
import { useSession } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { ErrorText, Loading } from "@/components/ui";

function GroupCard({
  group,
  event,
  isMine,
  canAct,
  busy,
  onJoin,
}: {
  group: Group;
  event: RunEvent;
  isMine: boolean;
  canAct: boolean;
  busy: boolean;
  onJoin: () => void;
}) {
  const full = group.capacity != null && group.goingCount >= group.capacity;
  const seats = group.capacity != null ? `${group.goingCount}/${group.capacity} posti` : adesioni(group.goingCount);
  const details = [formatDistance(group.distanceMeters), formatPaceRange(group.paceMinSecPerKm, group.paceMaxSecPerKm)].filter(Boolean);

  return (
    <li className={`card group-card${isMine ? " selected" : ""}`}>
      <div className="row">
        <strong>{group.name}</strong>
        {isMine ? <span className="badge">Partecipi</span> : full ? <span className="badge">Completo</span> : null}
      </div>
      {details.length > 0 && <p>{details.join(" · ")}</p>}
      <p className="muted small">
        {group.leaderName ? `Capogruppo: ${group.leaderName} · ` : ""}
        {seats}
      </p>
      {group.participants && group.participants.length > 0 && <p className="names">{group.participants.join(", ")}</p>}
      {canAct && !isMine && event.status === "Scheduled" && (
        <button className="button" disabled={busy || full} onClick={onJoin}>
          {full ? "Gruppo al completo" : event.myRsvp?.status === "Going" ? "Passa a questo gruppo" : "Partecipo con questo gruppo"}
        </button>
      )}
    </li>
  );
}

export function EventView({ id }: { id: string }) {
  const { status, user } = useSession();
  const { data: event, error, loading, reload } = useLoad(
    () => api<RunEvent>(`/events/${id}`),
    `event-${id}-${status}`,
    status !== "loading",
  );
  const [now] = useState(() => Date.now());
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  if (loading || status === "loading") return <Loading />;
  if (error || !event)
    return (
      <div className="stack">
        <h1>Evento non trovato</h1>
        <ErrorText>{error}</ErrorText>
        {status !== "authenticated" && (
          <p className="muted">Se l&apos;evento è riservato a lə membrə di un club, accedi per vederlo.</p>
        )}
      </div>
    );

  const started = new Date(event.startsAtUtc).getTime() <= now;
  const open = event.status === "Scheduled" && !started;
  const mine = event.myRsvp;
  const isAdmin = event.myRole === "Admin";

  async function run(action: () => Promise<unknown>, message?: string) {
    setBusy(true);
    setActionError(null);
    setNotice(null);
    try {
      await action();
      if (message) setNotice(message);
    } catch (err) {
      setActionError(errorMessage(err));
    } finally {
      setBusy(false);
      reload(); // anche dopo un errore: i contatori mostrati potrebbero essere cambiati
    }
  }

  const rsvp = (paceGroupId: string | null, rsvpStatus: "Going" | "NotGoing", message: string) =>
    run(() => api(`/events/${id}/rsvp`, { method: "PUT", body: { status: rsvpStatus, paceGroupId } }), message);

  async function share() {
    const url = window.location.href;
    try {
      if (navigator.share) await navigator.share({ title: event!.title, text: `${event!.title} · ${formatWhen(event!.startsAtUtc, event!.timeZoneId)}`, url });
      else {
        await navigator.clipboard.writeText(url);
        setNotice("Link copiato: incollalo dove vuoi.");
      }
    } catch {
      // Condivisione annullata dall'utente: nessun messaggio.
    }
  }

  return (
    <div className="stack">
      <div>
        <p className="muted small">
          <Link href={`/club/${event.clubId}`}>{event.clubName}</Link>
          {event.visibility === "ClubOnly" && " · Solo membrə"}
          {event.seriesId && " · Serie settimanale"}
        </p>
        <h1>{event.title}</h1>
      </div>

      {event.status === "Cancelled" && <p className="banner">Questa uscita è stata annullata.</p>}
      {event.status === "Scheduled" && started && <p className="banner">Questa uscita è già iniziata.</p>}

      <section className="card stack">
        <p>
          <strong>{formatWhen(event.startsAtUtc, event.timeZoneId)}</strong>
        </p>
        <p>Ritrovo: {event.meetingPointLabel}</p>
        {event.description && <p className="muted">{event.description}</p>}
      </section>

      <h2>Scegli il tuo gruppo</h2>
      <ul className="list">
        {event.groups.map((g) => (
          <GroupCard
            key={g.id}
            group={g}
            event={event}
            isMine={mine?.status === "Going" && mine.paceGroupId === g.id}
            canAct={open && status === "authenticated"}
            busy={busy}
            onJoin={() => rsvp(g.id, "Going", `Ci sei: ${g.name}.`)}
          />
        ))}
      </ul>

      {open && status !== "authenticated" && (
        <Link className="button" href={`/accedi?next=${encodeURIComponent(`/e/${id}`)}`}>
          Accedi per partecipare
        </Link>
      )}

      {open && user && mine?.status === "Going" && (
        <button className="button secondary" disabled={busy} onClick={() => run(() => api(`/events/${id}/rsvp`, { method: "DELETE" }), "Partecipazione annullata.")}>
          Annulla la mia partecipazione
        </button>
      )}
      {open && user && mine?.status !== "Going" && (
        <button className="button secondary" disabled={busy || mine?.status === "NotGoing"} onClick={() => rsvp(null, "NotGoing", "Va bene: segnato come non partecipante.")}>
          {mine?.status === "NotGoing" ? "Hai detto che non partecipi" : "Non partecipo"}
        </button>
      )}

      <ErrorText>{actionError}</ErrorText>
      {notice && <p role="status">{notice}</p>}

      <button className="button secondary" onClick={share}>
        Condividi
      </button>

      {isAdmin && event.status === "Scheduled" && (
        <Link className="button secondary" href={`/e/${id}/modifica`}>
          Modifica l&apos;uscita
        </Link>
      )}
      {isAdmin && event.status === "Scheduled" && (
        <button
          className="button danger"
          disabled={busy}
          onClick={() => {
            if (confirm("Annullare questa uscita? Chi ha aderito la vedrà come annullata.")) run(() => api(`/events/${id}/cancel`, { method: "POST" }));
          }}
        >
          Annulla l&apos;uscita
        </button>
      )}
    </div>
  );
}

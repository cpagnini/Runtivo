"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api, errorMessage, formatWhen, weekdayLabel, type Dashboard, type DashboardEvent, type SeriesSummary } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { Card, ErrorText, Loading } from "@/components/ui";

function EventRow({ event }: { event: DashboardEvent }) {
  return (
    <li>
      <Link href={`/e/${event.id}`} className="card stack">
        <div className="row">
          <strong>{event.title}</strong>
          {event.status === "Cancelled" ? <span className="badge">Annullata</span> : <span className="badge">{event.going} iscritti</span>}
        </div>
        <span>{formatWhen(event.startsAtUtc)}</span>
        {event.status === "Scheduled" && (
          <span className="muted small">
            {event.groups.map((g) => `${g.name}: ${g.going}${g.capacity != null ? `/${g.capacity}` : ""}`).join(" · ")}
            {event.notGoing > 0 && ` · ${event.notGoing} non partecipano`}
          </span>
        )}
      </Link>
    </li>
  );
}

function SeriesRow({ series, onChange }: { series: SeriesSummary; onChange: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function stop(cancelFutureEvents: boolean) {
    const message = cancelFutureEvents
      ? "Fermare la serie e annullare tutte le uscite future? Chi si è iscritto le vedrà annullate."
      : "Fermare la serie? Le uscite già create restano in calendario.";
    if (!confirm(message)) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/series/${series.id}/stop`, { method: "POST", body: { cancelFutureEvents } });
      onChange();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <li className="card stack">
      <div className="row">
        <strong>{series.title}</strong>
        <span className="badge">{series.enabled ? "Attiva" : "Ferma"}</span>
      </div>
      <span className="muted small">
        Ogni {weekdayLabel[series.weekday]?.toLowerCase()} alle {series.localStartTime} · {series.meetingPointLabel} · {series.groupCount}{" "}
        {series.groupCount === 1 ? "gruppo" : "gruppi"}
        {series.enabled && ` · ${series.upcomingEvents} uscite in calendario`}
      </span>
      {series.enabled && (
        <div className="row wrap">
          <button className="button secondary small" disabled={busy} onClick={() => stop(false)}>
            Ferma la serie
          </button>
          <button className="button danger small" disabled={busy} onClick={() => stop(true)}>
            Ferma e annulla le future
          </button>
        </div>
      )}
      <ErrorText>{error}</ErrorText>
    </li>
  );
}

function ManagePage() {
  const { id } = useParams<{ id: string }>();
  const user = useRequireLogin();
  const { data, error, loading, reload } = useLoad(() => api<Dashboard>(`/clubs/${id}/dashboard`), `dashboard-${id}`, !!user);

  if (!user || loading) return <Loading />;
  if (error || !data)
    return (
      <div className="stack">
        <ErrorText>{error ?? "Impossibile caricare la gestione."}</ErrorText>
        <Link href={`/club/${id}`}>Torna al club</Link>
      </div>
    );

  return (
    <div className="stack">
      <p className="muted small">
        <Link href={`/club/${id}`}>← Torna al club</Link>
      </p>
      <h1>Gestione uscite</h1>
      <div className="row wrap">
        <Link className="button small" href={`/club/${id}/eventi/nuovo`}>
          Nuova uscita
        </Link>
        <Link className="button small secondary" href={`/club/${id}/serie/nuova`}>
          Nuova serie settimanale
        </Link>
      </div>

      <h2>Prossimi 60 giorni</h2>
      {data.upcoming.length === 0 && (
        <Card>
          <p>Nessuna uscita in programma.</p>
          <p className="muted">Crea una serie settimanale per non dover ricreare ogni volta le uscite.</p>
        </Card>
      )}
      <ul className="list">
        {data.upcoming.map((e) => (
          <EventRow key={e.id} event={e} />
        ))}
      </ul>

      <h2>Serie settimanali</h2>
      {data.series.length === 0 && <p className="muted">Nessuna serie.</p>}
      <ul className="list">
        {data.series.map((s) => (
          <SeriesRow key={s.id} series={s} onChange={reload} />
        ))}
      </ul>
    </div>
  );
}

export default function ManageClubPage() {
  return (
    <Suspense fallback={<Loading />}>
      <ManagePage />
    </Suspense>
  );
}

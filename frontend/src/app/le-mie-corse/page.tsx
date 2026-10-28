"use client";

import Link from "next/link";
import { api, formatWhen, type MyEvent } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { ErrorText, Loading } from "@/components/ui";

export default function MyRunsPage() {
  const user = useRequireLogin();
  const { data, error, loading } = useLoad(() => api<MyEvent[]>("/me/events"), "me-events", !!user);

  if (!user || loading) return <Loading />;

  return (
    <div className="stack">
      <h1>Le mie prossime corse</h1>
      <ErrorText>{error}</ErrorText>
      {data?.length === 0 && (
        <div className="card">
          <p>Non hai ancora confermato nessuna uscita.</p>
          <p className="muted">Apri il link di un&apos;uscita che ti hanno condiviso, oppure guarda le prossime uscite dei tuoi club.</p>
          <Link href="/club" className="button secondary">
            Vai ai club
          </Link>
        </div>
      )}
      <ul className="list">
        {data?.map((e) => (
          <li key={e.eventId}>
            <Link href={`/e/${e.eventId}`} className="card stack">
              <div className="row">
                <strong>{e.title}</strong>
                {e.eventStatus === "Cancelled" ? <span className="badge">Annullata</span> : <span className="badge">{e.groupName}</span>}
              </div>
              <span>{formatWhen(e.startsAtUtc)}</span>
              <span className="muted small">
                {e.clubName} · {e.meetingPointLabel}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}

"use client";

import { Suspense, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { adesioni, api, errorMessage, formatWhen, roleLabel, type Club, type EventSummary, type Member, type Role } from "@/lib/api";
import { useSession } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { Card, ErrorText, Loading } from "@/components/ui";

type Invitation = { id: string; token: string; link: string; expiresAt: string };

function InviteForm({ clubId }: { clubId: string }) {
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [invitation, setInvitation] = useState<Invitation | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setCopied(false);
    try {
      setInvitation(await api<Invitation>(`/clubs/${clubId}/invitations`, { method: "POST", body: { email: email || null } }));
      setEmail("");
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function copy() {
    if (!invitation) return;
    try {
      await navigator.clipboard.writeText(invitation.link);
      setCopied(true);
    } catch {
      setError("Copia non riuscita: seleziona il link e copialo a mano.");
    }
  }

  return (
    <Card>
      <form className="stack" onSubmit={submit}>
        <h2>Invita qualcuno</h2>
        <label>
          Email (facoltativa)
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="Lascia vuoto per un link da condividere" />
          <span className="hint">Con l&apos;email, solo quella persona potrà usare l&apos;invito. Vale 7 giorni e una sola volta.</span>
        </label>
        <ErrorText>{error}</ErrorText>
        <button className="button" disabled={busy}>
          {busy ? "Creazione…" : "Crea invito"}
        </button>
      </form>
      {invitation && (
        <div className="stack invite-result">
          <p>
            Invito creato. Il link si vede solo adesso: copialo e mandalo, ad esempio su WhatsApp.
          </p>
          <input readOnly value={invitation.link} onFocus={(e) => e.currentTarget.select()} aria-label="Link di invito" />
          <button className="button secondary" onClick={copy}>
            {copied ? "Copiato" : "Copia link"}
          </button>
        </div>
      )}
    </Card>
  );
}

function MemberRow({ clubId, member, isMe, onChange }: { clubId: string; member: Member; isMe: boolean; onChange: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChange();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const base = `/clubs/${clubId}/memberships/${member.userId}`;
  return (
    <li className="card stack">
      <div className="row">
        <span>
          <strong>{member.displayName}</strong>
          {isMe && <span className="muted"> (tu)</span>}
          <br />
          <span className="muted small">{member.email}</span>
        </span>
        {member.status === "Pending" && <span className="badge">In attesa</span>}
      </div>
      <div className="row wrap">
        {member.status === "Pending" ? (
          <button className="button small" disabled={busy} onClick={() => run(() => api(`${base}/approve`, { method: "POST" }))}>
            Approva
          </button>
        ) : (
          <label className="inline">
            Ruolo
            <select
              value={member.role}
              disabled={busy}
              onChange={(e) => run(() => api(`${base}/role`, { method: "PUT", body: { role: e.target.value as Role } }))}
            >
              {(Object.keys(roleLabel) as Role[]).map((r) => (
                <option key={r} value={r}>
                  {roleLabel[r]}
                </option>
              ))}
            </select>
          </label>
        )}
        {!isMe && (
          <button
            className="button small danger"
            disabled={busy}
            onClick={() => {
              const what = member.status === "Pending" ? "Rifiutare la richiesta di" : "Rimuovere dal club";
              if (confirm(`${what} ${member.displayName}?`)) run(() => api(base, { method: "DELETE" }));
            }}
          >
            {member.status === "Pending" ? "Rifiuta" : "Rimuovi"}
          </button>
        )}
      </div>
      <ErrorText>{error}</ErrorText>
    </li>
  );
}

function AdminPanel({ club, myId }: { club: Club; myId: string }) {
  const { data, error, reload } = useLoad(() => api<Member[]>(`/clubs/${club.id}/members`), `members-${club.id}`);
  const members = [...(data ?? [])].sort((a, b) => Number(b.status === "Pending") - Number(a.status === "Pending"));

  return (
    <>
      <InviteForm clubId={club.id} />
      <h2>Membri</h2>
      <ErrorText>{error}</ErrorText>
      {!data && !error && <Loading />}
      <ul className="list">
        {members.map((m) => (
          <MemberRow key={m.userId} clubId={club.id} member={m} isMe={m.userId === myId} onChange={reload} />
        ))}
      </ul>
    </>
  );
}

function UpcomingEvents({ clubId, isAdmin }: { clubId: string; isAdmin: boolean }) {
  const { data, error, loading } = useLoad(() => api<EventSummary[]>(`/clubs/${clubId}/events`), `events-${clubId}`);

  return (
    <>
      <div className="row">
        <h2>Prossime uscite</h2>
        {isAdmin && (
          <Link className="button small" href={`/club/${clubId}/gestione`}>
            Gestione
          </Link>
        )}
      </div>
      <ErrorText>{error}</ErrorText>
      {loading && <Loading />}
      {data?.length === 0 && <p className="muted">Nessuna uscita in programma.</p>}
      <ul className="list">
        {data?.map((e) => (
          <li key={e.id}>
            <Link href={`/e/${e.id}`} className="card stack">
              <div className="row">
                <strong>{e.title}</strong>
                {e.status === "Cancelled" ? <span className="badge">Annullata</span> : e.visibility === "ClubOnly" && <span className="badge">Solo membrə</span>}
              </div>
              <span>{formatWhen(e.startsAtUtc)}</span>
              <span className="muted small">
                {e.meetingPointLabel} · {e.groupCount} {e.groupCount === 1 ? "gruppo" : "gruppi"} · {adesioni(e.goingCount)}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}

function ClubView() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const { status, user } = useSession();
  // Il club è pubblico: si legge anche senza accesso; ruolo e stato dipendono dalla sessione.
  const { data: club, error, loading, reload } = useLoad(() => api<Club>(`/clubs/${id}`), `club-${id}-${status}`, status !== "loading");
  const [busy, setBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  if (loading || status === "loading") return <Loading />;
  if (error || !club) return <ErrorText>{error ?? "Club non trovato."}</ErrorText>;

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setActionError(null);
    try {
      await action();
      reload();
    } catch (err) {
      setActionError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const isAdmin = club.myRole === "Admin" && club.myStatus === "Active";

  return (
    <div className="stack">
      <div>
        <h1>{club.name}</h1>
        {club.city && <p className="muted">{club.city}</p>}
      </div>
      {club.description && <p>{club.description}</p>}

      <Card>
        {status !== "authenticated" || !user ? (
          <div className="stack">
            <p>Accedi per chiedere di entrare in questo club.</p>
            <Link className="button" href={`/accedi?next=${encodeURIComponent(`/club/${club.id}`)}`}>
              Accedi
            </Link>
          </div>
        ) : club.myStatus === null ? (
          <div className="stack">
            <p>Non fai ancora parte di questo club.</p>
            <button
              className="button"
              disabled={busy}
              onClick={() => run(() => api(`/clubs/${club.id}/memberships`, { method: "POST", body: {} }))}
            >
              Chiedi di entrare
            </button>
          </div>
        ) : club.myStatus === "Pending" ? (
          <div className="stack">
            <p>Richiesta inviata: unə amministratorə la valuterà.</p>
            <button
              className="button secondary"
              disabled={busy}
              onClick={() => run(() => api(`/clubs/${club.id}/memberships/${user.id}`, { method: "DELETE" }))}
            >
              Ritira la richiesta
            </button>
          </div>
        ) : (
          <div className="row wrap">
            <span>
              Fai parte del club come <strong>{roleLabel[club.myRole!].toLowerCase()}</strong>.
            </span>
            <button
              className="button secondary small"
              disabled={busy}
              onClick={() => {
                if (!confirm(`Vuoi uscire da ${club.name}?`)) return;
                run(async () => {
                  await api(`/clubs/${club.id}/memberships/${user.id}`, { method: "DELETE" });
                  router.push("/club");
                });
              }}
            >
              Esci dal club
            </button>
          </div>
        )}
        <ErrorText>{actionError}</ErrorText>
      </Card>

      <UpcomingEvents clubId={club.id} isAdmin={isAdmin} />

      {isAdmin && user && <AdminPanel club={club} myId={user.id} />}
    </div>
  );
}

export default function ClubPage() {
  return (
    <Suspense fallback={<Loading />}>
      <ClubView />
    </Suspense>
  );
}

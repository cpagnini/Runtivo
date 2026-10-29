"use client";

import { useState } from "react";
import Link from "next/link";
import {
  api,
  ApiError,
  errorMessage,
  formatDistance,
  formatPaceRange,
  formatWhen,
  runStatusLabel,
  runTypeLabel,
  slotLabel,
  type Intent,
  type Match,
  type Place,
  type RunSummary,
} from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { IntentForm } from "@/components/IntentForm";
import { MatchCard } from "@/components/MatchCard";
import { Card, ErrorText, Loading } from "@/components/ui";

type Loaded = { intent: Intent | null; places: Place[] };

async function loadIntentAndPlaces(): Promise<Loaded> {
  const places = await api<Place[]>("/places");
  try {
    return { intent: await api<Intent>("/me/intent"), places };
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return { intent: null, places };
    throw e;
  }
}

function MyRuns() {
  const { data } = useLoad(() => api<RunSummary[]>("/me/runs"), "my-runs");
  if (!data || data.length === 0) return null;

  return (
    <>
      <h2>Le tue uscite</h2>
      <ul className="list">
        {data.map((r) => (
          <li key={r.id}>
            <Link href={`/uscite/${r.id}`} className="card stack">
              <div className="row">
                <strong>{formatWhen(r.startsAtUtc)}</strong>
                <span className="badge">{r.myStatus === "Pending" ? "Aspetta la tua risposta" : runStatusLabel[r.status]}</span>
              </div>
              <span className="muted small">
                {r.placeName}, {r.city} · {r.activeParticipants} {r.activeParticipants === 1 ? "persona" : "persone"}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </>
  );
}

function Matches({ intent }: { intent: Intent }) {
  const { data, error, loading, reload } = useLoad(() => api<Match[]>("/me/matches?take=50"), `matches-${intent.status}-${intent.expiresAt}`);

  if (loading) return <Loading />;
  return (
    <>
      <h2>Persone compatibili</h2>
      <ErrorText>{error}</ErrorText>
      {data?.length === 0 && (
        <Card>
          <p>Per ora nessuno è compatibile con il tuo annuncio.</p>
          <p className="muted">Prova ad allargare il raggio, a indicare più giorni o fasce orarie, oppure a far girare Runtivo tra chi conosci.</p>
        </Card>
      )}
      <ul className="list">
        {data?.map((m) => (
          <MatchCard key={m.userId} match={m} onChanged={reload} />
        ))}
      </ul>
    </>
  );
}

function Summary({ intent, onChanged, onEdit }: { intent: Intent; onChanged: () => void; onEdit: () => void }) {
  const [now] = useState(() => Date.now());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onChanged();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const expired = new Date(intent.expiresAt).getTime() <= now;
  const km = (a: number, b: number) => (a === b ? formatDistance(a) : `${formatDistance(a).replace(" km", "")}–${formatDistance(b)}`);

  return (
    <Card>
      <div className="stack">
        <div className="row">
          <h2>Il tuo annuncio</h2>
          <span className="badge">{expired ? "Scaduto" : intent.status === "Active" ? "Visibile" : "In pausa"}</span>
        </div>
        <p>
          {formatPaceRange(intent.paceMinSecPerKm, intent.paceMaxSecPerKm)} · {km(intent.distanceMinMeters, intent.distanceMaxMeters)} ·{" "}
          {runTypeLabel[intent.runType].toLowerCase()}
        </p>
        <p className="muted small">
          Parti da {intent.homePlaceName} ({intent.city}), fino a {intent.radiusKm} km.
          {intent.allowPairs ? " Anche in coppia." : " Solo gruppi di 3 o più."}
        </p>
        <p className="muted small">Disponibilità: {intent.availability.map(slotLabel).join(", ")}</p>
        {intent.note && <p className="quote">{intent.note}</p>}
        <p className="muted small">Scade il {new Date(intent.expiresAt).toLocaleDateString("it-IT")}.</p>
        <ErrorText>{error}</ErrorText>
        <div className="row wrap">
          <button className="button secondary small" onClick={onEdit} disabled={busy}>
            Modifica
          </button>
          {intent.status === "Active" && !expired ? (
            <button className="button secondary small" disabled={busy} onClick={() => run(() => api("/me/intent/pause", { method: "POST" }))}>
              Metti in pausa
            </button>
          ) : (
            <button className="button small" disabled={busy} onClick={() => run(() => api("/me/intent/resume", { method: "POST" }))}>
              {expired ? "Riattiva per altri 30 giorni" : "Riattiva"}
            </button>
          )}
          <button
            className="button danger small"
            disabled={busy}
            onClick={() => {
              if (confirm("Eliminare il tuo annuncio? Non comparirai più tra le persone compatibili.")) run(() => api("/me/intent", { method: "DELETE" }));
            }}
          >
            Elimina
          </button>
        </div>
      </div>
    </Card>
  );
}

function CompanionsContent({ data, reload, needsName }: { data: Loaded; reload: () => void; needsName: boolean }) {
  const [editing, setEditing] = useState(false);
  // Letto qui, dove il componente esiste solo dopo il caricamento: durante il pre-rendering non c'è un "adesso".
  const [now] = useState(() => Date.now());

  const { intent, places } = data;
  const visible = intent && intent.status === "Active" && new Date(intent.expiresAt).getTime() > now;

  return (
    <div className="stack">
      <h1>Trova compagnia per correre</h1>
      {!intent && (
        <p className="muted">
          Dicci come corri e quando puoi: ti mostriamo le persone più compatibili, vicino a te. Le uscite si fanno sempre in luoghi pubblici.
        </p>
      )}
      {needsName && !intent && (
        <p className="banner" role="alert">
          Il tuo nome visibile è ancora quello ricavato dall&apos;email. Prima di pubblicare scegli il nome che vuoi mostrare agli altri nel{" "}
          <Link href="/profilo">profilo</Link>.
        </p>
      )}
      {!intent || editing ? (
        <IntentForm
          initial={intent}
          places={places}
          onCancel={editing ? () => setEditing(false) : undefined}
          onSaved={() => {
            setEditing(false);
            reload();
          }}
        />
      ) : (
        <>
          <MyRuns />
          <Summary intent={intent} onChanged={reload} onEdit={() => setEditing(true)} />
          {visible ? <Matches intent={intent} /> : <p className="muted">Riattiva l&apos;annuncio per vedere le persone compatibili.</p>}
        </>
      )}
    </div>
  );
}

export default function CompanionsPage() {
  const user = useRequireLogin();
  const { data, error, loading, reload } = useLoad(loadIntentAndPlaces, "intent", !!user);

  if (!user || loading) return <Loading />;
  if (error || !data) return <ErrorText>{error ?? "Impossibile caricare la pagina."}</ErrorText>;
  const needsName = user.displayName.toLowerCase() === user.email.split("@")[0].toLowerCase();
  return <CompanionsContent data={data} reload={reload} needsName={needsName} />;
}

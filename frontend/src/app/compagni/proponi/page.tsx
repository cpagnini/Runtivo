"use client";

import { Suspense, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { api, errorMessage, type Match, type Place } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { ErrorText, Loading } from "@/components/ui";

const MAX_INVITEES = 7;

function ProposeForm({ matches, places, preselected }: { matches: Match[]; places: Place[]; preselected: string | null }) {
  const router = useRouter();
  const [selected, setSelected] = useState<Set<string>>(() => new Set(preselected && matches.some((m) => m.userId === preselected) ? [preselected] : []));
  const [placeId, setPlaceId] = useState(places[0]?.id ?? "");
  const [startsAt, setStartsAt] = useState("");
  const [km, setKm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const cities = [...new Set(places.map((p) => p.city))];
  const chosen = matches.filter((m) => selected.has(m.userId));
  const pairBlocked = chosen.length === 1 && !chosen[0].canMeetAsPair;

  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else if (next.size < MAX_INVITEES) next.add(id);
      return next;
    });

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (chosen.length === 0) return setError("Scegli almeno una persona da invitare.");
    const distance = km.trim() ? Number(km.replace(",", ".")) : null;
    if (distance !== null && !(distance > 0)) return setError("La distanza non è valida.");

    setBusy(true);
    try {
      // L'orario inserito è quello del dispositivo (in Italia: Europe/Rome).
      const created = await api<{ id: string }>("/runs", {
        method: "POST",
        body: {
          placeId,
          startsAt: new Date(startsAt).toISOString(),
          distanceMeters: distance === null ? null : Math.round(distance * 1000),
          inviteeIds: chosen.map((m) => m.userId),
        },
      });
      router.push(`/uscite/${created.id}`);
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <p className="muted small">
        <Link href="/compagni">← Compagnia</Link>
      </p>
      <h1>Proponi un&apos;uscita</h1>
      <p className="muted">Chi inviti dovrà confermare giorno, ora e luogo. L&apos;uscita è confermata solo quando hanno risposto tuttə.</p>

      <fieldset className="slots">
        <legend>Chi inviti (fino a {MAX_INVITEES})</legend>
        {matches.length === 0 && <p className="muted">Non ci sono persone compatibili da invitare, per ora.</p>}
        <div className="stack">
          {matches.map((m) => (
            <label key={m.userId} className="check">
              <input type="checkbox" checked={selected.has(m.userId)} onChange={() => toggle(m.userId)} />
              <span>
                <strong>{m.displayName}</strong> · {m.city}, a circa {m.approxDistanceKm} km{!m.canMeetAsPair && " · preferisce i gruppi"}
              </span>
            </label>
          ))}
        </div>
      </fieldset>
      {pairBlocked && <p className="banner">Una di voi due preferisce le uscite di gruppo: invita almeno un&apos;altra persona.</p>}

      <label>
        Luogo di ritrovo
        <select value={placeId} onChange={(e) => setPlaceId(e.target.value)}>
          {cities.map((city) => (
            <optgroup key={city} label={city}>
              {places
                .filter((p) => p.city === city)
                .map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
            </optgroup>
          ))}
        </select>
        <span className="hint">Solo luoghi pubblici.</span>
      </label>
      <label>
        Data e ora
        <input type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
      </label>
      <label>
        Distanza prevista, km (facoltativa)
        <input inputMode="decimal" value={km} onChange={(e) => setKm(e.target.value)} placeholder="8" />
      </label>

      <ErrorText>{error}</ErrorText>
      <button className="button" disabled={busy || pairBlocked}>
        {busy ? "Invio…" : "Invia la proposta"}
      </button>
    </form>
  );
}

function ProposePage() {
  const user = useRequireLogin();
  const preselected = useSearchParams().get("con");
  const { data, error, loading } = useLoad(
    async () => ({ matches: await api<Match[]>("/me/matches?take=50"), places: await api<Place[]>("/places") }),
    "propose",
    !!user,
  );

  if (!user || loading) return <Loading />;
  if (error || !data)
    return (
      <div className="stack">
        <ErrorText>{error ?? "Impossibile caricare la pagina."}</ErrorText>
        <Link href="/compagni">Torna a Compagnia</Link>
      </div>
    );
  return <ProposeForm matches={data.matches} places={data.places} preselected={preselected} />;
}

export default function ProposeRunPage() {
  return (
    <Suspense fallback={<Loading />}>
      <ProposePage />
    </Suspense>
  );
}

"use client";

import { Suspense, useState, type FormEvent } from "react";
import { useParams, useRouter } from "next/navigation";
import { api, errorMessage, parsePace, type Visibility } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { ErrorText, Loading } from "@/components/ui";

type GroupForm = { name: string; km: string; paceMin: string; paceMax: string; capacity: string };

const emptyGroup = (name = ""): GroupForm => ({ name, km: "", paceMin: "", paceMax: "", capacity: "" });
const MAX_GROUPS = 8;

function NewEventForm() {
  const user = useRequireLogin();
  const { id: clubId } = useParams<{ id: string }>();
  const router = useRouter();
  const [title, setTitle] = useState("");
  const [meetingPoint, setMeetingPoint] = useState("");
  const [startsAt, setStartsAt] = useState("");
  const [visibility, setVisibility] = useState<Visibility>("Public");
  const [description, setDescription] = useState("");
  const [groups, setGroups] = useState<GroupForm[]>([emptyGroup("Gruppo 1")]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!user) return <Loading />;

  const update = (i: number, patch: Partial<GroupForm>) =>
    setGroups((gs) => gs.map((g, j) => (j === i ? { ...g, ...patch } : g)));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    const payloadGroups = [];
    for (const [i, g] of groups.entries()) {
      const min = parsePace(g.paceMin);
      const max = parsePace(g.paceMax);
      if (min === undefined || max === undefined) return setError(`Gruppo ${i + 1}: scrivi il passo come minuti:secondi, ad esempio 5:30.`);
      const km = g.km.trim() ? Number(g.km.replace(",", ".")) : null;
      if (km !== null && !(km > 0)) return setError(`Gruppo ${i + 1}: la distanza non è valida.`);
      payloadGroups.push({
        name: g.name,
        distanceMeters: km === null ? null : Math.round(km * 1000),
        paceMinSecPerKm: min,
        paceMaxSecPerKm: max ?? min,
        capacity: g.capacity.trim() ? Number(g.capacity) : null,
      });
    }

    setBusy(true);
    try {
      // L'orario inserito è quello del dispositivo (in Italia: Europe/Rome).
      const created = await api<{ id: string }>(`/clubs/${clubId}/events`, {
        method: "POST",
        body: {
          title,
          description,
          meetingPointLabel: meetingPoint,
          startsAt: new Date(startsAt).toISOString(),
          visibility,
          groups: payloadGroups,
        },
      });
      router.push(`/e/${created.id}`);
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <h1>Nuova uscita</h1>
      <label>
        Titolo
        <input required minLength={2} maxLength={100} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Martedì Running" />
      </label>
      <label>
        Data e ora
        <input type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
      </label>
      <label>
        Luogo di ritrovo
        <input required minLength={2} maxLength={120} value={meetingPoint} onChange={(e) => setMeetingPoint(e.target.value)} placeholder="Piazza del Comune" />
        <span className="hint">Un luogo pubblico, mai un indirizzo privato.</span>
      </label>
      <label>
        Chi può vederla
        <select value={visibility} onChange={(e) => setVisibility(e.target.value as Visibility)}>
          <option value="Public">Chiunque abbia il link</option>
          <option value="ClubOnly">Solo i membri del club</option>
        </select>
      </label>
      <label>
        Note (facoltative)
        <textarea rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>

      <h2>Gruppi di passo</h2>
      {groups.map((g, i) => (
        <fieldset key={i} className="card group-form">
          <legend className="small muted">Gruppo {i + 1}</legend>
          <label>
            Nome
            <input required maxLength={40} value={g.name} onChange={(e) => update(i, { name: e.target.value })} placeholder="Relax" />
          </label>
          <div className="two-cols">
            <label>
              Distanza (km)
              <input inputMode="decimal" value={g.km} onChange={(e) => update(i, { km: e.target.value })} placeholder="6" />
            </label>
            <label>
              Posti (facoltativo)
              <input inputMode="numeric" value={g.capacity} onChange={(e) => update(i, { capacity: e.target.value })} placeholder="illimitati" />
            </label>
            <label>
              Passo da (min/km)
              <input inputMode="numeric" value={g.paceMin} onChange={(e) => update(i, { paceMin: e.target.value })} placeholder="6:15" />
            </label>
            <label>
              Passo a (min/km)
              <input inputMode="numeric" value={g.paceMax} onChange={(e) => update(i, { paceMax: e.target.value })} placeholder="6:45" />
            </label>
          </div>
          {groups.length > 1 && (
            <button type="button" className="button danger small" onClick={() => setGroups((gs) => gs.filter((_, j) => j !== i))}>
              Rimuovi gruppo
            </button>
          )}
        </fieldset>
      ))}
      {groups.length < MAX_GROUPS && (
        <button type="button" className="button secondary" onClick={() => setGroups((gs) => [...gs, emptyGroup(`Gruppo ${gs.length + 1}`)])}>
          Aggiungi un gruppo
        </button>
      )}

      <ErrorText>{error}</ErrorText>
      <button className="button" disabled={busy}>
        {busy ? "Creazione…" : "Crea l'uscita"}
      </button>
    </form>
  );
}

export default function NewEventPage() {
  return (
    <Suspense fallback={<Loading />}>
      <NewEventForm />
    </Suspense>
  );
}

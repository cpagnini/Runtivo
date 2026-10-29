"use client";

import { Suspense, useState, type FormEvent } from "react";
import { useParams, useRouter } from "next/navigation";
import { api, errorMessage, type Visibility } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { emptyGroup, GroupsEditor, groupsToPayload, type GroupForm } from "@/components/GroupsEditor";
import { ErrorText, Loading } from "@/components/ui";

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

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const converted = groupsToPayload(groups);
    if ("error" in converted) return setError(converted.error);

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
          groups: converted.payload,
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
          <option value="ClubOnly">Solo lə membrə del club</option>
        </select>
      </label>
      <label>
        Note (facoltative)
        <textarea rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>

      <h2>Gruppi di passo</h2>
      <GroupsEditor groups={groups} onChange={setGroups} />

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

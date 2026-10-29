"use client";

import { Suspense, useState, type FormEvent } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { api, errorMessage, toLocalInput, type RunEvent, type Visibility } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { groupsToPayload, GroupsEditor, groupToForm, type GroupForm } from "@/components/GroupsEditor";
import { ErrorText, Loading } from "@/components/ui";

function EditForm({ event }: { event: RunEvent }) {
  const router = useRouter();
  const [title, setTitle] = useState(event.title);
  const [meetingPoint, setMeetingPoint] = useState(event.meetingPointLabel);
  const [startsAt, setStartsAt] = useState(toLocalInput(event.startsAtUtc));
  const [visibility, setVisibility] = useState<Visibility>(event.visibility);
  const [description, setDescription] = useState(event.description ?? "");
  const [groups, setGroups] = useState<GroupForm[]>(event.groups.map(groupToForm));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const converted = groupsToPayload(groups);
    if ("error" in converted) return setError(converted.error);

    setBusy(true);
    try {
      await api(`/events/${event.id}`, {
        method: "PATCH",
        body: {
          title,
          description,
          meetingPointLabel: meetingPoint,
          visibility,
          startsAt: new Date(startsAt).toISOString(),
        },
      });
      // I gruppi si aggiornano uno alla volta; se uno fallisce (ad esempio capienza sotto gli iscritti) ci si ferma lì.
      for (const [i, g] of groups.entries()) {
        try {
          await api(`/events/${event.id}/groups/${g.id}`, { method: "PUT", body: converted.payload[i] });
        } catch (err) {
          throw new Error(`Gruppo ${i + 1}: ${errorMessage(err)} Le altre modifiche sono state salvate.`);
        }
      }
      router.push(`/e/${event.id}`);
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <p className="muted small">
        <Link href={`/e/${event.id}`}>← Torna all&apos;uscita</Link>
      </p>
      <h1>Modifica uscita</h1>
      {event.seriesId && <p className="muted">Fa parte di una serie: la modifica vale solo per questa data, le altre non cambiano.</p>}
      <label>
        Titolo
        <input required minLength={2} maxLength={100} value={title} onChange={(e) => setTitle(e.target.value)} />
      </label>
      <label>
        Data e ora
        <input type="datetime-local" required value={startsAt} onChange={(e) => setStartsAt(e.target.value)} />
      </label>
      <label>
        Luogo di ritrovo
        <input required minLength={2} maxLength={120} value={meetingPoint} onChange={(e) => setMeetingPoint(e.target.value)} />
      </label>
      <label>
        Chi può vederla
        <select value={visibility} onChange={(e) => setVisibility(e.target.value as Visibility)}>
          <option value="Public">Chiunque abbia il link</option>
          <option value="ClubOnly">Solo lə membrə del club</option>
        </select>
      </label>
      <label>
        Note
        <textarea rows={3} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>
      <h2>Gruppi di passo</h2>
      <GroupsEditor groups={groups} onChange={setGroups} canAddRemove={false} />
      <ErrorText>{error}</ErrorText>
      <button className="button" disabled={busy}>
        {busy ? "Salvataggio…" : "Salva le modifiche"}
      </button>
    </form>
  );
}

function EditPage() {
  const { id } = useParams<{ id: string }>();
  const user = useRequireLogin();
  const { data, error, loading } = useLoad(() => api<RunEvent>(`/events/${id}`), `edit-${id}`, !!user);

  if (!user || loading) return <Loading />;
  if (error || !data) return <ErrorText>{error ?? "Evento non trovato."}</ErrorText>;
  if (data.myRole !== "Admin") return <ErrorText>Solo chi amministra il club può modificare l&apos;uscita.</ErrorText>;
  if (data.status === "Cancelled") return <ErrorText>L&apos;uscita è annullata e non si può modificare.</ErrorText>;
  return <EditForm event={data} />;
}

export default function EditEventPage() {
  return (
    <Suspense fallback={<Loading />}>
      <EditPage />
    </Suspense>
  );
}

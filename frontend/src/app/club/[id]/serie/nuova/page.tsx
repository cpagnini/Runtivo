"use client";

import { Suspense, useState, type FormEvent } from "react";
import { useParams, useRouter } from "next/navigation";
import { api, errorMessage, type Visibility } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { emptyGroup, GroupsEditor, groupsToPayload, type GroupForm } from "@/components/GroupsEditor";
import { ErrorText, Loading } from "@/components/ui";

const weekdays = [
  ["Monday", "Lunedì"],
  ["Tuesday", "Martedì"],
  ["Wednesday", "Mercoledì"],
  ["Thursday", "Giovedì"],
  ["Friday", "Venerdì"],
  ["Saturday", "Sabato"],
  ["Sunday", "Domenica"],
] as const;

const todayIso = () => new Date().toLocaleDateString("sv-SE");

function NewSeriesForm() {
  const user = useRequireLogin();
  const { id: clubId } = useParams<{ id: string }>();
  const router = useRouter();
  const [title, setTitle] = useState("");
  const [weekday, setWeekday] = useState<string>("Tuesday");
  const [time, setTime] = useState("19:00");
  const [startDate, setStartDate] = useState(todayIso);
  const [endDate, setEndDate] = useState("");
  const [meetingPoint, setMeetingPoint] = useState("");
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
      await api(`/clubs/${clubId}/series`, {
        method: "POST",
        body: {
          title,
          description,
          meetingPointLabel: meetingPoint,
          weekday,
          localStartTime: time,
          startDate,
          endDate: endDate || null,
          visibility,
          groups: converted.payload,
        },
      });
      router.push(`/club/${clubId}/gestione`);
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <h1>Nuova serie settimanale</h1>
      <p className="muted">Creiamo le uscite delle prossime 8 settimane e poi continuiamo ogni giorno in automatico. Ogni data si può modificare o annullare da sola.</p>
      <label>
        Titolo
        <input required minLength={2} maxLength={100} value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Martedì Running" />
      </label>
      <div className="two-cols">
        <label>
          Giorno
          <select value={weekday} onChange={(e) => setWeekday(e.target.value)}>
            {weekdays.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Ora
          <input type="time" required value={time} onChange={(e) => setTime(e.target.value)} />
        </label>
        <label>
          Dal
          <input type="date" required value={startDate} onChange={(e) => setStartDate(e.target.value)} />
        </label>
        <label>
          Fino al (facoltativo)
          <input type="date" value={endDate} min={startDate} onChange={(e) => setEndDate(e.target.value)} />
        </label>
      </div>
      <p className="hint">L&apos;orario è quello italiano e resta lo stesso anche quando cambia l&apos;ora legale.</p>
      <label>
        Luogo di ritrovo
        <input required minLength={2} maxLength={120} value={meetingPoint} onChange={(e) => setMeetingPoint(e.target.value)} placeholder="Piazza del Comune" />
      </label>
      <label>
        Chi può vederle
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
        {busy ? "Creazione…" : "Crea la serie"}
      </button>
    </form>
  );
}

export default function NewSeriesPage() {
  return (
    <Suspense fallback={<Loading />}>
      <NewSeriesForm />
    </Suspense>
  );
}

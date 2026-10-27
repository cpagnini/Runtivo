"use client";

import { useState, type FormEvent } from "react";
import { api, errorMessage, formatPace, parsePace, type User } from "@/lib/api";
import { useRequireLogin, useSession } from "@/components/Session";
import { ErrorText, Loading } from "@/components/ui";

function ProfileForm({ user }: { user: User }) {
  const { setUser } = useSession();
  const [displayName, setDisplayName] = useState(user.displayName);
  const [city, setCity] = useState(user.city ?? "");
  const [pace, setPace] = useState(formatPace(user.preferredPaceSecPerKm));
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setSaved(false);
    setError(null);
    const parsed = parsePace(pace);
    if (parsed === undefined) return setError("Scrivi il passo come minuti:secondi, ad esempio 5:30.");

    setBusy(true);
    try {
      // Il backend non permette ancora di cancellare un passo già impostato: se vuoto, non lo inviamo.
      const updated = await api<User>("/me", {
        method: "PATCH",
        body: { displayName, city, ...(parsed !== null ? { preferredPaceSecPerKm: parsed } : {}) },
      });
      setUser(updated);
      setSaved(true);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <h1>Il tuo profilo</h1>
      <p className="muted">{user.email}</p>
      <label>
        Nome visibile
        <input required maxLength={60} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
      </label>
      <label>
        Località (facoltativa)
        <input maxLength={80} value={city} onChange={(e) => setCity(e.target.value)} placeholder="Prato" />
      </label>
      <label>
        Passo indicativo, min/km (facoltativo)
        <input value={pace} onChange={(e) => setPace(e.target.value)} placeholder="5:30" inputMode="numeric" />
        <span className="hint">Serve solo a orientarti nella scelta del gruppo, non è un giudizio.</span>
      </label>
      <ErrorText>{error}</ErrorText>
      {saved && <p role="status">Profilo aggiornato.</p>}
      <button className="button" disabled={busy}>
        {busy ? "Salvataggio…" : "Salva"}
      </button>
    </form>
  );
}

export default function ProfilePage() {
  const user = useRequireLogin();
  return user ? <ProfileForm user={user} /> : <Loading />;
}

"use client";

import { useState, type FormEvent } from "react";
import { api, errorMessage, formatPace, parsePace, type User } from "@/lib/api";
import { useRequireLogin, useSession } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { Card, ErrorText, Loading } from "@/components/ui";

type Prefs = { eventChangesEmail: boolean; remindersEmail: boolean; discoveryEmails: boolean };

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
      const updated = await api<User>("/me", {
        method: "PATCH",
        body: {
          displayName,
          city,
          ...(parsed !== null ? { preferredPaceSecPerKm: parsed } : { clearPreferredPace: true }),
        },
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
        <span className="hint">Serve solo a orientarti nella scelta del gruppo, non è un giudizio. Lascia vuoto per non indicarlo.</span>
      </label>
      <ErrorText>{error}</ErrorText>
      {saved && <p role="status">Profilo aggiornato.</p>}
      <button className="button" disabled={busy}>
        {busy ? "Salvataggio…" : "Salva"}
      </button>
    </form>
  );
}

function NotificationsForm() {
  const { data, error: loadError, loading } = useLoad(() => api<Prefs>("/me/notifications"), "prefs");
  const [edited, setEdited] = useState<Prefs | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (loading) return <Loading />;
  const prefs = edited ?? data;
  if (!prefs) return <ErrorText>{loadError}</ErrorText>;

  const toggle = (key: keyof Prefs) => {
    setSaved(false);
    setEdited({ ...prefs, [key]: !prefs[key] });
  };

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/me/notifications", { method: "PUT", body: prefs });
      setSaved(true);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <form className="stack" onSubmit={submit}>
        <h2>Comunicazioni</h2>
        <p className="muted small">Scegli tu cosa ricevere. Per ora salviamo le preferenze: le email partiranno quando il servizio di invio sarà attivo.</p>
        <label className="check">
          <input type="checkbox" checked={prefs.eventChangesEmail} onChange={() => toggle("eventChangesEmail")} />
          <span>Cambi e annullamenti delle uscite a cui partecipo</span>
        </label>
        <label className="check">
          <input type="checkbox" checked={prefs.remindersEmail} onChange={() => toggle("remindersEmail")} />
          <span>Promemoria prima delle uscite</span>
        </label>
        <label className="check">
          <input type="checkbox" checked={prefs.discoveryEmails} onChange={() => toggle("discoveryEmails")} />
          <span>Novità e nuove uscite nella mia zona (facoltativo)</span>
        </label>
        <ErrorText>{error}</ErrorText>
        {saved && <p role="status">Preferenze salvate.</p>}
        <button className="button secondary" disabled={busy}>
          {busy ? "Salvataggio…" : "Salva le preferenze"}
        </button>
      </form>
    </Card>
  );
}

function PrivacySection({ user }: { user: User }) {
  const [confirmEmail, setConfirmEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function download() {
    setBusy(true);
    setError(null);
    try {
      const data = await api<unknown>("/me/export");
      const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = "runtivo-i-miei-dati.json";
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function remove(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/me", { method: "DELETE", body: { confirmEmail } });
      // Ricaricamento completo: azzera la sessione in memoria senza passare dalla guardia di login.
      // eslint-disable-next-line @next/next/no-location-assign-relative-destination
      window.location.href = "/";
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <Card>
      <div className="stack">
        <h2>I tuoi dati</h2>
        <p className="muted small">Puoi scaricare tutto ciò che conserviamo su di te, oppure eliminare l&apos;account.</p>
        <button className="button secondary" onClick={download} disabled={busy}>
          Scarica i miei dati
        </button>
        <details>
          <summary>Elimina il mio account</summary>
          <form className="stack delete-form" onSubmit={remove}>
            <p>
              Verranno cancellati il tuo profilo, le iscrizioni ai club e alle uscite. Non si può annullare. Se sei l&apos;unica persona che amministra un club con altrə
              membrə, assegna prima il ruolo a qualcunə altrə.
            </p>
            <label>
              Per confermare scrivi la tua email ({user.email})
              <input type="email" required value={confirmEmail} onChange={(e) => setConfirmEmail(e.target.value)} autoComplete="off" />
            </label>
            <button className="button danger" disabled={busy}>
              Elimina definitivamente
            </button>
          </form>
        </details>
        <ErrorText>{error}</ErrorText>
      </div>
    </Card>
  );
}

export default function ProfilePage() {
  const user = useRequireLogin();
  if (!user) return <Loading />;
  return (
    <div className="stack">
      <ProfileForm user={user} />
      <NotificationsForm />
      <PrivacySection user={user} />
    </div>
  );
}

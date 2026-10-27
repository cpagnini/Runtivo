"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { api, errorMessage, type Club } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { ErrorText, Loading } from "@/components/ui";

export default function NewClubPage() {
  const user = useRequireLogin();
  const router = useRouter();
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [city, setCity] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!user) return <Loading />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const club = await api<Club>("/clubs", { method: "POST", body: { name, description, city } });
      router.push(`/club/${club.id}`);
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit}>
      <h1>Nuovo club</h1>
      <label>
        Nome del club
        <input required minLength={2} maxLength={80} value={name} onChange={(e) => setName(e.target.value)} placeholder="Martedì Running" />
      </label>
      <label>
        Zona (facoltativa)
        <input maxLength={80} value={city} onChange={(e) => setCity(e.target.value)} placeholder="Prato" />
      </label>
      <label>
        Descrizione (facoltativa)
        <textarea maxLength={500} rows={4} value={description} onChange={(e) => setDescription(e.target.value)} />
      </label>
      <ErrorText>{error}</ErrorText>
      <button className="button" disabled={busy}>
        {busy ? "Creazione…" : "Crea il club"}
      </button>
    </form>
  );
}

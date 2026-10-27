"use client";

import { Suspense, useEffect, useState, type FormEvent } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, errorMessage, safeNext } from "@/lib/api";
import { useSession } from "@/components/Session";
import { ErrorText } from "@/components/ui";

function LoginForm() {
  const { status } = useSession();
  const router = useRouter();
  const next = safeNext(useSearchParams().get("next"));
  const [email, setEmail] = useState("");
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (status === "authenticated") router.replace(next);
  }, [status, next, router]);

  if (status === "authenticated") return null;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/auth/request-link", { method: "POST", body: { email } });
      // Ricordiamo la destinazione, così dopo l'accesso si torna dove si era.
      // Il link si apre spesso in un'altra scheda: localStorage, non sessionStorage.
      try {
        if (next !== "/club") localStorage.setItem("runtivo.next", next);
      } catch {}
      setSent(true);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <div className="stack">
        <h1>Controlla la posta</h1>
        <p>
          Se <strong>{email}</strong> è corretto, ti abbiamo inviato un link per accedere. Vale 15 minuti e si può usare una
          sola volta.
        </p>
        <button className="button secondary" onClick={() => setSent(false)}>
          Usa un&apos;altra email
        </button>
      </div>
    );
  }

  return (
    <form className="stack" onSubmit={submit}>
      <h1>Accedi</h1>
      <p className="muted">Niente password: ti mandiamo un link per email. Se non hai ancora un account, lo creiamo ora.</p>
      <label>
        Email
        <input
          type="email"
          name="email"
          autoComplete="email"
          inputMode="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
      </label>
      <ErrorText>{error}</ErrorText>
      <button className="button" disabled={busy}>
        {busy ? "Invio in corso…" : "Inviami il link"}
      </button>
    </form>
  );
}

export default function LoginPage() {
  return (
    <Suspense>
      <LoginForm />
    </Suspense>
  );
}

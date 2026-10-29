"use client";

import { Suspense, useEffect, useState, type FormEvent } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { api, errorMessage, safeNext } from "@/lib/api";
import { useSession } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { ErrorText } from "@/components/ui";

const googleErrors: Record<string, string> = {
  annullato: "Accesso con Google annullato.",
  sessione: "L'accesso con Google è scaduto: riprova.",
  google: "Google non ha confermato l'accesso: riprova o usa il link via email.",
  email: "Google non ha verificato la tua email: usa il link via email.",
  sospeso: "Questo account è sospeso.",
};

function LoginForm() {
  const { status } = useSession();
  const router = useRouter();
  const params = useSearchParams();
  const next = safeNext(params.get("next"));
  const googleError = googleErrors[params.get("errore") ?? ""] ?? null;
  const providers = useLoad(() => api<{ google: boolean }>("/auth/providers"), "providers");
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
      <p className="muted">Niente password: ti mandiamo un link per email, oppure entri con Google. Se non hai ancora un account, lo creiamo ora.</p>
      <ErrorText>{googleError}</ErrorText>
      {providers.data?.google && (
        <>
          {/* Navigazione vera (non una chiamata): il server porta a Google e riporta qui con la sessione già aperta. */}
          <a className="button secondary" href={`/api/auth/google/start?next=${encodeURIComponent(next)}`}>
            Continua con Google
          </a>
          <p className="muted small">Da Google leggiamo solo la tua email, non il nome né la foto.</p>
          <p className="muted small">oppure</p>
        </>
      )}
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

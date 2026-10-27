"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { api, errorMessage, safeNext, type User } from "@/lib/api";
import { useSession } from "@/components/Session";
import { ErrorText, Loading } from "@/components/ui";

function Verify() {
  const token = useSearchParams().get("token");
  const router = useRouter();
  const { setUser } = useSession();
  const started = useRef(false);
  const [error, setError] = useState<string | null>(token ? null : "Link non valido.");

  useEffect(() => {
    // Il token è monouso: la richiesta parte una volta sola anche con il doppio render di sviluppo.
    if (!token || started.current) return;
    started.current = true;
    api<User>("/auth/verify", { method: "POST", body: { token } }).then(
      (user) => {
        setUser(user);
        let saved: string | null = null;
        try {
          saved = localStorage.getItem("runtivo.next");
          localStorage.removeItem("runtivo.next");
        } catch {}
        router.replace(safeNext(saved));
      },
      (e) => setError(errorMessage(e)),
    );
  }, [token, router, setUser]);

  if (error) {
    return (
      <div className="stack">
        <h1>Accesso non riuscito</h1>
        <ErrorText>{error}</ErrorText>
        <Link className="button" href="/accedi">
          Richiedi un nuovo link
        </Link>
      </div>
    );
  }
  return (
    <div className="stack">
      <h1>Accesso in corso…</h1>
      <Loading />
    </div>
  );
}

export default function VerifyPage() {
  return (
    <Suspense>
      <Verify />
    </Suspense>
  );
}

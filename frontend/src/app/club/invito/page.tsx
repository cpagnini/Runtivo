"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { api, errorMessage } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { ErrorText, Loading } from "@/components/ui";

function AcceptInvitation() {
  const params = useSearchParams();
  const clubId = params.get("club");
  const token = params.get("token");
  const user = useRequireLogin();
  const router = useRouter();
  const started = useRef(false);
  const [error, setError] = useState<string | null>(clubId && token ? null : "Link di invito non valido.");

  useEffect(() => {
    // L'invito è monouso: una sola richiesta, anche con il doppio render di sviluppo.
    if (!user || !clubId || !token || started.current) return;
    started.current = true;
    api(`/clubs/${clubId}/memberships`, { method: "POST", body: { inviteToken: token } }).then(
      () => router.replace(`/club/${clubId}`),
      (e) => setError(errorMessage(e)),
    );
  }, [user, clubId, token, router]);

  if (error) {
    return (
      <div className="stack">
        <h1>Invito non utilizzabile</h1>
        <ErrorText>{error}</ErrorText>
        <p className="muted">Chiedi a un amministratore del club di crearne uno nuovo.</p>
        {clubId && (
          <Link className="button secondary" href={`/club/${clubId}`}>
            Vai alla pagina del club
          </Link>
        )}
      </div>
    );
  }
  return (
    <div className="stack">
      <h1>Entro nel club…</h1>
      <Loading />
    </div>
  );
}

export default function InvitationPage() {
  return (
    <Suspense fallback={<Loading />}>
      <AcceptInvitation />
    </Suspense>
  );
}

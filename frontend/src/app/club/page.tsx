"use client";

import Link from "next/link";
import { api, roleLabel, type Club } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { ErrorText, Loading } from "@/components/ui";

export default function MyClubsPage() {
  const user = useRequireLogin();
  const { data, error, loading } = useLoad(() => api<Club[]>("/clubs/mine"), "mine", !!user);

  if (!user || loading) return <Loading />;

  return (
    <div className="stack">
      <h1>I miei club</h1>
      <ErrorText>{error}</ErrorText>
      {data?.length === 0 && (
        <div className="card">
          <p>Non fai ancora parte di nessun club.</p>
          <p className="muted">Crea il tuo, oppure apri il link di invito che ti ha mandato un amministratore.</p>
        </div>
      )}
      <ul className="list">
        {data?.map((c) => (
          <li key={c.id}>
            <Link href={`/club/${c.id}`} className="card row">
              <span>
                <strong>{c.name}</strong>
                {c.city && <span className="muted"> · {c.city}</span>}
              </span>
              <span className="badge">{c.myStatus === "Pending" ? "In attesa" : c.myRole && roleLabel[c.myRole]}</span>
            </Link>
          </li>
        ))}
      </ul>
      <Link href="/club/nuovo" className="button">
        Crea un club
      </Link>
    </div>
  );
}

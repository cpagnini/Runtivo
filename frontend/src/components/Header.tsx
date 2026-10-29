"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useSession } from "./Session";

export function Header() {
  const { status, user, logout } = useSession();
  const router = useRouter();

  return (
    <header className="header">
      <Link href="/" className="brand">
        runtivo.
      </Link>
      <nav className="nav" aria-label="Principale">
        {status === "authenticated" && user ? (
          <>
            <Link href="/le-mie-corse">Le mie corse</Link>
            <Link href="/compagni">Compagni</Link>
            <Link href="/club">Club</Link>
            <Link href="/profilo">{user.displayName}</Link>
            <button
              className="link-button"
              onClick={async () => {
                await logout();
                router.push("/");
              }}
            >
              Esci
            </button>
          </>
        ) : status === "anonymous" ? (
          <Link href="/accedi">Accedi</Link>
        ) : null}
      </nav>
    </header>
  );
}

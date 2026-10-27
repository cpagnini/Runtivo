"use client";

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from "react";
import { usePathname, useRouter } from "next/navigation";
import { api, ApiError, type User } from "@/lib/api";

type SessionState = {
  status: "loading" | "anonymous" | "authenticated";
  user: User | null;
  setUser: (user: User | null) => void;
  logout: () => Promise<void>;
};

const SessionContext = createContext<SessionState | null>(null);

export function SessionProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ status: SessionState["status"]; user: User | null }>({
    status: "loading",
    user: null,
  });

  useEffect(() => {
    let cancelled = false;
    api<User>("/me").then(
      (user) => !cancelled && setState({ status: "authenticated", user }),
      (e) => {
        if (cancelled) return;
        // Con il backend irraggiungibile l'utente resta "anonimo": le pagine mostrano l'errore delle loro chiamate.
        if (!(e instanceof ApiError) || e.status !== 401) console.warn("Sessione non verificabile", e);
        setState({ status: "anonymous", user: null });
      },
    );
    return () => {
      cancelled = true;
    };
  }, []);

  const setUser = useCallback(
    (user: User | null) => setState({ status: user ? "authenticated" : "anonymous", user }),
    [],
  );

  const logout = useCallback(async () => {
    await api("/auth/logout", { method: "POST" });
    setState({ status: "anonymous", user: null });
  }, []);

  return <SessionContext value={{ ...state, setUser, logout }}>{children}</SessionContext>;
}

export function useSession() {
  const ctx = useContext(SessionContext);
  if (!ctx) throw new Error("useSession fuori da SessionProvider");
  return ctx;
}

/** Restituisce l'utente; se la sessione non c'è porta a /accedi e torna qui dopo il login. */
export function useRequireLogin(): User | null {
  const { status, user } = useSession();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (status === "anonymous") {
      const here = pathname + window.location.search;
      router.replace(`/accedi?next=${encodeURIComponent(here)}`);
    }
  }, [status, pathname, router]);

  return user;
}

export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}

type Options = { method?: string; body?: unknown };

// Il browser parla solo con Next.js: /api viene inoltrato al backend (vedi next.config.ts).
// L'header X-Requested-With è richiesto dal backend come difesa CSRF sulle richieste non-GET.
export async function api<T = void>(path: string, { method = "GET", body }: Options = {}): Promise<T> {
  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      method,
      headers: {
        "X-Requested-With": "runtivo",
        ...(body !== undefined ? { "Content-Type": "application/json" } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(0, "Connessione assente: riprova tra un momento.");
  }
  if (!res.ok) {
    let detail = "Qualcosa è andato storto, riprova.";
    try {
      const problem = await res.json();
      if (typeof problem.detail === "string") detail = problem.detail;
    } catch {}
    throw new ApiError(res.status, detail);
  }
  if (res.status === 204 || res.status === 202) return undefined as T;
  return (await res.json()) as T;
}

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : "Errore imprevisto.");

export type User = {
  id: string;
  email: string;
  displayName: string;
  city: string | null;
  preferredPaceSecPerKm: number | null;
};

export type Role = "Member" | "Leader" | "Admin";
export type MembershipStatus = "Pending" | "Active";

export type Club = {
  id: string;
  name: string;
  slug: string;
  description: string | null;
  city: string | null;
  myRole: Role | null;
  myStatus: MembershipStatus | null;
};

export type Member = {
  userId: string;
  displayName: string;
  email: string;
  role: Role;
  status: MembershipStatus;
  joinedAt: string;
};

export const roleLabel: Record<Role, string> = {
  Member: "Membro",
  Leader: "Capogruppo",
  Admin: "Amministratore",
};

export function formatPace(sec: number | null): string {
  if (sec == null) return "";
  return `${Math.floor(sec / 60)}:${String(sec % 60).padStart(2, "0")}`;
}

/** "5:30" → 330; stringa vuota → null; formato non valido → undefined. */
export function parsePace(text: string): number | null | undefined {
  const t = text.trim();
  if (!t) return null;
  const m = /^(\d{1,2}):([0-5]\d)$/.exec(t);
  return m ? Number(m[1]) * 60 + Number(m[2]) : undefined;
}

/** Accetta solo percorsi interni, per evitare redirect verso altri siti. */
export const safeNext = (value: string | null) => (value && /^\/(?!\/)/.test(value) ? value : "/club");

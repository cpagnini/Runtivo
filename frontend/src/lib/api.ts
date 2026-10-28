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

export type Visibility = "Public" | "ClubOnly";

export type Group = {
  id: string;
  name: string;
  distanceMeters: number | null;
  paceMinSecPerKm: number | null;
  paceMaxSecPerKm: number | null;
  leaderUserId: string | null;
  leaderName: string | null;
  capacity: number | null;
  goingCount: number;
  participants: string[] | null;
};

export type RunEvent = {
  id: string;
  clubId: string;
  clubName: string;
  title: string;
  description: string | null;
  startsAtUtc: string;
  timeZoneId: string;
  meetingPointLabel: string;
  visibility: Visibility;
  status: "Scheduled" | "Cancelled";
  myRole: Role | null;
  groups: Group[];
  myRsvp: { status: "Going" | "NotGoing"; paceGroupId: string | null } | null;
  seriesId: string | null;
};

export type EventSummary = {
  id: string;
  title: string;
  startsAtUtc: string;
  meetingPointLabel: string;
  visibility: Visibility;
  status: "Scheduled" | "Cancelled";
  groupCount: number;
  goingCount: number;
};

export type MyEvent = {
  eventId: string;
  title: string;
  startsAtUtc: string;
  meetingPointLabel: string;
  eventStatus: "Scheduled" | "Cancelled";
  clubId: string;
  clubName: string;
  groupName: string;
};

export function formatWhen(iso: string, timeZone = "Europe/Rome"): string {
  const text = new Date(iso).toLocaleString("it-IT", {
    weekday: "long",
    day: "numeric",
    month: "long",
    hour: "2-digit",
    minute: "2-digit",
    timeZone,
  });
  return text.charAt(0).toUpperCase() + text.slice(1);
}

export function formatDistance(meters: number | null): string {
  if (meters == null) return "";
  return `${(meters / 1000).toLocaleString("it-IT", { maximumFractionDigits: 1 })} km`;
}

export function formatPaceRange(min: number | null, max: number | null): string {
  if (min != null && max != null) return min === max ? `${formatPace(min)} min/km` : `${formatPace(min)}–${formatPace(max)} min/km`;
  const one = min ?? max;
  return one != null ? `circa ${formatPace(one)} min/km` : "";
}

export type SeriesSummary = {
  id: string;
  title: string;
  meetingPointLabel: string;
  weekday: string;
  localStartTime: string;
  startDate: string;
  endDate: string | null;
  enabled: boolean;
  visibility: Visibility;
  groupCount: number;
  upcomingEvents: number;
};

export type DashboardEvent = {
  id: string;
  title: string;
  startsAtUtc: string;
  status: "Scheduled" | "Cancelled";
  seriesId: string | null;
  going: number;
  notGoing: number;
  groups: { name: string; going: number; capacity: number | null }[];
};

export type Dashboard = { upcoming: DashboardEvent[]; series: SeriesSummary[] };

export const weekdayLabel: Record<string, string> = {
  Monday: "Lunedì",
  Tuesday: "Martedì",
  Wednesday: "Mercoledì",
  Thursday: "Giovedì",
  Friday: "Venerdì",
  Saturday: "Sabato",
  Sunday: "Domenica",
};

/** ISO UTC → valore per <input type="datetime-local"> nel fuso del dispositivo. */
export function toLocalInput(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

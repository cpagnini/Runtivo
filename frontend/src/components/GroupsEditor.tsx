"use client";

import { formatPace, parsePace, type Group } from "@/lib/api";

export type GroupForm = {
  id?: string;
  leaderUserId?: string | null;
  name: string;
  km: string;
  paceMin: string;
  paceMax: string;
  capacity: string;
};

export const MAX_GROUPS = 8;

export const emptyGroup = (name = ""): GroupForm => ({ name, km: "", paceMin: "", paceMax: "", capacity: "" });

export const groupToForm = (g: Group): GroupForm => ({
  id: g.id,
  leaderUserId: g.leaderUserId,
  name: g.name,
  km: g.distanceMeters != null ? String(g.distanceMeters / 1000).replace(".", ",") : "",
  paceMin: formatPace(g.paceMinSecPerKm),
  paceMax: formatPace(g.paceMaxSecPerKm),
  capacity: g.capacity != null ? String(g.capacity) : "",
});

/** Converte i campi del modulo nel formato dell'API; restituisce un messaggio se qualcosa non è valido. */
export function groupsToPayload(groups: GroupForm[]): { error: string } | { payload: object[] } {
  const payload = [];
  for (const [i, g] of groups.entries()) {
    const min = parsePace(g.paceMin);
    const max = parsePace(g.paceMax);
    if (min === undefined || max === undefined) return { error: `Gruppo ${i + 1}: scrivi il passo come minuti:secondi, ad esempio 5:30.` };
    const km = g.km.trim() ? Number(g.km.replace(",", ".")) : null;
    if (km !== null && !(km > 0)) return { error: `Gruppo ${i + 1}: la distanza non è valida.` };
    const capacity = g.capacity.trim() ? Number(g.capacity) : null;
    if (capacity !== null && !Number.isInteger(capacity)) return { error: `Gruppo ${i + 1}: i posti devono essere un numero intero.` };
    payload.push({
      name: g.name,
      distanceMeters: km === null ? null : Math.round(km * 1000),
      paceMinSecPerKm: min ?? max,
      paceMaxSecPerKm: max ?? min,
      leaderUserId: g.leaderUserId ?? null,
      capacity,
    });
  }
  return { payload };
}

export function GroupsEditor({
  groups,
  onChange,
  canAddRemove = true,
}: {
  groups: GroupForm[];
  onChange: (groups: GroupForm[]) => void;
  canAddRemove?: boolean;
}) {
  const update = (i: number, patch: Partial<GroupForm>) => onChange(groups.map((g, j) => (j === i ? { ...g, ...patch } : g)));

  return (
    <>
      {groups.map((g, i) => (
        <fieldset key={g.id ?? i} className="card group-form">
          <legend className="small muted">Gruppo {i + 1}</legend>
          <label>
            Nome
            <input required maxLength={40} value={g.name} onChange={(e) => update(i, { name: e.target.value })} placeholder="Relax" />
          </label>
          <div className="two-cols">
            <label>
              Distanza (km)
              <input inputMode="decimal" value={g.km} onChange={(e) => update(i, { km: e.target.value })} placeholder="6" />
            </label>
            <label>
              Posti (facoltativo)
              <input inputMode="numeric" value={g.capacity} onChange={(e) => update(i, { capacity: e.target.value })} placeholder="illimitati" />
            </label>
            <label>
              Passo da (min/km)
              <input inputMode="numeric" value={g.paceMin} onChange={(e) => update(i, { paceMin: e.target.value })} placeholder="6:15" />
            </label>
            <label>
              Passo a (min/km)
              <input inputMode="numeric" value={g.paceMax} onChange={(e) => update(i, { paceMax: e.target.value })} placeholder="6:45" />
            </label>
          </div>
          {canAddRemove && groups.length > 1 && (
            <button type="button" className="button danger small" onClick={() => onChange(groups.filter((_, j) => j !== i))}>
              Rimuovi gruppo
            </button>
          )}
        </fieldset>
      ))}
      {canAddRemove && groups.length < MAX_GROUPS && (
        <button type="button" className="button secondary" onClick={() => onChange([...groups, emptyGroup(`Gruppo ${groups.length + 1}`)])}>
          Aggiungi un gruppo
        </button>
      )}
    </>
  );
}

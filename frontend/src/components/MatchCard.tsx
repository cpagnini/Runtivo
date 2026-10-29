"use client";

import { useState } from "react";
import { api, errorMessage, formatDistance, formatPaceRange, runTypeLabel, scoreLabel, slotLabel, type Match } from "@/lib/api";
import { ErrorText } from "./ui";

export function MatchCard({ match, onBlocked }: { match: Match; onBlocked: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function block() {
    if (!confirm(`Bloccare ${match.displayName}? Non vi vedrete più a vicenda e non verrà avvisato.`)) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/users/${match.userId}/block`, { method: "POST" });
      onBlocked();
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  const range = (a: number, b: number) => (a === b ? formatDistance(a) : `${formatDistance(a).replace(" km", "")}–${formatDistance(b)}`);

  return (
    <li className="card stack">
      <div className="row">
        <div>
          <strong>{match.displayName}</strong>
          <span className="muted small">
            {" "}
            · {match.city}, a circa {match.approxDistanceKm} km
          </span>
        </div>
        <span className="badge" title={`Compatibilità ${match.score} su 100`}>
          {scoreLabel(match.score)} · {match.score}
        </span>
      </div>
      <p className="small">
        {formatPaceRange(match.paceMinSecPerKm, match.paceMaxSecPerKm)} · {range(match.distanceMinMeters, match.distanceMaxMeters)} ·{" "}
        {runTypeLabel[match.runType].toLowerCase()}
        {match.canMeetAsPair && " · anche in coppia"}
      </p>
      <ul className="reasons">
        {match.reasons.map((r) => (
          <li key={r}>{r}</li>
        ))}
      </ul>
      <p className="muted small">In comune: {match.sharedSlots.map(slotLabel).join(", ")}</p>
      <ErrorText>{error}</ErrorText>
      <div className="row wrap">
        <span className="muted small">Presto potrai proporre un&apos;uscita in un luogo pubblico.</span>
        <button className="button danger small" onClick={block} disabled={busy}>
          Blocca
        </button>
      </div>
    </li>
  );
}

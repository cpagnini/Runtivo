"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api, errorMessage, formatDistance, formatPaceRange, runTypeLabel, scoreLabel, slotLabel, type Match } from "@/lib/api";
import { ErrorText } from "./ui";
import { ReportForm } from "./ReportForm";

type InterestResult = { mutual: boolean; runId: string | null };

export function MatchCard({ match, onChanged }: { match: Match; onChanged: () => void }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [reporting, setReporting] = useState(false);

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  const showInterest = () =>
    run(async () => {
      const result = await api<InterestResult>("/interests", { method: "POST", body: { toUserId: match.userId } });
      if (result.mutual && result.runId) {
        router.push(`/uscite/${result.runId}`);
        return;
      }
      setNotice(
        result.mutual
          ? "Interesse reciproco! Non abbiamo trovato un orario libero in comune nei prossimi giorni: prova a proporre tu un'uscita."
          : "Interesse inviato. Se anche questa persona mostra interesse per te, qui comparirà la proposta di uscita. Nessuno vede chi ha scartato.",
      );
      onChanged();
    });

  const withdraw = () =>
    run(async () => {
      await api(`/interests/${match.userId}`, { method: "DELETE" });
      onChanged();
    });

  const block = () =>
    run(async () => {
      if (!confirm(`Bloccare ${match.displayName}? Non vi vedrete più a vicenda e non riceverà alcun avviso.`)) return;
      await api(`/users/${match.userId}/block`, { method: "POST" });
      onChanged();
    });

  const range = (a: number, b: number) => (a === b ? formatDistance(a) : `${formatDistance(a).replace(" km", "")}–${formatDistance(b)}`);

  return (
    <li className="card stack">
      <div className="row">
        <div className="person">
          <span className="avatar" aria-hidden="true">
            {match.displayName.trim().charAt(0).toUpperCase()}
          </span>
          <div>
            <strong>{match.displayName}</strong>
            <br />
            <span className="muted small">
              {match.city}, a circa {match.approxDistanceKm} km
            </span>
          </div>
        </div>
        <span className="badge">{scoreLabel(match.score)}</span>
      </div>
      <div className="meter" role="img" aria-label={`Compatibilità ${match.score} su 100`}>
        <span className="meter-bar">
          <span style={{ width: `${match.score}%` }} />
        </span>
        <span>{match.score}%</span>
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
      {match.note && <p className="quote">{match.note}</p>}
      <p className="muted small">In comune: {match.sharedSlots.map(slotLabel).join(", ")}</p>

      <ErrorText>{error}</ErrorText>
      {notice && <p role="status">{notice}</p>}

      <div className="row wrap">
        {match.runId ? (
          <Link className="button small" href={`/uscite/${match.runId}`}>
            Vedi l&apos;uscita
          </Link>
        ) : match.interestSent ? (
          <>
            <span className="badge">Interesse inviato</span>
            <button className="link-button small" onClick={withdraw} disabled={busy}>
              Ritira
            </button>
          </>
        ) : (
          <button className="button small" onClick={showInterest} disabled={busy || !match.canMeetAsPair} title={match.canMeetAsPair ? undefined : "Una di voi due preferisce uscite di gruppo"}>
            Mi interessa
          </button>
        )}
        <Link className="button secondary small" href={`/compagni/proponi?con=${match.userId}`}>
          Proponi un&apos;uscita
        </Link>
        <button className="button secondary small" onClick={() => setReporting((v) => !v)} disabled={busy}>
          Segnala
        </button>
        <button className="button danger small" onClick={block} disabled={busy}>
          Blocca
        </button>
      </div>
      {reporting && <ReportForm userId={match.userId} name={match.displayName} onClose={() => setReporting(false)} onBlocked={onChanged} />}
      {!match.canMeetAsPair && !match.runId && (
        <p className="muted small">Una di voi due preferisce le uscite di gruppo: per correre insieme proponi un&apos;uscita con almeno un&apos;altra persona.</p>
      )}
    </li>
  );
}

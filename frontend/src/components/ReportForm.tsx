"use client";

import { useState, type FormEvent } from "react";
import { api, errorMessage, reportReasonLabel, type ReportReason } from "@/lib/api";
import { ErrorText } from "./ui";

export function ReportForm({
  userId,
  name,
  runId,
  onClose,
  onBlocked,
}: {
  userId: string;
  name: string;
  runId?: string;
  onClose: () => void;
  onBlocked?: () => void;
}) {
  const [reason, setReason] = useState<ReportReason>("Harassment");
  const [details, setDetails] = useState("");
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api("/reports", { method: "POST", body: { reportedUserId: userId, runId: runId ?? null, reason, details } });
      setSent(true);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  async function block() {
    setBusy(true);
    setError(null);
    try {
      await api(`/users/${userId}/block`, { method: "POST" });
      onBlocked?.();
      onClose();
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <div className="stack report-form">
        <p role="status">Grazie, abbiamo ricevuto la segnalazione. {name} non saprà che l&apos;hai fatta tu.</p>
        <p className="muted small">Se non vuoi più incontrarvi puoi anche bloccare la persona.</p>
        <ErrorText>{error}</ErrorText>
        <div className="row wrap">
          <button className="button danger small" onClick={block} disabled={busy}>
            Blocca {name}
          </button>
          <button className="button secondary small" onClick={onClose}>
            Chiudi
          </button>
        </div>
      </div>
    );
  }

  return (
    <form className="stack report-form" onSubmit={submit}>
      <strong>Segnala {name}</strong>
      <label>
        Motivo
        <select value={reason} onChange={(e) => setReason(e.target.value as ReportReason)}>
          {(Object.keys(reportReasonLabel) as ReportReason[]).map((r) => (
            <option key={r} value={r}>
              {reportReasonLabel[r]}
            </option>
          ))}
        </select>
      </label>
      <label>
        Cosa è successo (facoltativo)
        <textarea rows={3} maxLength={500} value={details} onChange={(e) => setDetails(e.target.value)} />
        <span className="hint">La persona segnalata non vedrà mai che la segnalazione viene da te. Le segnalazioni le legge solo chi modera la piattaforma.</span>
      </label>
      <ErrorText>{error}</ErrorText>
      <div className="row wrap">
        <button className="button small" disabled={busy}>
          {busy ? "Invio…" : "Invia la segnalazione"}
        </button>
        <button type="button" className="button secondary small" onClick={onClose}>
          Annulla
        </button>
      </div>
    </form>
  );
}

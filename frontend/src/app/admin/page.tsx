"use client";

import { useState } from "react";
import { api, errorMessage, formatWhen, reportReasonLabel, type AdminReport } from "@/lib/api";
import { useRequireLogin } from "@/components/Session";
import { useLoad } from "@/lib/useLoad";
import { Card, ErrorText, Loading } from "@/components/ui";

function ReportRow({ report, onDone }: { report: AdminReport; onDone: () => void }) {
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function resolve(action: "Dismiss" | "Suspend") {
    const text =
      action === "Suspend"
        ? `Sospendere ${report.reportedName}? Non potrà più accedere, uscirà dalle sue uscite future e le altre segnalazioni su di lui verranno chiuse.`
        : "Archiviare la segnalazione senza provvedimenti?";
    if (!confirm(text)) return;
    setBusy(true);
    setError(null);
    try {
      await api(`/admin/reports/${report.id}/resolve`, { method: "POST", body: { action, note } });
      onDone();
    } catch (err) {
      setError(errorMessage(err));
      setBusy(false);
    }
  }

  return (
    <li className="card stack">
      <div className="row">
        <strong>{report.reportedName}</strong>
        <span className="badge">{reportReasonLabel[report.reason]}</span>
      </div>
      {report.details && <p>{report.details}</p>}
      <p className="muted small">
        Segnalata il {formatWhen(report.createdAt)} da {report.reporterName ?? "account eliminato"}
        {report.openReportsAgainstTarget > 1 && ` · ${report.openReportsAgainstTarget} segnalazioni aperte su questa persona`}
        {report.reportedStatus === "Suspended" && " · già sospesa"}
      </p>
      <label>
        Nota (facoltativa, resta nel registro)
        <input value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
      </label>
      <ErrorText>{error}</ErrorText>
      <div className="row wrap">
        <button className="button secondary small" disabled={busy} onClick={() => resolve("Dismiss")}>
          Archivia
        </button>
        <button className="button danger small" disabled={busy || report.reportedStatus === null} onClick={() => resolve("Suspend")}>
          Sospendi l&apos;account
        </button>
      </div>
    </li>
  );
}

export default function AdminPage() {
  const user = useRequireLogin();
  const { data, error, loading, reload } = useLoad(() => api<AdminReport[]>("/admin/reports"), "admin-reports", !!user?.isPlatformAdmin);

  if (!user) return <Loading />;
  if (!user.isPlatformAdmin) return <ErrorText>Questa pagina è riservata a chi modera la piattaforma.</ErrorText>;
  if (loading) return <Loading />;

  return (
    <div className="stack">
      <h1>Moderazione</h1>
      <ErrorText>{error}</ErrorText>
      {data?.length === 0 && (
        <Card>
          <p>Nessuna segnalazione da esaminare.</p>
        </Card>
      )}
      <ul className="list">
        {data?.map((r) => (
          <ReportRow key={r.id} report={r} onDone={reload} />
        ))}
      </ul>
    </div>
  );
}

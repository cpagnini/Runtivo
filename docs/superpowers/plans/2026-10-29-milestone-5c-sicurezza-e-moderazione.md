# Milestone 5c — Sicurezza e moderazione — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chi incontra sconosciuti ha strumenti di tutela: segnalare una persona, condividere l'uscita con una persona di fiducia, essere protetto da account nuovi che esagerano; chi gestisce la piattaforma può esaminare le segnalazioni e sospendere account, con traccia.

**Architecture:** Estensione del modulo `Matching`/`Safety` del backend. Il ruolo di amministratore piattaforma è definito da configurazione (`Platform:AdminEmails`) e verificato a ogni richiesta sulle sole API `/api/admin/*`: nessun endpoint permette di diventare admin. La condivisione usa un token casuale di cui si salva solo l'hash, con scadenza, e una lettura pubblica minimale. I limiti per account nuovi sono configurabili (`App:NewAccountDays`).

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit su PostgreSQL reale (`runtivo_test`), Next.js 16.

**Spec:** `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md` (milestone 5c) · Decisioni 0006 e 0007.

## Global Constraints

- Mai email né coordinate nelle risposte; la persona segnalata non vede mai chi l'ha segnalata.
- Admin piattaforma = email in `Platform:AdminEmails` (vuoto di default, in produzione). Le API `/api/admin/*` rispondono 403 a tutti gli altri. Ogni azione amministrativa produce una voce di audit con l'attore.
- Token di condivisione: 256 bit casuali, solo l'hash nel DB, scadenza = inizio dell'uscita + 6 ore; massimo 5 condivisioni attive per persona e uscita; revocabili.
- Limiti: al massimo 10 segnalazioni al giorno per persona; account più giovani di `App:NewAccountDays` (default 7): al massimo 5 «Mi interessa» nelle ultime 24 ore e 1 proposta in attesa.
- Una persona sospesa non può accedere né comparire (già garantito da stato account) e le sue uscite future vengono lasciate con le stesse regole di blocco/cancellazione.
- UI in italiano; codice e API in inglese; le mutazioni richiedono `X-Requested-With`.

## Review Focus

- Un non-admin che prova `/api/admin/*` (anche con un id valido) riceve 403 e non apprende nulla (Task 3).
- Segnalare più volte la stessa persona o se stessa; segnalare qualcuno con cui non si ha mai interagito (Task 1).
- Un link di condivisione scaduto, revocato o di un'uscita annullata non rivela dati sensibili (Task 4).
- Persona sospesa con sessione ancora aperta: la sessione cade e non compare più (Task 3).
- Account nuovo che tenta di aggirare il limite creando più richieste in parallelo (Task 5).
- Cancellazione dell'account di chi ha segnalato o è stato segnalato (Task 1, 3).

---

### Task 1: Segnalazioni

**Files:**
- Create: `backend/src/Runtivo.Api/Safety/SafetyEntities.cs`, `backend/src/Runtivo.Api/Safety/ReportEndpoints.cs`
- Modify: `backend/src/Runtivo.Api/Data/RuntivoDbContext.cs`; migrazione `SafetyAndReports`; `backend/src/Runtivo.Api/Program.cs`; `backend/src/Runtivo.Api/Auth/AccountEndpoints.cs` (cancellazione/export)
- Test: `backend/tests/Runtivo.Api.Tests/ReportTests.cs`

**Interfaces:**
- Produces: `enum ReportReason { Harassment, FakeProfile, Unsafe, Spam, Other }`, `enum ReportStatus { Open, Dismissed, ActionTaken }`
- Produces: `class Report { Guid Id; Guid? ReporterUserId; Guid ReportedUserId; Guid? RunId; ReportReason Reason; string? Details; ReportStatus Status; DateTimeOffset CreatedAt; DateTimeOffset? ReviewedAt; Guid? ReviewedByUserId; string? ResolutionNote }` — niente FK sugli utenti: restano come identificatori anche dopo la cancellazione di un account (alla cancellazione del segnalante `ReporterUserId` diventa null)
- Produces: `POST /api/reports` `{ reportedUserId, runId?, reason, details? }` → 201 `{ id }`; 400 se è sé stessi, motivo non valido, dettagli > 500 caratteri; 404 se la persona non esiste; con `runId` il segnalante e la persona devono essere entrambi partecipanti (404 altrimenti); un duplicato aperto dello stesso segnalante sulla stessa persona nelle ultime 24 ore restituisce 200 con lo stesso `id`; oltre 10 segnalazioni nelle ultime 24 ore → 429.

- [ ] **Step 1: Write the failing tests** (`ReportTests`): `Report_requires_login`; `Report_is_created_and_target_never_learns_who_reported` (la risposta e ogni endpoint della persona segnalata non contengono il segnalante); `Cannot_report_yourself_or_unknown_people_or_bad_input`; `Duplicate_open_report_returns_the_same_id`; `Daily_limit_of_ten_reports` (undici → 429); `Run_reports_require_both_to_be_participants`; `Reporter_deletion_anonymises_the_report_but_keeps_it` (dopo la cancellazione `ReporterUserId` è null, `ReportedUserId` resta); `Export_does_not_include_reports_filed_against_me`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~ReportTests` — Expected: FAIL.
- [ ] **Step 3: Implement** entità, mapping (indici su `(Status, CreatedAt)` e `ReportedUserId`), migrazione, endpoint; alla cancellazione account: `UPDATE` di `ReporterUserId` a null e **nessuna** cancellazione delle segnalazioni ricevute (servono alla moderazione); audit `report.create` senza dettagli.
- [ ] **Step 4: Run** `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(safety): segnalazioni`.

### Task 2: Ruolo admin piattaforma e API di moderazione

**Files:**
- Create: `backend/src/Runtivo.Api/Safety/PlatformAdmin.cs`, `backend/src/Runtivo.Api/Safety/AdminEndpoints.cs`
- Modify: `backend/src/Runtivo.Api/Common/EmailSender.cs` (`AppOptions`: `NewAccountDays`; nuova `PlatformOptions { List<string> AdminEmails }`), `backend/src/Runtivo.Api/Program.cs`, `backend/src/Runtivo.Api/Auth/AuthEndpoints.cs` (`UserDto.IsPlatformAdmin`), `backend/src/Runtivo.Api/appsettings.Development.json` (`Platform:AdminEmails: ["admin@example.com"]`)
- Test: `backend/tests/Runtivo.Api.Tests/AdminTests.cs`

**Interfaces:**
- Produces: `static class PlatformAdmin { Task<bool> IsAsync(RuntivoDbContext db, IOptions<PlatformOptions> options, Guid userId, CancellationToken ct); }` — confronto senza distinzione di maiuscole tra l'email dell'utente e l'elenco
- Produces: `record AdminReportDto(Guid Id, ReportReason Reason, string? Details, ReportStatus Status, DateTimeOffset CreatedAt, Guid ReportedUserId, string ReportedName, UserStatus ReportedStatus, string? ReporterName, int OpenReportsAgainstTarget, Guid? RunId)`
- Produces: `GET /api/admin/reports?status=Open|Dismissed|ActionTaken` (default `Open`, max 100, più recenti prima); `POST /api/admin/reports/{id}/resolve` `{ action: "Dismiss" | "Suspend", note? }`; `POST /api/admin/users/{id}/suspend` `{ reason }` e `POST /api/admin/users/{id}/unsuspend`; tutti 403 per i non admin. `Suspend` imposta `UserStatus.Suspended`, mette in pausa l'annuncio, fa lasciare le uscite future (`RunMembership.LeaveFutureRunsAsync(db, userId, null, now, ct)`) e chiude tutte le segnalazioni aperte contro quella persona come `ActionTaken`. `GET /api/me` espone `isPlatformAdmin`.

- [ ] **Step 1: Write the failing tests** (`AdminTests`, con una factory dedicata che imposta `Platform:AdminEmails` su un indirizzo di prova): `Non_admins_get_403_on_every_admin_endpoint` (anche con id validi di segnalazioni e utenti); `Admin_sees_open_reports_with_names_but_no_emails`; `Dismiss_closes_the_report_and_records_the_reviewer`; `Suspending_blocks_login_hides_the_person_and_ends_the_session` (la sessione già aperta risponde 401, il nuovo accesso 403, sparisce dai match, esce dalle uscite future, l'annuncio è in pausa); `Suspend_resolves_all_open_reports_against_the_person`; `Unsuspend_restores_login`; `Admin_actions_leave_audit_entries_with_the_actor`; `Me_exposes_isPlatformAdmin_only_for_admins`; `Admin_emails_match_case_insensitively`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~AdminTests` — Expected: FAIL.
- [ ] **Step 3: Implement** helper, endpoint, opzioni e `UserDto.IsPlatformAdmin`; ogni azione scrive `AuditEntry` (`admin.report.resolve`, `admin.user.suspend`, `admin.user.unsuspend`) con `ActorUserId`.
- [ ] **Step 4: Run** `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(safety): ruolo admin piattaforma e moderazione`.

### Task 3: Condivisione dell'uscita con una persona di fiducia

**Files:**
- Create: `backend/src/Runtivo.Api/Safety/ShareEndpoints.cs`
- Modify: `backend/src/Runtivo.Api/Safety/SafetyEntities.cs` (`SafetyShare`), `RuntivoDbContext`; migrazione `SafetyShares`
- Test: `backend/tests/Runtivo.Api.Tests/ShareTests.cs`

**Interfaces:**
- Produces: `class SafetyShare { Guid Id; Guid RunId; Guid UserId; string TokenHash; DateTimeOffset CreatedAt; DateTimeOffset ExpiresAt; DateTimeOffset? RevokedAt }` (indice univoco su `TokenHash`; FK cascade su uscita e utente)
- Produces: `POST /api/runs/{id}/share` → 201 `{ link, expiresAt }` (link = `{PublicBaseUrl}/sicurezza/{token}`, mostrato una volta); `DELETE /api/runs/{id}/share` (revoca le proprie condivisioni, 204); `GET /api/safety/{token}` pubblico → `{ sharedBy, startsAtUtc, timeZoneId, placeName, city, status, participants: [name], expiresAt }`, 404 se token ignoto, scaduto o revocato
- Regole: solo i partecipanti attivi (in attesa o confermati) possono condividere; 409 se l'uscita è annullata; massimo 5 condivisioni attive per persona e uscita; la lettura pubblica non contiene email, id utente né coordinate.

- [ ] **Step 1: Write the failing tests** (`ShareTests`): `Participant_creates_a_link_and_the_trusted_person_reads_it_without_login`; `Public_view_has_no_emails_ids_or_coordinates`; `Only_active_participants_can_share_and_cancelled_runs_cannot` (409); `Link_stops_working_after_expiry_or_revocation` (avanzando `api.Time`; 404 senza rivelare altro); `Limit_of_five_active_shares`; `Unknown_token_is_404_and_tokens_are_stored_hashed` (il token in chiaro non compare nel DB); `Public_view_reflects_a_run_cancelled_after_sharing` (stato «Cancelled», nessun altro dettaglio personale).
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~ShareTests` — Expected: FAIL.
- [ ] **Step 3: Implement** con `Tokens.Generate()`/`Tokens.Hash()` già esistenti; audit `run.share` senza token.
- [ ] **Step 4: Run** `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(safety): condivisione dell'uscita con una persona di fiducia`.

### Task 4: Limiti per account nuovi

**Files:**
- Create: `backend/src/Runtivo.Api/Safety/NewAccountLimits.cs`
- Modify: `backend/src/Runtivo.Api/Matching/InterestEndpoints.cs`, `backend/src/Runtivo.Api/Matching/RunEndpoints.cs`, `backend/tests/Runtivo.Api.Tests/ApiFixture.cs` (`App:NewAccountDays = 0` per gli altri test)
- Test: `backend/tests/Runtivo.Api.Tests/NewAccountLimitTests.cs` (factory dedicata con `App:NewAccountDays = 7`)

**Interfaces:**
- Produces: `static class NewAccountLimits { const int InterestsPerDay = 5; const int OpenProposals = 1; bool IsNew(DateTimeOffset createdAt, DateTimeOffset now, int newAccountDays); }`

- [ ] **Step 1: Write the failing tests:** `New_account_can_show_interest_five_times_per_day_not_six` (6ª → 429 con messaggio italiano); `New_account_has_a_single_open_proposal` (2ª → 409); `Limits_lift_after_the_new_account_period` (avanzando 8 giorni passano); `Parallel_requests_cannot_exceed_the_interest_limit` (10 interessi in parallelo → al massimo 5 riusciti); `Established_accounts_keep_the_normal_limits`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~NewAccountLimitTests` — Expected: FAIL.
- [ ] **Step 3: Implement** il controllo dentro la stessa transazione/lock dell'operazione (per gli interessi un `pg_advisory_xact_lock` sull'utente, così il conteggio e l'inserimento sono atomici).
- [ ] **Step 4: Run** `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(safety): limiti per account nuovi`.

### Task 5: Frontend

**Files:**
- Create: `frontend/src/components/ReportForm.tsx`, `frontend/src/app/sicurezza/[token]/page.tsx`, `frontend/src/app/admin/page.tsx`
- Modify: `frontend/src/lib/api.ts` (tipi e etichette), `frontend/src/components/MatchCard.tsx`, `frontend/src/app/uscite/[id]/page.tsx`, `frontend/src/components/Header.tsx`, `frontend/src/components/Session.tsx`/`api.ts` (`User.isPlatformAdmin`)

**Interfaces:**
- Consumes: endpoint dei Task 1–4.
- Produces: «Segnala» su scheda match e su partecipante dell'uscita (motivo, dettagli facoltativi, e l'offerta di bloccare la persona); «Condividi con una persona di fiducia» sulla pagina dell'uscita (link copiabile / condivisione di sistema, «Revoca»); pagina pubblica `/sicurezza/[token]` in sola lettura; pagina `/admin` (solo admin) con segnalazioni aperte e azioni Archivia / Sospendi, voce «Moderazione» nel menu solo per gli admin.

- [ ] **Step 1: Implement the pages and components** con stati vuoti utili e messaggi in italiano; la pagina pubblica non richiede accesso e non mostra nulla se il link non è valido.
- [ ] **Step 2: Run** `npm run lint` e `npm run build` in `frontend` — Expected: nessun errore.
- [ ] **Step 3: Verify in the browser:** segnalare da una scheda; condividere un'uscita e aprire il link da una finestra senza sessione; accedere come admin e sospendere; layout a 375 px.
- [ ] **Step 4: Commit** `feat(safety): pagine di segnalazione, condivisione e moderazione`.

### Task 6: Documentazione e chiusura

**Files:** Create `docs/decisions/0008-sicurezza-e-moderazione.md`; Modify `docs/piano-milestone.md`

- [ ] **Step 1: Write the decision log** (ruolo admin da configurazione, segnalazioni anonime per la persona segnalata, sospensione e conseguenze, condivisione con token hashato, limiti account nuovi, limiti noti: coda senza notifiche, nessuna appellabilità, moderazione manuale da gestire, privacy policy e responsabilità da definire con un legale).
- [ ] **Step 2: Run the full verification:** `dotnet test backend`, `npm run lint`, `npm run build` — Expected: tutto verde.
- [ ] **Step 3: Commit** `docs: decisione 0008 e stato milestone 5c`.

---

## Self-review

- **Copertura (5c):** segnalazioni (T1), ruolo admin piattaforma con sospensione e audit (T2), condivisione dell'uscita (T3), limiti ai nuovi account (T4), pagine (T5). Il blocco era già in 5a.
- **Coerenza dei tipi:** `ReportReason`, `ReportStatus`, `Report`, `SafetyShare`, `PlatformAdmin`, `NewAccountLimits` definiti una volta; `RunMembership.LeaveFutureRunsAsync` e `Tokens` riusati dalle fasi precedenti.
- **Proporzione:** firme, valori e test; i corpi restano a chi implementa.

# Milestone 5b — Interesse e uscite spontanee — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Da un match si può mostrare interesse in privato; se è reciproco nasce una proposta di uscita in un luogo pubblico; chiunque può anche proporre un'uscita invitando persone compatibili. L'uscita è confermata solo quando tutti i partecipanti accettano, con regole specifiche per le coppie.

**Architecture:** Estensione del modulo `Matching`. Un servizio (`MatchService`) calcola la compatibilità tra due persone e viene riusato da elenco match, interessi e inviti. Le regole di stato di un'uscita sono una funzione pura (`RunRules`) usata da conferma, rifiuto, uscita dal gruppo, blocco e cancellazione account. Le transazioni bloccano la riga dell'uscita (`SELECT … FOR UPDATE`) come già fatto per i posti degli eventi.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit su PostgreSQL reale (`runtivo_test`), Next.js 16.

**Spec:** `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md` (milestone 5b) · Decisione 0006 per la 5a.

## Global Constraints

- Luoghi solo dall'elenco `PublicPlace`; nessuna risposta contiene coordinate o email. I nomi dei partecipanti sono visibili solo ai partecipanti dell'uscita.
- L'interesse è privato: l'altra persona non sa di essere stata scartata né vede un interesse finché non è reciproco.
- Si può mostrare interesse o invitare solo chi è **compatibile adesso** (stesse regole del match, non bloccato, con annuncio attivo): per ogni altro id la risposta è 404, per non permettere di sondare utenti.
- Uscita con 2 partecipanti attivi solo se entrambi hanno «anche in coppia»; se un gruppo scende a 2 e uno dei due non lo accetta, l'uscita è annullata.
- Uscita confermata solo quando **tutti** i partecipanti attivi (invitati non rifiutati) hanno confermato e sono almeno 2. Si confermano solo uscite future (almeno 1 ora da ora) e non annullate.
- Invitati: da 1 a 7. Data entro 60 giorni. Al massimo 3 uscite proposte in corso per organizzatore.
- Orari memorizzati in UTC con fuso `Europe/Rome`; fasce → ore: mattina 08:00, pausa pranzo 12:30, sera 19:00.
- UI e messaggi in italiano; codice e API in inglese.

## Review Focus

- Due persone che mostrano interesse nello stesso istante: nasce una sola proposta (Task 3).
- Conferme e rifiuti simultanei sullo stesso gruppo: stato finale coerente, mai «confermata» con un rifiutato (Task 4).
- Una persona bloccata o che esce dall'app (blocco, cancellazione account) mentre è in un'uscita: l'uscita si aggiorna o si annulla, non resta con partecipanti fantasma (Task 5).
- Un'uscita già iniziata o annullata non accetta conferme (Task 4).
- Interesse verso una persona non più compatibile, scaduta o in pausa: 404 senza rivelare perché (Task 3).
- Chi non è partecipante non vede l'uscita né i nomi (Task 4).

---

### Task 1: Modello dati e regole di stato

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/RunEntities.cs`, `backend/src/Runtivo.Api/Matching/RunRules.cs`
- Modify: `backend/src/Runtivo.Api/Data/RuntivoDbContext.cs`; migrazione `RunsAndInterests`
- Test: `backend/tests/Runtivo.Api.Tests/RunRulesTests.cs`

**Interfaces:**
- Produces: `enum RunStatus { Proposed, Confirmed, Cancelled }`, `enum ParticipantStatus { Pending, Confirmed, Declined, Left }`
- Produces: `class Interest { Guid FromUserId; Guid ToUserId; DateTimeOffset CreatedAt }` (chiave composta, FK cascade su entrambi, CHECK `FromUserId <> ToUserId`)
- Produces: `class SpontaneousRun { Guid Id; Guid CreatedByUserId; Guid PlaceId; DateTimeOffset StartsAtUtc; string TimeZoneId; int? DistanceMeters; int? PaceMinSecPerKm; int? PaceMaxSecPerKm; RunStatus Status; string? CancelReason; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }`
- Produces: `class RunParticipant { Guid RunId; Guid UserId; ParticipantStatus Status; DateTimeOffset UpdatedAt }` (chiave composta; FK cascade su run e utente)
- Produces: `record ParticipantState(Guid UserId, ParticipantStatus Status, bool AllowsPairs)`; `record RunOutcome(RunStatus Status, string? CancelReason)`; `static class RunRules { RunOutcome Resolve(IEnumerable<ParticipantState> participants, bool alreadyCancelled = false); }`

- [ ] **Step 1: Write the failing tests** (`RunRulesTests`): `Two_confirmed_pair_who_allow_pairs_is_confirmed`; `Pending_participant_keeps_the_run_proposed`; `Declined_participants_are_ignored_when_everyone_else_confirmed` (3 persone, una rifiuta, due confermano e accettano coppie → Confermata); `Fewer_than_two_active_participants_cancels_with_a_reason`; `Pair_with_someone_who_disallows_pairs_is_cancelled` (due attivi, uno con `AllowsPairs=false` → Annullata, motivo che cita «gruppo di 3»); `Group_of_three_ignores_the_pair_preference`; `Cancelled_stays_cancelled`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~RunRulesTests` — Expected: FAIL.
- [ ] **Step 3: Implement `RunRules.Resolve`:** attivi = `Pending` + `Confirmed`; < 2 attivi → Annullata («Non ci sono abbastanza partecipanti»); esattamente 2 attivi e almeno uno con `AllowsPairs=false` → Annullata («Non è più un gruppo di almeno 3 persone»); tutti gli attivi `Confirmed` → Confermata; altrimenti Proposta.
- [ ] **Step 4: Add entities, DbContext mapping, indices** (`SpontaneousRun`: indice `(Status, StartsAtUtc)`; `RunParticipant`: indice su `UserId`) **and the migration**; run `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(runs): modello uscite spontanee e regole di stato`.

### Task 2: Servizio di compatibilità condiviso

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/MatchService.cs`
- Modify: `backend/src/Runtivo.Api/Matching/MatchEndpoints.cs` (usa il servizio), `MatchDto` (nuovi campi)
- Test: i `MatchTests` esistenti devono restare verdi; aggiungere `Match_cards_report_interest_sent_and_active_proposal` nel Task 3.

**Interfaces:**
- Produces: `record Compatible(Guid UserId, string DisplayName, string City, MatchScore Score, double DistanceKm, RunIntent Intent, PublicPlace Place)`
- Produces: `static class MatchService { Task<MyIntentContext?> LoadMineAsync(RuntivoDbContext, Guid userId, DateTimeOffset now, CancellationToken); Task<List<Compatible>> FindAsync(RuntivoDbContext, MyIntentContext mine, DateTimeOffset now, CancellationToken); Task<Compatible?> FindOneAsync(RuntivoDbContext, MyIntentContext mine, Guid otherUserId, DateTimeOffset now, CancellationToken); }` — `MyIntentContext` = intent + luogo del chiamante; `null` se senza annuncio attivo e non scaduto. `FindOneAsync` applica gli stessi filtri (attivo, non scaduto, account attivo, non bloccato in nessuna direzione, punteggio ≥ soglia).
- Produces: `MatchDto` con `bool InterestSent` e `Guid? RunId` aggiunti in coda (valori calcolati nell'endpoint con due query).

- [ ] **Step 1: Refactor** la query e il punteggio di `GET /api/me/matches` nel servizio senza cambiare il comportamento.
- [ ] **Step 2: Run** `dotnet test backend` — Expected: PASS (nessuna regressione).
- [ ] **Step 3: Commit** `refactor(match): servizio di compatibilità condiviso`.

### Task 3: Interesse e proposta automatica

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/InterestEndpoints.cs`, `backend/src/Runtivo.Api/Matching/RunSuggestions.cs`
- Test: `backend/tests/Runtivo.Api.Tests/InterestTests.cs`, `RunSuggestionsTests.cs`

**Interfaces:**
- Produces: `static class RunSuggestions { PublicPlace BestMeetingPlace(IEnumerable<PublicPlace> places, PublicPlace a, PublicPlace b); (DateTimeOffset StartsAtUtc, DateTime LocalDate)? NextSharedSlot(int sharedMask, DateTimeOffset now, string timeZoneId = "Europe/Rome"); }` — luogo che minimizza la distanza maggiore dai due punti di partenza (a parità, la somma minore); slot = primo istante futuro (≥ now + 24 h, entro 14 giorni) tra le fasce condivise.
- Produces: `POST /api/interests` `{ toUserId }` → `{ mutual: bool, runId: Guid? }`; `DELETE /api/interests/{userId}` (204, idempotente); `MatchDto.InterestSent/RunId` valorizzati.

- [ ] **Step 1: Write the failing unit tests** (`RunSuggestionsTests`): `Best_place_is_the_one_closest_to_both` (Prato e Firenze → luogo intermedio più equilibrato tra i sei del seed, mai uno agli estremi se esiste di meglio); `Next_slot_is_at_least_24_hours_ahead_and_at_the_band_time` (martedì sera → 19:00 Europe/Rome); `Next_slot_picks_the_earliest_of_several_shared_slots`; `No_slot_without_shared_mask_returns_null`.
- [ ] **Step 2: Write the failing integration tests** (`InterestTests`): `Interest_in_a_compatible_person_is_private_until_mutual` (A→B: `mutual=false`, B non vede nulla di A nelle sue schede salvo `InterestSent=false`); `Mutual_interest_creates_one_proposal_for_both` (B→A: `mutual=true`, `runId` presente, entrambi hanno `RunId` sulla scheda, A e B partecipanti `Pending`); `Simultaneous_mutual_interest_creates_a_single_proposal` (due `PUT` concorrenti → una sola uscita); `Interest_requires_login_and_my_own_active_intent`; `Interest_in_a_stranger_or_incompatible_person_is_404` (id casuale, passo incompatibile, bloccato, in pausa, scaduto); `Cannot_show_interest_in_myself`; `Repeated_interest_is_idempotent`; `Withdrawing_interest_removes_it`; `Match_cards_report_interest_sent_and_active_proposal`.
- [ ] **Step 3: Run** `dotnet test backend --filter "FullyQualifiedName~RunSuggestionsTests|FullyQualifiedName~InterestTests"` — Expected: FAIL.
- [ ] **Step 4: Implement `RunSuggestions`** (distanza con `Geo.DistanceKm`; slot con `Recurrence.Occurrences` su 14 giorni) **and the endpoints.** Su interesse: `MatchService.FindOneAsync` (404 se `null`), inserimento idempotente; se esiste l'interesse inverso e nessuna uscita attiva tra i due, in una transazione con lock `pg_advisory_xact_lock(hashtextextended(<coppia ordinata>, 0))` si crea l'uscita `Proposed` (creatore = chi ha chiuso la reciprocità, partecipanti `Pending`, luogo e ora suggeriti; se non c'è slot condiviso futuro l'uscita non si crea e la risposta è `mutual=true, runId=null`).
- [ ] **Step 5: Run the same tests** — Expected: PASS.
- [ ] **Step 6: Commit** `feat(runs): interesse privato e proposta automatica`.

### Task 4: Uscite — creazione, conferma, rifiuto, annullamento

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/RunEndpoints.cs`
- Test: `backend/tests/Runtivo.Api.Tests/RunTests.cs`

**Interfaces:**
- Consumes: `RunRules.Resolve`, `MatchService.FindOneAsync`
- Produces: `record RunParticipantDto(Guid UserId, string DisplayName, ParticipantStatus Status)`; `record RunDto(Guid Id, RunStatus Status, string? CancelReason, DateTimeOffset StartsAtUtc, string TimeZoneId, PlaceDto Place, int? DistanceMeters, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, List<RunParticipantDto> Participants, ParticipantStatus MyStatus, bool IsOrganizer)`; `record RunSummaryDto(Guid Id, RunStatus Status, DateTimeOffset StartsAtUtc, string PlaceName, string City, ParticipantStatus MyStatus, int ActiveParticipants)`
- Produces: `POST /api/runs` `{ placeId, startsAt, distanceMeters?, paceMinSecPerKm?, paceMaxSecPerKm?, inviteeIds[] }` → 201 `{ id }`; `GET /api/runs/{id}` (404 ai non partecipanti); `GET /api/me/runs` (future, non annullate, dove sono `Pending` o `Confirmed`); `POST /api/runs/{id}/confirm`; `POST /api/runs/{id}/decline` (da `Pending` → `Declined`, da `Confirmed` → `Left`); `POST /api/runs/{id}/cancel` (solo organizzatore).

- [ ] **Step 1: Write the failing tests** (`RunTests`, utenti con nome scelto e fascia oraria propria come in `MatchTests`): `Organizer_invites_a_compatible_person_and_the_run_is_proposed`; `Creation_is_validated` (luogo inesistente, data passata o oltre 60 giorni, 0 o 8 invitati, invitato duplicato o se stesso, passo minimo > massimo); `Cannot_invite_incompatible_or_blocked_people` (404 per quell'invitato); `A_pair_requires_both_to_allow_pairs` (400 con una coppia in cui uno ha `AllowPairs=false`; con 2 invitati lo stesso utente è ammesso); `Run_is_confirmed_only_when_everyone_confirms` (3 persone: confermano uno per volta, stato `Proposed` finché l'ultimo conferma); `Declining_removes_the_person_and_can_confirm_the_rest` (3 persone, una rifiuta, le altre due — che accettano coppie — completano → `Confirmed`); `Leaving_a_group_that_becomes_a_pair_with_someone_who_disallows_pairs_cancels_it`; `Organizer_can_cancel_and_cancelled_runs_refuse_confirmations` (409); `Runs_that_have_started_refuse_confirmations` (avanzando `api.Time`); `Only_participants_see_the_run_and_the_names` (non partecipante e anonimo → 404/401); `Concurrent_confirmations_and_declines_end_consistent` (invitati che confermano e rifiutano in parallelo → mai `Confirmed` con un `Declined` attivo; stato coerente con `RunRules`); `Organizer_has_at_most_three_open_proposals` (la quarta → 409); `My_runs_lists_only_my_future_active_runs`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~RunTests` — Expected: FAIL.
- [ ] **Step 3: Implement the endpoints.** Ogni mutazione apre una transazione, blocca la riga dell'uscita con `SELECT 1 FROM "SpontaneousRuns" WHERE "Id" = {id} FOR UPDATE`, applica la modifica al partecipante, rilegge i partecipanti e imposta stato e motivo con `RunRules.Resolve`; aggiorna `ConfirmedAt`/`UpdatedAt`; audit senza dati personali. Convalida in creazione con `MatchService.FindOneAsync` per ogni invitato (404 per l'invitato non valido), regola della coppia (`mine.AllowPairs && invitee.AllowPairs` quando c'è un solo invitato), limite di 3 uscite `Proposed` per organizzatore.
- [ ] **Step 4: Run** `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(runs): creazione, conferma e annullamento delle uscite`.

### Task 5: Blocco, cancellazione account ed export

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/RunMembership.cs` (metodo condiviso `LeaveFutureRunsAsync`)
- Modify: `backend/src/Runtivo.Api/Matching/MatchEndpoints.cs` (blocco), `backend/src/Runtivo.Api/Auth/AccountEndpoints.cs` (cancellazione ed export)
- Test: `backend/tests/Runtivo.Api.Tests/RunSafetyTests.cs`

**Interfaces:**
- Produces: `static class RunMembership { Task LeaveFutureRunsAsync(RuntivoDbContext db, Guid userId, Guid? onlyRunsWithUser, DateTimeOffset now, CancellationToken ct); }` — per ogni uscita futura non annullata dove `userId` è `Pending`/`Confirmed` (e, se `onlyRunsWithUser` è valorizzato, dove c'è anche quell'utente come partecipante attivo) imposta `Left` e applica `RunRules.Resolve` sotto lock.

- [ ] **Step 1: Write the failing tests** (`RunSafetyTests`): `Blocking_someone_leaves_the_runs_you_share_with_them` (A blocca B dentro un'uscita a 3 → A risulta `Left`, l'uscita prosegue per gli altri o si annulla per le regole); `Blocking_leaves_unrelated_runs_untouched`; `Deleting_the_account_leaves_future_runs_and_cancels_those_that_fall_below_two` (uscita a 2 → annullata con motivo; a 3 → resta con 2 se entrambi accettano coppie, altrimenti annullata); `Export_lists_my_runs_but_not_other_participants_names`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~RunSafetyTests` — Expected: FAIL.
- [ ] **Step 3: Implement `LeaveFutureRunsAsync`** e chiamarlo dal blocco (con `onlyRunsWithUser = blocked`) e, prima dell'eliminazione dell'utente, dalla cancellazione account (con `null`); estendere l'export con `Runs` (data, luogo, stato proprio, stato dell'uscita) e il numero di interessi inviati.
- [ ] **Step 4: Run** `dotnet test backend` — Expected: PASS.
- [ ] **Step 5: Commit** `feat(runs): blocco e cancellazione account coerenti con le uscite`.

### Task 6: Frontend

**Files:**
- Create: `frontend/src/app/uscite/[id]/page.tsx`, `frontend/src/app/compagni/proponi/page.tsx`
- Modify: `frontend/src/lib/api.ts` (tipi `Run`, `RunSummary`, stati e etichette), `frontend/src/components/MatchCard.tsx` («Mi interessa», «Proponi un'uscita», stato), `frontend/src/app/compagni/page.tsx` (sezione «Le tue uscite»)

**Interfaces:**
- Consumes: gli endpoint dei Task 3–4.
- Produces: pagina `/uscite/[id]` (dettaglio con partecipanti, azioni Conferma / Non posso / Annulla, consigli di sicurezza fissi: luogo pubblico, avvisa qualcuno, porta il telefono); pagina `/compagni/proponi?con=<userId>` (luogo, data e ora, distanza facoltativa, elenco dei match con caselle per aggiungere fino a 7 invitati, avviso se una coppia non è consentita).

- [ ] **Step 1: Implement the pages and components** seguendo gli stili esistenti; stati vuoti utili; pulsanti disabilitati durante la richiesta; errori del server mostrati in italiano.
- [ ] **Step 2: Run** `npm run lint` e `npm run build` in `frontend` — Expected: nessun errore.
- [ ] **Step 3: Verify in the browser** con due utenti di prova: interesse A→B (stato «Interesse inviato»), B→A (proposta creata, link all'uscita), entrambi confermano e lo stato diventa «Confermata»; un invito diretto a un terzo; layout a 375 px.
- [ ] **Step 4: Commit** `feat(runs): pagine uscite spontanee`.

### Task 7: Documentazione e chiusura

**Files:**
- Create: `docs/decisions/0007-interesse-e-uscite-spontanee.md`; Modify: `docs/piano-milestone.md`

- [ ] **Step 1: Write the decision log** (modello, reciprocità, regole di coppia, concorrenza, blocco e cancellazione, limiti noti: niente notifiche email, niente condivisione dell'uscita e segnalazioni fino a 5c, nessun contatto tra partecipanti, scelta automatica di luogo e ora da rivedere con i dati reali).
- [ ] **Step 2: Run the full verification:** `dotnet test backend` (database di test), `npm run lint`, `npm run build` — Expected: tutto verde.
- [ ] **Step 3: Commit** `docs: decisione 0007 e stato milestone 5b`.

---

## Self-review

- **Copertura (5b):** interesse privato/reciproco (T3), uscita spontanea creata da una persona (T4), conferma di tutti (T1, T4), coppie e gruppi con doppio consenso (T1, T4), ritiro (T4, T5), test di concorrenza e reciprocità (T3, T4), pagine (T6). Rimandati a 5c per scelta: segnalazioni, ruolo admin, condivisione dell'uscita, limiti ai nuovi account.
- **Coerenza dei tipi:** `ParticipantState`, `RunOutcome`, `Compatible`, `MyIntentContext`, `RunDto`, `RunSummaryDto` e `PlaceDto` (già esistente) sono definiti una volta e riusati con gli stessi nomi.
- **Proporzione:** firme, valori e test; i corpi restano a chi implementa.

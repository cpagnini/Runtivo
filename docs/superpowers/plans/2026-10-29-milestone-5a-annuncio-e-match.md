# Milestone 5a — Annuncio e match — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Chi ha un account può pubblicare un annuncio (passo, distanza, orari, zona e raggio) e vedere un elenco di persone compatibili, con motivazioni leggibili, senza che compaiano coordinate, email o luoghi esatti.

**Architecture:** Nuovo modulo `Matching` nel backend ASP.NET Core esistente. Zona e raggio poggiano su un elenco curato di luoghi pubblici (`PublicPlace`, seed nella migrazione). Il punteggio è una funzione pura (`MatchScoring`) separata dall'accesso ai dati; l'endpoint carica i candidati con prefiltro per riquadro geografico, li valuta in memoria e restituisce i migliori. Il frontend aggiunge una pagina «Compagni» con annuncio e match.

**Tech Stack:** .NET 10, EF Core + Npgsql, xUnit (test su PostgreSQL reale con `ApiFixture`), Next.js 16 / React 19.

**Spec:** `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md` (sezione milestone 5a)

## Global Constraints

- UI e messaggi in italiano; codice, API e schema in inglese.
- Distanza in linea d'aria (Haversine) tra **luoghi pubblici**; nessun GPS, nessun indirizzo libero, nessun PostGIS.
- Nessuna risposta dell'API contiene latitudine/longitudine, email o nome del luogo esatto di un'altra persona: solo nome visibile, città, distanza approssimata.
- Funzione riservata ai maggiorenni: pubblicare l'annuncio richiede `confirmAdult = true` (salvato come `AdultConfirmedAt`).
- Un solo annuncio per utente (`RunIntent.UserId` univoco). Raggio 5–30 km. Scadenza predefinita 30 giorni, massimo 90.
- Punteggio 0–100 con pesi: passo 35, distanza 20, orari 25, vicinanza 15, tipo 5; compare solo se ≥ 50 e superati i filtri obbligatori (passo, orari, raggio).
- Passo in secondi/km come nel resto dell'app (120–1200). Autorizzazioni sempre lato server; tutte le mutazioni richiedono l'header `X-Requested-With` già gestito dal middleware.

## Review Focus

- Annuncio scaduto o in pausa: non compare nei match di nessuno (test in Task 4).
- Raggio asimmetrico (A 5 km, B 30 km, distanza 10 km): nessun match, vale il minore (Task 2).
- Intervallo di passo a un solo valore (min = max): non deve dividere per zero né escludere il match (Task 2).
- Utente bloccato o che ha bloccato: sparisce da entrambe le liste, anche dopo che il match era già visibile (Task 4).
- Account sospeso o eliminato: i suoi annunci non compaiono; l'eliminazione cancella annuncio e blocchi (Task 4).
- Chi non ha un annuncio attivo non può vedere i match (409), per non rendere la ricerca una vetrina a senso unico (Task 4).

---

### Task 1: Modello dati, luoghi pubblici e distanza

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/MatchEntities.cs`, `backend/src/Runtivo.Api/Matching/Geo.cs`
- Modify: `backend/src/Runtivo.Api/Data/RuntivoDbContext.cs`
- Create: migrazione `MatchingFoundations` (`dotnet dotnet-ef migrations add MatchingFoundations --project src/Runtivo.Api --output-dir Data/Migrations`)
- Test: `backend/tests/Runtivo.Api.Tests/GeoTests.cs`

**Interfaces:**
- Produces: `enum RunType { Easy, Training, Social }`, `enum DayBand { Morning, Midday, Evening }`, `enum IntentStatus { Active, Paused }`
- Produces: `class PublicPlace { Guid Id; string Name; string City; double Latitude; double Longitude; bool IsActive }`
- Produces: `class RunIntent { Guid Id; Guid UserId; Guid HomePlaceId; int RadiusKm; int PaceMinSecPerKm; int PaceMaxSecPerKm; int DistanceMinMeters; int DistanceMaxMeters; RunType RunType; int AvailabilityMask; bool AllowPairs; IntentStatus Status; DateTimeOffset AdultConfirmedAt; DateTimeOffset ExpiresAt; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }`
- Produces: `class UserBlock { Guid BlockerId; Guid BlockedId; DateTimeOffset CreatedAt }` (chiave composta)
- Produces: `static class Geo { double DistanceKm(double lat1, double lon1, double lat2, double lon2); (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(double lat, double lon, double radiusKm); }`
- Produces: `static class Availability { int SlotBit(DayOfWeek day, DayBand band); int Mask(IEnumerable<(DayOfWeek, DayBand)> slots); IReadOnlyList<(DayOfWeek Day, DayBand Band)> Slots(int mask); }` — bit = `((int)day * 3) + (int)band`

- [ ] **Step 1: Write the failing tests** in `GeoTests.cs`: `Distance_between_Prato_and_Florence_centres_is_about_16_km` (Piazza del Comune di Prato 43.8790, 11.0963 → Piazza Santa Croce 43.7687, 11.2626: tra 15 e 19 km); `Distance_to_itself_is_zero`; `Bounding_box_contains_points_within_the_radius` (punto a 9 km dentro il riquadro da 10 km); `Availability_mask_round_trips` (martedì sera + giovedì sera → due slot, stessa maschera).
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~GeoTests` — Expected: FAIL (tipi non definiti).
- [ ] **Step 3: Implement** `Geo` (Haversine, raggio terrestre 6371 km; riquadro con 111,32 km per grado di latitudine e correzione `cos(lat)` per la longitudine) e `Availability` come da Interfaces.
- [ ] **Step 4: Define entities and DbContext mapping.** Enum come stringhe (convenzione già presente). Vincoli CHECK: `PaceMinSecPerKm <= PaceMaxSecPerKm`, ritmo 120–1200, distanza `1000 <= min <= max <= 60000`, `RadiusKm BETWEEN 5 AND 30`, `AvailabilityMask > 0`. Indice univoco su `RunIntent.UserId`; FK `UserId` cascade verso `User`, `HomePlaceId` restrict verso `PublicPlace`; `UserBlock` con FK cascade su entrambi gli utenti. Seed con `HasData` di luoghi con GUID fissi: Prato — Piazza del Comune (43.8790, 11.0963), Piazza Mercatale (43.8810, 11.0990); Firenze — Parco delle Cascine (43.7795, 11.2160), Piazzale Michelangelo (43.7629, 11.2650), Piazza Santa Croce (43.7687, 11.2626), Fortezza da Basso (43.7803, 11.2432). Coordinate approssimate: da verificare prima del lancio (annotato nella decisione 0006).
- [ ] **Step 5: Create the migration, run** `dotnet test backend` con `RUNTIVO_TEST_DB` impostata — Expected: PASS (suite esistente + `GeoTests`).
- [ ] **Step 6: Commit** `feat(match): modello annuncio, luoghi pubblici e distanza`.

### Task 2: Punteggio di compatibilità

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/MatchScoring.cs`
- Test: `backend/tests/Runtivo.Api.Tests/MatchScoringTests.cs`

**Interfaces:**
- Consumes: `RunType`, `Availability` (Task 1)
- Produces: `record IntentProfile(int PaceMin, int PaceMax, int DistMin, int DistMax, RunType Type, int Mask, int RadiusKm)`
- Produces: `record MatchScore(int Total, IReadOnlyList<string> Reasons, int SharedMask)`
- Produces: `static class MatchScoring { const int MinTotal = 50; MatchScore? Score(IntentProfile a, IntentProfile b, double distanceKm); }` — `null` se un filtro obbligatorio fallisce o il totale è < `MinTotal`. Simmetrica: `Score(a,b)` e `Score(b,a)` danno lo stesso totale.

- [ ] **Step 1: Write the failing tests** (nomi e asserzioni): `Identical_profiles_at_the_same_spot_score_100`; `Is_symmetric` (totale uguale scambiando a e b); `Pace_gap_over_60_seconds_never_matches` (5:00–5:10 contro 6:30–6:40 → null); `Single_value_pace_ranges_do_not_break` (min = max in entrambi, stesso valore → non null, nessuna eccezione); `No_shared_time_slot_never_matches`; `Radius_uses_the_smaller_of_the_two` (A 5 km, B 30 km, distanza 10 → null; distanza 4 → non null); `Reasons_are_in_italian_and_mention_shared_days` (contiene «martedì» per una maschera condivisa del martedì sera); `Score_drops_as_distance_grows` (stesso profilo a 1 km > a 9 km con raggio 10); `Different_run_type_scores_lower_than_same_type`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~MatchScoringTests` — Expected: FAIL.
- [ ] **Step 3: Implement `MatchScoring.Score`.** Filtri: `distanceKm <= min(raggi)`, almeno uno slot condiviso, divario di passo ≤ 60 s/km. Componenti (frazione 0–1 × peso): passo = sovrapposizione / larghezza minore (larghezza minima 30 s; se non si sovrappongono ma il divario è ≤ 30 s, credito parziale `0.3 × (1 − gap/30)`); distanza = come il passo con larghezza minima 1000 m e divario massimo 3000 m; orari = slot condivisi / min(slot A, slot B); vicinanza = `1 − distanceKm / min(raggi)`; tipo = 1 se uguale, 0,4 altrimenti. Totale arrotondato all'intero. Le motivazioni sono frasi italiane brevi per ciascun componente significativo (mai coordinate).
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** `feat(match): punteggio di compatibilità spiegabile`.

### Task 3: Annuncio — endpoint e luoghi

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/IntentEndpoints.cs`
- Modify: `backend/src/Runtivo.Api/Program.cs` (registrare `app.MapIntentEndpoints()`)
- Test: `backend/tests/Runtivo.Api.Tests/IntentTests.cs` (collezione `"api"`, `[SkippableFact]`)

**Interfaces:**
- Consumes: entità e `Availability` (Task 1)
- Produces: `record SlotDto(DayOfWeek Day, DayBand Band)`; `record IntentInputDto(Guid? HomePlaceId, int? RadiusKm, int? PaceMinSecPerKm, int? PaceMaxSecPerKm, int? DistanceMinMeters, int? DistanceMaxMeters, RunType? RunType, List<SlotDto>? Availability, bool? AllowPairs, bool? ConfirmAdult, int? ExpiresInDays)`; `record IntentDto(Guid Id, Guid HomePlaceId, string HomePlaceName, string City, int RadiusKm, int PaceMinSecPerKm, int PaceMaxSecPerKm, int DistanceMinMeters, int DistanceMaxMeters, RunType RunType, List<SlotDto> Availability, bool AllowPairs, IntentStatus Status, DateTimeOffset ExpiresAt)`
- Produces: `GET /api/places?city=` → `[{ id, name, city }]` (senza coordinate); `PUT /api/me/intent`; `GET /api/me/intent` (404 se assente); `POST /api/me/intent/pause`; `POST /api/me/intent/resume` (se scaduto, riparte da oggi + 30 giorni); `DELETE /api/me/intent`.

- [ ] **Step 1: Write the failing tests:** `Places_list_has_no_coordinates` (risposta senza `latitude`/`longitude`, almeno un luogo di Prato e uno di Firenze); `Publishing_requires_login`; `Publishing_requires_the_adult_confirmation` (400 senza `confirmAdult`; con `true` 200; un secondo PUT senza flag ma già confermato → 200); `Invalid_input_is_rejected` (radius 4 e 31, pace min > max, distanza 500 m, availability vuota, luogo inesistente → 400); `Put_replaces_the_existing_intent` (due PUT → un solo annuncio, valori del secondo); `Pause_and_resume_change_the_status`; `Resume_of_an_expired_intent_extends_it` (avanzando `api.Time` di 31 giorni → resume → `expiresAt` nel futuro); `Delete_removes_the_intent` (poi GET 404).
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~IntentTests` — Expected: FAIL.
- [ ] **Step 3: Implement the endpoints.** Riusare lo stile di `ClubEndpoints` (`Http.Problem`, `RequireAuthorization`). Validazioni come in Global Constraints; scadenza = `now + (ExpiresInDays ?? 30)` con tetto 90; `AdultConfirmedAt` impostato al primo `confirmAdult = true` e mai azzerato. Audit `intent.publish` / `intent.delete` senza dati personali.
- [ ] **Step 4: Run tests** — Expected: PASS.
- [ ] **Step 5: Commit** `feat(match): annuncio e luoghi pubblici`.

### Task 4: Match, blocco e privacy

**Files:**
- Create: `backend/src/Runtivo.Api/Matching/MatchEndpoints.cs`
- Modify: `backend/src/Runtivo.Api/Program.cs`, `backend/src/Runtivo.Api/Auth/AccountEndpoints.cs` (l'export include l'annuncio proprio; i blocchi si cancellano a cascata)
- Test: `backend/tests/Runtivo.Api.Tests/MatchTests.cs`

**Interfaces:**
- Consumes: `MatchScoring.Score`, `Geo`, `Availability`, `IntentProfile` (Task 1–2); `RunIntent`, `UserBlock`
- Produces: `record MatchDto(Guid UserId, string DisplayName, string City, int Score, List<string> Reasons, int PaceMinSecPerKm, int PaceMaxSecPerKm, int DistanceMinMeters, int DistanceMaxMeters, RunType RunType, List<SlotDto> SharedSlots, int ApproxDistanceKm, bool CanMeetAsPair)`
- Produces: `GET /api/me/matches?skip=0&take=20` (take ≤ 50, ordinati per punteggio decrescente poi per distanza); 409 se il chiamante non ha un annuncio attivo e non scaduto; `POST /api/users/{userId}/block` e `DELETE …/block` (idempotenti, 204; non si può bloccare sé stessi → 400).

- [ ] **Step 1: Write the failing tests** (utenti con `api.LoginAsync`, annunci via `PUT /api/me/intent`): `Compatible_people_see_each_other_with_reasons`; `Matches_require_my_own_active_intent` (409); `Response_has_no_coordinates_email_or_exact_place` (la risposta serializzata non contiene `latitude`, `longitude`, l'email dell'altro né il nome del luogo di partenza dell'altro); `Incompatible_pace_or_time_does_not_match`; `Expired_or_paused_intents_are_hidden` (pausa dell'altro → sparisce; avanzando il tempo oltre la scadenza → sparisce); `Blocking_hides_both_ways_even_if_already_listed`; `Unblocking_restores_the_match`; `Cannot_block_yourself`; `Suspended_users_are_hidden` (stato `Suspended` impostato via scope DB); `Pagination_returns_the_requested_window` (5 candidati, `take=2&skip=2`); `Pair_flag_requires_both_to_allow_pairs`; `Account_deletion_removes_intent_and_blocks`; `Export_includes_my_intent_but_not_others`.
- [ ] **Step 2: Run** `dotnet test backend --filter FullyQualifiedName~MatchTests` — Expected: FAIL.
- [ ] **Step 3: Implement `GET /api/me/matches`.** Carica l'annuncio del chiamante con il suo luogo; candidati = annunci attivi, non scaduti, con `AdultConfirmedAt`, di utenti `Active`, diversi dal chiamante, non in `UserBlock` in nessuna direzione, con luogo dentro `Geo.BoundingBox` del raggio del chiamante; massimo 500 candidati valutati in memoria con `MatchScoring.Score`; ordina, applica `skip/take`. `ApproxDistanceKm` = distanza arrotondata al km (minimo 1). `City` è la città del luogo del candidato, mai il nome del luogo.
- [ ] **Step 4: Implement block/unblock** con `INSERT` idempotente (controllo esistenza + indice sulla chiave composta); audit `user.block` senza dettagli. Aggiornare l'export con `intent` (campi dell'annuncio, nome del luogo scelto) e verificare che la cancellazione dell'account li rimuova a cascata.
- [ ] **Step 5: Run the full suite** `dotnet test backend` con `RUNTIVO_TEST_DB` — Expected: PASS, nessun test esistente rotto.
- [ ] **Step 6: Commit** `feat(match): elenco match, blocco e privacy`.

### Task 5: Frontend «Compagni»

**Files:**
- Create: `frontend/src/app/compagni/page.tsx`, `frontend/src/components/IntentForm.tsx`, `frontend/src/components/MatchCard.tsx`
- Modify: `frontend/src/lib/api.ts` (tipi `Place`, `Intent`, `Match`, `Slot` e etichette italiane per giorni, fasce e tipi di corsa), `frontend/src/components/Header.tsx` (voce «Compagni»), `frontend/src/app/globals.css`

**Interfaces:**
- Consumes: gli endpoint del Task 3 e 4; helper esistenti `api`, `parsePace`, `formatPace`, `useLoad`, `useRequireLogin`, `ErrorText`, `Loading`, `Card`
- Produces: pagina `/compagni`: se non c'è annuncio mostra il modulo; se c'è mostra riepilogo (con Pausa/Riprendi/Modifica/Elimina) e sotto l'elenco dei match.

- [ ] **Step 1: Write the page and components.** Modulo: zona (select dei luoghi raggruppati per città), raggio (5–30 km), passo da–a (`m:ss`), distanza da–a (km), tipo di corsa, griglia 7 giorni × 3 fasce con checkbox accessibili (etichette `aria-label` «martedì sera»), «anche in coppia», casella «Ho almeno 18 anni e voglio essere visibile a persone compatibili» obbligatoria al primo invio, scadenza (30/60/90 giorni). Scheda match: nome, città, «a circa N km», punteggio con etichetta testuale (non solo colore), motivazioni, slot condivisi, badge «anche in coppia», pulsante «Blocca» con conferma. Stato vuoto utile: spiega di allargare raggio e orari e mostra come invitare amici. Nessun pulsante «Mi interessa» (arriva in 5b).
- [ ] **Step 2: Run** `npm run lint` e `npm run build` in `frontend` — Expected: nessun errore, rotta `/compagni` generata.
- [ ] **Step 3: Verify in the browser** (dev: API su 5044, `web` da `.claude/launch.json`): con due utenti di prova pubblicare due annunci compatibili e controllare che ciascuno veda l'altro con motivazioni, che «Blocca» lo faccia sparire, e il layout a 375 px.
- [ ] **Step 4: Commit** `feat(match): pagina Compagni`.

### Task 6: Documentazione e chiusura

**Files:**
- Create: `docs/decisions/0006-annuncio-e-match.md`
- Modify: `docs/piano-milestone.md`, `README.md` (solo se cambia l'avvio)

- [ ] **Step 1: Write the decision log**: scelta dei luoghi pubblici come unica fonte di posizione, pesi e soglia del punteggio, regola del raggio minore, maggiore età, annuncio unico, privacy della risposta, limiti noti (coordinate dei luoghi da verificare, nessun «Mi interessa» né uscita fino a 5b, nessuna segnalazione/ruolo admin fino a 5c, match visibili solo aprendo l'app senza email).
- [ ] **Step 2: Run the whole verification:** `dotnet test backend` con DB, `npm run lint`, `npm run build` — Expected: tutto verde.
- [ ] **Step 3: Commit** `docs: decisione 0006 e piano milestone 5a`.

---

## Self-review

- **Copertura della spec (5a):** luoghi pubblici (T1, T3), annuncio con 18+ e unicità (T3), punteggio spiegabile e soglia (T2), elenco match (T4), bloccati invisibili e nessuna coordinata nelle risposte (T4), pagina utente (T5), documentazione (T6). Fuori da 5a per scelta: interesse, uscita, conferma (5b); segnalazioni, ruolo admin, condivisione uscita, limiti ai nuovi account (5c). Il blocco minimo è anticipato in T4 perché il criterio di test della 5a lo richiede.
- **Coerenza dei tipi:** `IntentProfile`, `MatchScore`, `SlotDto`, `MatchDto`, `Availability.SlotBit` e `Geo.DistanceKm` sono definiti una sola volta (T1–T2) e riusati con gli stessi nomi in T3–T5.
- **Proporzione:** il piano indica firme, valori e test; i corpi restano a chi implementa.

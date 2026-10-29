# Piano milestone 0–3

Ogni milestone è verticale e verificabile. Dettagli del prodotto in `CLAUDE.md`.

| M | Contenuto | Verifica |
|---|---|---|
| 0 | Repo, backend, frontend, health, CI, README | `dotnet test` e `npm run build` verdi; avvio locale |
| 1 (fatta, vedi decisione 0002) | Login passwordless (token monouso hashato, rate limit), profilo, club, ruoli, inviti; prima migrazione | Test autorizzazioni cross-club, token scaduti/riusati |
| 2 (fatta, vedi decisione 0003) | Evento, gruppi di passo, pagina pubblica, RSVP, cambio gruppo, disdetta, contatori | Test su Postgres reale: ultimo posto concorrente, idempotenza, cambio verso gruppo pieno |
| 3 (fatta, vedi decisione 0004) | Serie settimanali idempotenti (UTC + `Europe/Rome`), eccezioni singola data, dashboard, PWA base | Test ora legale/solare, rigenerazione senza duplicati |

## Flusso MVP «link WhatsApp → gruppo → RSVP»
- Pagine: `/e/{eventId}` (pubblica, Open Graph), `/accedi`, `/le-mie-corse`, `/club/{slug}`, `/club/{slug}/admin`.
- API: vedi §7.2 del PRD. RSVP sempre sull'utente autenticato; `PUT /api/events/{id}/rsvp` con transazione e vincolo univoco `(EventId, UserId)`; capienza controllata nella stessa transazione.
- Id evento non prevedibili (GUID) per gli eventi `club_only`.

## Anti-overbooking (M2)
Transazione con `SELECT ... FOR UPDATE` sulla riga del gruppo di destinazione, conteggio e insert/update nella stessa transazione; cambio gruppo come singolo `UPDATE` dell'RSVP esistente, quindi mai senza prenotazione se fallisce. Errore di capienza → `409 Conflict` con dati aggiornati.

## Milestone 5 — Uscite spontanee e match (design approvato)
Design: `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md`.

| M | Contenuto | Stato |
|---|---|---|
| 5a | Annuncio, luoghi pubblici, punteggio, elenco match, blocco minimo | fatta (decisione 0006) |
| 5b | Interesse reciproco, uscita spontanea, conferma, coppie e gruppi | fatta (decisione 0007) |
| 5c | Segnalazioni, ruolo admin piattaforma, condivisione uscita, limiti ai nuovi account | fatta (decisione 0008) |

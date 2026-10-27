# 0002 — Identità e club (milestone 1)

Data: 2026-10-27 · Stato: accettata

## Decisioni
- **Login passwordless:** `POST /api/auth/request-link` invia un link monouso; `POST /api/auth/verify` lo consuma e apre la sessione. Il token è casuale (256 bit), nel DB c'è solo lo SHA-256, scade dopo 15 minuti ed è consumato con un `UPDATE ... WHERE UsedAt IS NULL` atomico. La verifica è un POST e non un GET, così le anteprime dei client di posta non bruciano il link.
- **Anti-enumerazione e abuso:** `request-link` risponde sempre 202; massimo 3 link per email ogni 15 minuti (oltre, nessuna email ma sempre 202); rate limit per IP sugli endpoint di auth (`App:AuthRequestsPerMinute`, default 20/minuto).
- **Sessione:** cookie `HttpOnly`, `SameSite=Lax`, `Secure` salvo `App:RequireSecureCookie=false` (solo test), validità 30 giorni scorrevoli. Le API rispondono 401/403, mai redirect.
- **CSRF:** oltre a `SameSite=Lax`, ogni richiesta non-GET su `/api` deve avere l'header `X-Requested-With: runtivo`. Il frontend lo aggiungerà in un unico client HTTP.
- **Email:** interfaccia `IEmailSender`. In sviluppo il link va nel log; in produzione fallisce in modo esplicito finché non è scelto un provider (decisione aperta n. 4 del PRD).
- **Ruoli club:** `Member`, `Leader`, `Admin`; stato `Pending` / `Active`. Il creatore è `Admin`.
- **Iscrizione (decisione aperta n. 2):** supportati entrambi i casi base. Con invito (monouso, 7 giorni, opzionalmente legato a un'email) l'iscrizione è subito attiva; senza invito è una richiesta `Pending` da approvare.
- **Ultimo admin:** non può essere declassato né rimosso (409). Le modifiche ai ruoli di un club sono serializzate con `SELECT ... FOR UPDATE` sulla riga del club.
- **Visibilità:** nome, descrizione e città del club sono pubblici; l'elenco membri (con email) è visibile solo agli admin di quel club. Chi non è admin del club riceve 403.
- **Audit:** `AuditEntry` per creazione club, inviti, approvazioni, cambi ruolo, rimozioni, senza token né payload.
- **Migrazioni:** tool locale `dotnet-ef` (`dotnet-tools.json`). Si applicano all'avvio in Development o con `Database:MigrateOnStartup=true`; in produzione vanno applicate esplicitamente.

## Limiti noti (da riprendere)
- Il profilo non permette ancora di cancellare il passo preferito una volta impostato.
- La sessione non controlla lo stato dell'account a ogni richiesta (sospensione efficace al login successivo).
- Export e cancellazione account (P0 del PRD) non ancora presenti.

## Frontend (milestone 1)
- Pagine: `/accedi`, `/accedi/verifica`, `/profilo`, `/club`, `/club/nuovo`, `/club/[id]` (con pannello admin: approva, ruoli, rimuovi, inviti), `/club/invito`.
- Tutte client-side e protette da `useRequireLogin`: senza sessione portano a `/accedi?next=...`. La destinazione è salvata in `localStorage` perché il link email si apre spesso in un'altra scheda; `next` accetta solo percorsi interni.
- Le chiamate passano da un unico client (`src/lib/api.ts`) che aggiunge l'header CSRF e traduce gli errori in messaggi italiani.
- Verifica link e accettazione invito partono una sola volta (token monouso), anche con il doppio render di sviluppo.
- Provato a mano nel browser: login, creazione club, invito, accesso di un secondo utente e iscrizione tramite invito; profilo su larghezza telefono.

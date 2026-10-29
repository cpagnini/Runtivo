# 0009 — Accesso con Google

Data: 2026-10-29 · Stato: accettata · Risponde alla decisione aperta n. 5 del PRD («passwordless puro o provider OAuth opzionale»)

## Decisioni
- **Opzionale e aggiuntivo:** il link via email resta. «Continua con Google» compare solo se `Auth:Google:ClientId` e `ClientSecret` sono configurati (`GET /api/auth/providers`). Guida alle credenziali: `docs/accesso-google.md`.
- **Flusso:** OpenID Connect, «authorization code» con **PKCE (S256)**, `state` e `nonce`, gestito interamente dal server (`/api/auth/google/start` e `/callback`, raggiungibili dal sito tramite il proxy `/api`). Alla fine emette lo stesso cookie di sessione del link via email: il resto dell'app non cambia.
- **Stato del flusso** in un cookie protetto (Data Protection), `HttpOnly`, `SameSite=Lax` (il ritorno da Google è una navigazione di primo livello), valido 10 minuti e **monouso** (si elimina a ogni ritorno).
- **Scambio del codice** server-a-server con il segreto del client; sul canale diretto TLS l'`id_token` non richiede la verifica della firma (regola di OpenID Connect), ma si controllano **emittente, destinatario (`aud`), scadenza e `nonce`**.
- **Solo l'email:** scope `openid email`. **Non si legge né si importa il nome o la foto**: il nome visibile resta quello predefinito (parte locale dell'email) e va scelto nel profilo prima di pubblicare un annuncio, così il nome reale non finisce nelle schede dei match.
- **Collegamento degli account:** al primo accesso si cerca l'utente per email **solo se Google dichiara l'email verificata** (altrimenti rifiuto); si crea un legame `ExternalLogin(Provider, Subject)`. Dai successivi accessi vale il `subject` di Google, anche se l'email su Google cambia. Il legame si cancella insieme all'account.
- **Redirect sicuro:** il percorso di ritorno (`next`) è accettato solo se interno (no indirizzi assoluti, no `//`, no `\`), altrimenti `/club`.
- **Account sospesi** non entrano; gli errori tornano a `/accedi?errore=…` con messaggi in italiano (annullato, scaduto, non confermato da Google, email non verificata, sospeso). Rate limit come per l'accesso via email.

## Test
17 test con un provider simulato: PKCE e parametri della richiesta, creazione e collegamento account, `subject` stabile, email non verificata, state/nonce/audience/emittente/scadenza errati, cookie monouso, utente che nega l'accesso, sospesi, redirect aperti, cancellazione account, lettura dell'`id_token`. Togliendo il controllo dell'email verificata il test relativo fallisce.

## Non verificato
Lo **scambio con Google vero** non si può provare in automatico: serve registrare l'app e fare un accesso con un account reale (vedi la guida). È stata verificata la parte nostra: il sito inoltra correttamente reindirizzamento e cookie fino a `accounts.google.com`.

## Limiti e prossimi passi
- **Facebook e Apple** non ci sono. Facebook è più oneroso: revisione dell'app per la produzione, informativa privacy, indirizzo per la cancellazione dei dati e email non sempre presente o verificata (rischio di furto di account se ci si fida dell'email). Apple ha senso per chi usa iPhone. Il design (tabella `ExternalLogin` con `Provider`) li accoglie.
- Chi entra con Google e non ha mai usato il link via email non ha una password da perdere, ma dipende dall'accesso al proprio account Google.
- L'informativa privacy deve citare Google come fornitore dell'accesso.
- In locale i segreti stanno nei «user secrets» di .NET, non nel repository.

# 0008 — Sicurezza e moderazione (milestone 5c)

Data: 2026-10-29 · Stato: accettata · Design: `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md` · Piano: `docs/superpowers/plans/2026-10-29-milestone-5c-sicurezza-e-moderazione.md`

## Segnalazioni
- `POST /api/reports`: motivo (molestie, profilo falso, situazione di disagio o pericolo, spam, altro), dettagli facoltativi (max 500 caratteri), eventuale uscita di riferimento (allora segnalante e segnalato devono esserne entrambi partecipanti).
- La persona segnalata **non vede mai** la segnalazione né chi l'ha fatta (nessun suo endpoint la espone). Ripetere una segnalazione aperta sulla stessa persona nelle 24 ore restituisce la stessa; massimo 10 segnalazioni al giorno per persona.
- Alla cancellazione dell'account il segnalante diventa anonimo ma la segnalazione resta alla moderazione; quelle ricevute non si cancellano con l'account del segnalato. L'export include le segnalazioni **fatte** dalla persona (motivo, stato, data), mai quelle ricevute.
- In interfaccia «Segnala» è su ogni scheda e su ogni partecipante di un'uscita, con l'offerta di bloccare subito la persona.

## Amministratore piattaforma
- Il ruolo si assegna **solo da configurazione** (`Platform:AdminEmails`, vuoto di default): nessun endpoint permette di diventare admin e toglierlo dalla configurazione ha effetto subito. Il confronto delle email non distingue le maiuscole. In sviluppo è impostato `admin@example.com`.
- `/api/admin/*` risponde **403** a chiunque non sia admin (anche con id validi) e 401 agli anonimi. `GET /api/me` espone `isPlatformAdmin`, usato solo per mostrare la voce «Moderazione».
- Coda: segnalazioni aperte, con nome del segnalato, nome del segnalante (mai le email) e quante altre segnalazioni aperte ha la stessa persona. Azioni: **Archivia** (nessun provvedimento) e **Sospendi**.
- **Sospensione:** l'account non può più accedere e la sessione aperta cade alla richiesta successiva; l'annuncio va in pausa; la persona esce dalle uscite future con le stesse regole del blocco; tutte le segnalazioni aperte su di lei si chiudono come «provvedimento preso». Non si può sospendere sé stessi né un altro amministratore. Esiste anche la revoca della sospensione.
- Ogni azione amministrativa scrive una voce di **audit** con l'attore (`admin.report.dismiss`, `admin.user.suspend`, `admin.user.unsuspend`).

## Condivisione dell'uscita con una persona di fiducia
- `POST /api/runs/{id}/share` (solo partecipanti attivi, uscita non annullata né conclusa) restituisce un link `/sicurezza/<token>`. Il token è casuale a 256 bit, **mostrato una sola volta** e salvato solo come hash; scade 6 ore dopo l'inizio dell'uscita; massimo 5 link attivi per persona e uscita; si revocano con `DELETE`.
- La lettura (`GET /api/safety/{token}`) è pubblica e minimale: nome di chi ha condiviso, giorno e ora, luogo pubblico, stato e nomi visibili dei partecipanti attivi. Mai email, id o coordinate. Se l'uscita viene annullata non si mostrano più i partecipanti. Risposta `no-store`; la pagina è `noindex` e senza referrer.

## Limiti per gli account nuovi
Per `App:NewAccountDays` giorni dalla registrazione (default 7, 0 = disattivato): al massimo **5 «Mi interessa» nelle ultime 24 ore** (429) e **1 proposta di uscita in attesa** (409). Conteggio e inserimento sono atomici con un lock per persona, così richieste parallele non superano il tetto. Dopo il periodo valgono i limiti normali (3 proposte in attesa). Gli altri test girano con il periodo disattivato; i test dedicati usano un'app con 7 giorni.

## Test
Da 164 a 196 test su PostgreSQL reale: segnalazioni (limiti, duplicati, privacy, anonimizzazione), moderazione (403 ai non admin, sospensione con tutte le sue conseguenze, audit), condivisione (scadenza, revoca, hash, annullamento) e limiti (finestra mobile, periodo scaduto, richieste parallele). La fixture svuota anche le segnalazioni all'avvio (solo su database con «test» nel nome).

## Limiti noti
- **Nessuna notifica** a chi segnala o a chi viene sospeso; la coda è da consultare a mano in `/admin`.
- Nessuna possibilità di **contestare** una sospensione né strumenti per cercare un utente o vedere lo storico: la moderazione è minima e va organizzata (chi la fa, con quali tempi) prima del lancio.
- Le segnalazioni non cambiano da sole la visibilità di un profilo: serve un'azione dell'amministratore.
- La condivisione non avvisa automaticamente la persona di fiducia se chi corre non dà notizie: mostra solo i dettagli.
- Il ruolo di amministratore dipende dall'email: va protetta come un'identità sensibile (accesso senza password via email).
- Informativa privacy, termini d'uso, responsabilità e policy sui minori non sono scritti e richiedono una verifica legale prima del lancio (PRD §9).

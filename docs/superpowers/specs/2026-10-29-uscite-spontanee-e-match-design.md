# Uscite spontanee e match — design

Data: 2026-10-29 · Stato: **da approvare** · Anticipa la V1 del PRD («Chi corre con me?», §3.2, milestone 5)

## Intento
Chi non fa parte di un club deve poter trovare qualcuno con cui correre: pubblica un **annuncio** (passo, distanza, quando, raggio d'azione, che tipo di corsa cerca) e l'app propone le persone e le corse più **compatibili**. Il risultato è un'**uscita in un luogo pubblico**, anche **in coppia**, non un contatto privato.

## Obiettivi
- Annuncio in meno di un minuto; match spiegati («passo compatibile, a ~3 km, libero il martedì sera»).
- Uscite da 2 persone in su, con tutele proporzionate al rischio.
- Funzionare in un'area con pochi utenti senza sembrare vuota (le corse aperte e i club restano visibili).

## Non obiettivi
- Chat in-app, messaggi privati, foto, feed, classifiche. Niente app di dating (PRD §1.2).
- Posizione GPS, indirizzi di casa, mappe in tempo reale.
- Modelli di machine learning: il punteggio è una formula leggibile.

## Anti-obiettivi (sarebbe un fallimento anche se funziona)
- Una persona può essere contattata o localizzata da uno sconosciuto senza averlo scelto.
- Un rifiuto è visibile all'altra persona.
- Le coordinate precise di qualcuno compaiono in una risposta dell'API.

## Concetti
- **Annuncio** (`RunIntent`): passo da–a, distanza da–a, tipo di corsa (tranquilla, allenamento, sociale), disponibilità (giorni + fasce: mattina, pranzo, sera), **zona** e **raggio** (5–30 km), preferenza «anche in coppia» / «solo gruppi di 3+», scadenza (es. 30 giorni), stato attivo/in pausa. Per pubblicarlo serve dichiararsi **maggiorenni**.
- **Luogo pubblico** (`PublicPlace`): elenco curato di punti di ritrovo (nome, città, coordinate). Sia la **zona** dell'annuncio sia il ritrovo si scelgono da lì: così la privacy è garantita dal modello (nessun indirizzo libero, nessun GPS) e la distanza si calcola tra punti pubblici. Partenza: Prato e Firenze; chiunque può suggerirne uno, che entra dopo verifica.
- **Match**: punteggio 0–100 con componenti visibili: passo (sovrapposizione degli intervalli), distanza, orari in comune, vicinanza, tipo di corsa. Soglia minima per comparire. Chi è bloccato o ha bloccato non compare.
- **Interesse** («Mi interessa»): privato. L'altra persona non sa di essere stata scartata né vede chi ha mostrato interesse finché non è reciproco.
- **Uscita spontanea** (`SpontaneousRun`): data, ora, luogo pubblico, partecipanti (min 2). Nasce da un interesse reciproco o la crea direttamente una persona («domani 7:00, Cascine, 10 km, passo 5:30») e gli altri aderiscono come a un evento.
- **Conferma**: l'uscita è confermata solo quando tutti i partecipanti hanno accettato giorno, ora e luogo.

## Uscite in coppia: tutele
1. Solo luoghi pubblici dell'elenco; nessun ritrovo libero.
2. **Doppio consenso**: una coppia si forma solo se entrambi hanno scelto «anche in coppia».
3. **Condividi la mia uscita**: link per una persona di fiducia con giorno, ora, luogo e nome del compagno; scade a fine uscita.
4. Si vedono solo nome visibile, passo, zona generica («~3 km») e tipo di corsa; mai email né coordinate.
5. **Segnala e blocca** a un tocco su ogni scheda e pagina uscita; il blocco è reciproco e invisibile.
6. Limiti per i nuovi account (proposte/interessi a settimana) e rate limit.
7. Avvisi pratici prima della conferma (luogo pubblico, avvisa qualcuno).
8. Scambio di contatti: **non** nella prima versione; si aggiunge dopo solo con consenso esplicito di entrambi.

## Approcci valutati
- **A. Match → uscita pubblica (scelto):** niente contatto privato; sicuro, coerente col PRD, copre coppie e gruppi. Costo: meno «calore» di una chat.
- B. Match → chat privata: più coinvolgimento, ma aggiunge moderazione, rischio e un prodotto diverso dal PRD.
- C. Solo bacheca di corse aperte (senza profili): il più semplice, ma non è il «trova i best match» richiesto. Resta comunque parte di A (le corse aperte).

Geografia: **distanza in linea d'aria (formula di Haversine) su coordinate di luoghi pubblici**, con prefiltro per riquadro; PostGIS rimandato finché i volumi non lo giustificano.

## Dati (nuove tabelle)
`PublicPlace`, `RunIntent` (+ disponibilità), `Interest` (da utente a utente, per annuncio), `SpontaneousRun`, `RunParticipant` (stato: invitato, confermato, rifiutato, ritirato), `UserBlock`, `Report`, `SafetyShare` (token hashato, scadenza). Vincoli: un solo annuncio attivo per utente; un solo `Interest` per coppia e annuncio; un partecipante per uscita; luogo obbligatorio e appartenente all'elenco.

## API e pagine (sintesi)
- `POST/PUT/DELETE /api/me/intent`, `GET /api/me/matches` (paginato), `POST /api/interests`, `POST /api/runs`, `POST /api/runs/{id}/join|confirm|leave|cancel`, `GET /api/runs/{id}`, `POST /api/runs/{id}/share`, `POST /api/users/{id}/block`, `POST /api/reports`, `GET /api/places?city=`.
- Pagine: «Cerco compagni» (annuncio), «I miei match», uscita spontanea (come la pagina evento), segnalazione/blocco, vista di condivisione per la persona di fiducia.

## Privacy e conformità
Visibilità nei match **solo con consenso esplicito** e revocabile (pausa/elimina annuncio). I dati dell'annuncio sono inclusi in export e cancellazione account. Zona e raggio sono dati personali: informativa dedicata. Funzione solo per maggiorenni; verifica legale prima dell'apertura pubblica (PRD §9).

## Moderazione
Segnalazioni in coda; nuovo ruolo di **amministratore piattaforma** (PRD §2) con sospensione account e audit. Nel pilot la coda può essere letta via API/consegna manuale.

## Milestone (verticali e verificabili)
- **5a — Annuncio e match:** luoghi pubblici, annuncio, punteggio, elenco match. *Test:* punteggio (unit), 18+, bloccati invisibili, nessuna coordinata nelle risposte.
- **5b — Interesse e uscita:** interesse privato/reciproco, uscita spontanea, conferma, coppie e gruppi, regole del doppio consenso. *Test su DB reale:* conferme concorrenti, reciprocità, ritiro.
- **5c — Sicurezza:** blocco, segnalazione, ruolo admin piattaforma, condivisione uscita, limiti ai nuovi account.

## Dipendenze e rischi
- Senza email i match si vedono solo aprendo l'app; le notifiche aspettano il provider.
- Con pochi utenti il match può restare vuoto: mostrare corse aperte e club vicini e invitare a pubblicare l'annuncio.
- L'elenco dei luoghi pubblici va curato a mano per l'area pilota.

## Scelte lasciate a chi costruisce
Pesi esatti del punteggio, soglia minima, durata e limiti dei nuovi account, testi degli avvisi, formato del link di condivisione, ordine dei campi nei moduli.

## Da confermare (assunzioni mie)
Solo maggiorenni; nessun filtro per genere in questa versione (richiederebbe un dato sensibile e una scelta di prodotto/legale); nessuno scambio di contatti all'inizio.

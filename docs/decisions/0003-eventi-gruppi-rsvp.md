# 0003 — Eventi, gruppi di passo e RSVP (milestone 2)

Data: 2026-10-28 · Stato: accettata

## Decisioni
- **Modello:** `RunEvent` → `PaceGroup` (1..8 per evento) → `EventRsvp` (`Going` con gruppo obbligatorio, `NotGoing` senza gruppo; vincolo CHECK nel DB). Un solo RSVP per evento e utente (indice univoco `(EventId, UserId)`). Passo in secondi/km con `min <= max`, capienza e distanza positive: tutti vincoli nel DB.
- **Anti-overbooking:** la richiesta RSVP apre una transazione, blocca la riga del gruppo di destinazione (`SELECT ... FOR UPDATE`), conta i partecipanti e scrive nella stessa transazione. Il cambio gruppo è un `UPDATE` dello stesso RSVP: se il gruppo nuovo è pieno (409) l'adesione precedente resta intatta. Ripetere la richiesta è idempotente. Richieste simultanee dello stesso utente su gruppi diversi: vince la prima, l'altra riceve 409.
- **Verificato:** il test sull'ultimo posto (8 richieste simultanee, 5 round) fallisce se si toglie il lock (8 successi su 1 posto) e passa con il lock.
- **Visibilità:** `Public` visibile a tutti; `ClubOnly` solo ai membri attivi, per gli altri **404** (non 403: l'esistenza di un evento riservato non si deduce dall'id). Contatori visibili a chi vede l'evento; **nomi dei partecipanti e capogruppo solo ai membri del club** (default prudente, decisione aperta n. 3 del PRD).
- **Regole:** un evento annullato o già iniziato rifiuta nuovi RSVP (409); le adesioni esistenti restano. La disdetta (`DELETE`) cancella il RSVP e libera subito il posto. Un evento annullato non si modifica.
- **Orari:** salvati in UTC con fuso `Europe/Rome` (per ora fisso). Il modulo web usa l'orario del dispositivo.
- **Anteprima condivisione:** `/e/[id]` genera i meta Open Graph lato server leggendo l'evento senza sessione, quindi solo gli eventi pubblici hanno anteprima ricca.

## Limiti noti
- Nessuna modifica dell'evento dal frontend (le API `PATCH /api/events/{id}` e `PUT .../groups/{id}` esistono e sono testate); non si possono aggiungere o togliere gruppi dopo la creazione.
- Il capogruppo si può impostare solo via API (serve un selettore dei membri).
- Il gruppo di un RSVP appartiene allo stesso evento solo per controllo applicativo (testato), non per chiave esterna composta.
- Ricorrenze, eccezioni e dashboard sono la milestone 3.
- L'ora legale è già corretta per eventi singoli; la generazione delle serie è da fare in M3.

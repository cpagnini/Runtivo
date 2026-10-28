# 0004 — Ricorrenze, dashboard e PWA (milestone 3)

Data: 2026-10-28 · Stato: accettata

## Ricorrenze
- **Modello:** `EventSeries` (giorno della settimana, ora locale, fuso IANA `Europe/Rome`, date di inizio/fine, modello dei gruppi in JSON). Le occorrenze sono normali `RunEvent` con `SeriesId` e `OccurrenceLocalDate`.
- **Idempotenza:** indice univoco `(SeriesId, OccurrenceLocalDate)`. La generazione calcola le date mancanti e, se un'altra richiesta le crea in parallelo, l'indice respinge il duplicato e si riprova una volta ricalcolando.
- **Ora legale:** l'istante UTC si calcola dall'ora locale con `TimeZoneInfo`; 19:00 resta 19:00 a Roma prima e dopo il cambio (test su marzo 2027 e ottobre 2026). Un'ora locale inesistente (salto in avanti) passa alla prima ora valida.
- **Una data per volta:** modificare o annullare un'occorrenza usa gli endpoint normali dell'evento e non tocca le altre. Rigenerare non ricrea né ripristina le date già esistenti, annullate o modificate comprese.
- **Orizzonte:** 8 settimane. La creazione della serie genera subito; `POST /api/series/{id}/generate` e un servizio in background (ogni 6 ore, disattivabile con `Series:BackgroundGeneration=false`) estendono l'orizzonte, così il club non ricrea nulla a mano.
- **Fermare una serie:** `POST /api/series/{id}/stop`, con l'opzione di annullare anche le uscite future. Le uscite già create restano se non si chiede di annullarle.
- **Modello dei gruppi:** copia di nome, distanza, passo e posti; il capogruppo non è nel modello e si assegna sulle singole uscite.

## Dashboard organizzatore
`GET /api/clubs/{id}/dashboard` (solo admin del club): uscite dei prossimi 60 giorni con iscritti, non partecipanti e riempimento per gruppo, più l'elenco delle serie. Pagina `/club/[id]/gestione`.

## Modifica di una singola uscita
Pagina `/e/[id]/modifica` (admin): dati dell'evento e di ogni gruppo. L'evento si salva per primo e poi i gruppi uno alla volta; se un gruppo fallisce (ad esempio posti sotto gli iscritti) l'errore lo dice e le altre modifiche restano salvate. Non si aggiungono né tolgono gruppi.

## PWA
- Manifest (`/manifest.webmanifest`), icone 192/512/maskable e icona iOS; installazione sempre facoltativa. La pagina iniziale installata è «Le mie corse».
- **Service worker prudente** (`public/sw.js`): nessuna cache di pagine o API, per non mostrare mai iscritti o posti vecchi. L'unica copia locale è `/offline.html`, mostrata se manca la connessione. Registrato solo in produzione.
- Verificato in produzione locale: service worker attivo, solo `offline.html` in cache, fallback offline funzionante.

## Limiti noti
- Il modello di una serie non si modifica dopo la creazione (si ferma e se ne crea un'altra); le singole date sì.
- L'orario nei moduli è quello del dispositivo; il fuso della serie è fisso a `Europe/Rome`.
- Il capogruppo si imposta solo via API.
- Il servizio in background non ha un test automatico dedicato (la logica di generazione sì).
- Non c'è ancora un test end-to-end automatico del browser: i flussi sono stati provati a mano.

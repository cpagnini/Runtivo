# 0007 — Interesse e uscite spontanee (milestone 5b)

Data: 2026-10-29 · Stato: accettata · Design: `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md` · Piano: `docs/superpowers/plans/2026-10-29-milestone-5b-interesse-e-uscite.md`

## Modello
- `Interest` (da persona a persona), `SpontaneousRun` (luogo pubblico, data e ora UTC con fuso `Europe/Rome`, distanza e passo facoltativi, stato `Proposed` / `Confirmed` / `Cancelled`) e `RunParticipant` (`Pending` / `Confirmed` / `Declined` / `Left`). Tutti con vincoli nel DB.

## Interesse privato e proposta automatica
- «Mi interessa» è possibile solo verso persone **compatibili adesso** (stesse regole dell'elenco match); per ogni altro id la risposta è **404**, così l'endpoint non serve a sondare chi esiste o chi ha un annuncio. Serve un annuncio proprio attivo (409 altrimenti).
- L'interesse resta invisibile all'altra persona finché non è reciproco. Se B ricambia, nasce **una** proposta di uscita per entrambi (lock di transazione sulla coppia: due interessi simultanei non creano due uscite).
- La proposta sceglie: **luogo** = punto pubblico che minimizza la distanza della persona più lontana; **orario** = la prima fascia in comune almeno 24 ore avanti, entro 14 giorni (mattina 08:00, pausa pranzo 12:30, sera 19:00); **passo e distanza** = intervallo comune. Se non c'è una fascia utile l'uscita non si crea e la risposta lo dice.
- Una coppia richiede il consenso di entrambi: se uno dei due ha «solo gruppi», l'interesse è rifiutato (400) e si propone un'uscita con almeno altre due persone.

## Uscite create da una persona
`POST /api/runs` invita da 1 a 7 persone, ciascuna compatibile con chi propone (404 altrimenti). Data tra 1 ora e 60 giorni; al massimo 3 proposte in attesa per organizzatore. Chi propone risulta già confermato, gli invitati `Pending`.

## Regole di stato (`RunRules`, funzione pura)
- Attivi = in attesa + confermati. Meno di 2 attivi → **annullata**. Esattamente 2 attivi e uno non accetta le coppie → **annullata** (un gruppo che si riduce a due). Tutti gli attivi confermati → **confermata**. Altrimenti **proposta**.
- Confermare, rifiutare/lasciare e annullare (solo l'organizzatore) bloccano la riga dell'uscita (`SELECT … FOR UPDATE`): richieste simultanee si accodano e lo stato finale deriva sempre dalla stessa funzione. Un test con conferme e rifiuti in parallelo fallisce se si toglie il lock.
- Le uscite annullate o già iniziate rifiutano conferme e rifiuti. Chi ha lasciato non può riconfermare.

## Privacy e sicurezza
- Solo i partecipanti vedono l'uscita e i nomi degli altri (404 per i non partecipanti); nessuna risposta contiene coordinate o email.
- **Blocco:** chi blocca esce dalle uscite future condivise con la persona bloccata e le regole di stato si riapplicano (l'uscita prosegue o si annulla).
- **Cancellazione account:** prima di eliminare l'utente si esce dalle sue uscite future con le stesse regole, quindi nessuna uscita resta con partecipanti fantasma.
- **Export:** include le proprie uscite (data, luogo, stati) e il numero di interessi inviati, senza i nomi degli altri partecipanti.
- La pagina dell'uscita mostra consigli di sicurezza fissi (luogo pubblico, avvisare qualcuno, telefono, possibilità di lasciare e bloccare).

## Test
158 → 164 test su PostgreSQL reale, tra cui: interesse reciproco simultaneo (3 giri), conferme e rifiuti concorrenti, coppie con preferenze diverse, blocco e cancellazione account dentro uscite condivise. Helper condiviso `MatchHarness` con «nicchie» di orari e città per isolare i test.

## Limiti noti
- **Nessuna notifica:** le proposte si vedono solo aprendo l'app (sezione «Le tue uscite» in `/compagni`); serve il provider email.
- Nessuna condivisione dell'uscita con una persona di fiducia, segnalazioni, ruolo di amministratore della piattaforma né limiti ai nuovi account (5c).
- Luogo e orario sono suggeriti in automatico dai soli annunci: da rivedere con dati reali. Non c'è modo di proporre un'altra data a un'uscita esistente (si annulla e se ne crea un'altra).
- Gli invitati vedono i nomi degli altri invitati prima di confermare.
- I partecipanti non hanno modo di scambiarsi un contatto: per decisione di prodotto, almeno per ora.

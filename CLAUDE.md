# Runtivo — Specifica di prodotto e istruzioni per Claude Code

> **Stato:** proposta iniziale / PRD operativo — versione 0.1, 27 ottobre 2026  
> **Lingua dell'app:** italiano (predisporre la localizzazione)  
> **Nome:** "Runtivo" — «Trova il tuo passo. Corri insieme.» (sostituisce il nome provvisorio "RunTogether"; verificare marchio e dominio prima dell'uso commerciale)
> **Destinatario:** Claude / Claude Code, per analisi, progettazione e sviluppo incrementale

## 0. Come usare questo documento (istruzioni per Claude)

Se stai lavorando come assistente di sviluppo:

1. Considera questo documento la **fonte iniziale dei requisiti di prodotto**, non una descrizione di software già implementato.
2. Prima di scrivere codice, ispeziona il repository e segnala eventuali incompatibilità tra progetto esistente e architettura proposta. Non sovrascrivere lavoro presente.
3. **Non implementare tutto il documento in una volta.** Proponi un piano per piccole milestone verticali verificabili, poi realizza soltanto la milestone richiesta dall'utente.
4. Per una funzionalità, includi codice, migrazioni, test pertinenti, gestione degli errori, autorizzazioni e una breve procedura di verifica.
5. Se una decisione manca, applica la scelta minima, esplicitala e registrala in `docs/decisions/` o in una sezione di decision log. Chiedi conferma solo quando la scelta è realmente bloccante o costosa da invertire.
6. Privilegia semplicità, sicurezza, UX mobile e manutenzione. **Non creare microservizi, chat proprietarie, algoritmi ML o integrazioni complesse** senza una motivazione concreta.
7. Distingui rigorosamente **MVP**, **versione successiva** e **idee future**. Non presentare le ipotesi commerciali come risultati già verificati.
8. UI e messaggi utente in italiano; nomi di classi, API, schema e codice in inglese. Documentazione tecnica in italiano, salvo diversa richiesta.
9. Prima di ogni modifica sostanziale, riepiloga in poche righe cosa realizzi, cosa lasci fuori e quali test eseguirai.

---

## 1. Visione

Creare una piattaforma **gratuita per chi corre** che permetta:

- ai running club e ai gruppi informali di organizzare uscite settimanali;
- agli amministratori di offrire **più gruppi nella stessa serata**, con ritmi, distanze, capigruppo e posti eventualmente diversi;
- ai partecipanti di confermare, rifiutare o modificare facilmente la propria adesione;
- a chi non appartiene a un club di proporre un'uscita e trovare compagni compatibili per zona, orario e passo;
- in futuro, a negozi e organizzatori di gare di raggiungere comunità locali tramite sponsorizzazioni pertinenti e iniziative, senza rendere obbligatorio un abbonamento per i runner.

**Proposta di valore:** «Trova persone con cui correre, al tuo ritmo, senza dover già conoscere un gruppo».

**Scelta strategica:** partire dalla gestione dei club (uso ricorrente, problema chiaro) per costruire gradualmente una rete locale di runner e iniziative spontanee.

### 1.1 Problema di partenza

Oggi molti gruppi utilizzano WhatsApp:

- i sondaggi vengono persi tra i messaggi;
- è difficile gestire più gruppi di passo nello stesso allenamento;
- conferme e cambi di programma non sono sempre aggiornati;
- un nuovo arrivato non conosce necessariamente ritmo, distanza e punto di partenza;
- per chi cerca compagnia fuori da un gruppo abituale non esiste un punto d'incontro semplice e centralizzato.

### 1.2 Cosa NON vogliamo costruire

Non è un'alternativa a Strava per tracciamento GPS, statistiche, segmenti, smartwatch o piani d'allenamento. Non è una chat generalista. Non è un'app di dating. Non è un social basato su feed infiniti o engagement artificiale.

---

## 2. Target e ruoli

| Ruolo | Esigenza principale | Permessi indicativi |
|---|---|---|
| Visitatore | Scoprire un'uscita da un link WhatsApp | Visualizzare gli eventi condivisi secondo la loro visibilità |
| Runner registrato | Partecipare, disdire, cercare compagnia | Gestire profilo, RSVP e proprie proposte |
| Membro del club | Consultare eventi e partecipanti del proprio club | Come runner, con accesso agli eventi riservati |
| Capogruppo | Gestire il gruppo di passo assegnato | Visualizzare partecipanti e informazioni operative del proprio gruppo |
| Organizzatore / amministratore del club | Creare eventi ricorrenti, gruppi e avvisi | Gestire solo il proprio club e i relativi eventi |
| Amministratore piattaforma | Moderazione e operazioni | Poter intervenire per sicurezza, supporto e abusi, con audit |
| Partner commerciale (futuro) | Promuovere iniziative locali | Accesso separato, senza dati personali dei runner |

**Principio importante:** autorizzazioni sempre lato server e limitate al club o all'evento interessato. L'appartenenza a un club non conferisce privilegi in altri club.

---

## 3. Esperienza principale

### 3.1 Evento settimanale di club

Esempio illustrativo:

**Martedì Running — martedì, ore 19:00**  
Ritrovo: luogo pubblico definito dal club

| Gruppo | Distanza | Passo indicativo | Capogruppo |
|---|---:|---|---|
| Relax | 6 km | 6:15–6:45 min/km | Marco |
| Intermedio | 9 km | 5:20–5:40 min/km | Sara |
| Veloce | 12 km | 4:30–4:50 min/km | Luca |

Flusso:

1. L'amministratore crea o programma l'evento settimanale.
2. Configura uno o più gruppi di passo, ciascuno con distanza, passo, capogruppo e capacità facoltativa.
3. Condivide un **link pubblico o riservato**, a seconda dell'evento, nella chat WhatsApp già esistente.
4. Il runner apre una pagina mobile, vede tutte le informazioni e sceglie un gruppo.
5. Si autentica in modo leggero se non ha già una sessione. **Non serve installare l'app.**
6. Conferma la partecipazione; in seguito può cambiare gruppo o annullare.
7. Il sistema mantiene contatori e liste aggiornati; gli organizzatori possono segnalare un cambio di programma o annullamento.

I partecipanti devono poter esprimere «Partecipo» / «Non partecipo». `Non partecipo` non occupa un posto. Per l'MVP non è necessario un terzo stato «Forse».

### 3.2 Uscite spontanee «Chi corre con me?» — fase successiva

Esempio: «Domani 7:00, zona Firenze, 10 km, passo circa 5:30/km; cerco 2–5 persone».

Flusso:

1. Un runner registrato pubblica una proposta specificando giorno, ora, zona di ritrovo pubblica, distanza, passo e numero indicativo di partecipanti.
2. Altri runner la trovano tramite ricerca per città, giorno e passo.
3. Possono aderire o ritirarsi senza chat obbligatoria.
4. Il creatore può aggiornare o annullare l'uscita; gli aderenti vedono la modifica.
5. Sono disponibili segnalazione degli abusi e blocco di utenti molesti.

Non è necessario raccogliere o pubblicare indirizzi di casa né condividere la posizione GPS in tempo reale.

### 3.3 Convivenza con WhatsApp

**Non chiedere agli utenti di abbandonare WhatsApp.** È un canale di invito e comunicazione, non il database delle adesioni.

- Condivisione nativa tramite URL e Web Share API, con fallback a link copiabile.
- Anteprima Open Graph ben formattata: titolo, data, ritrovo pubblico e numero di gruppi.
- La pagina evento rimane accessibile via browser.
- Nell'MVP **nessuna dipendenza da API non ufficiali di WhatsApp**, scraping della chat, invio automatico di messaggi o gestione dei numeri telefonici.
- Email/push opt-in, se introdotte, sono separate dalla condivisione WhatsApp.

---

## 4. Perimetro del prodotto

### 4.1 MVP — Da realizzare per primo

**P0 — indispensabili**

1. **Autenticazione leggera:** registrazione/accesso via email con link monouso o altra soluzione passwordless equivalente, con rate limiting.
2. **Profilo runner minimo:** nome visibile, località facoltativa, passo indicativo facoltativo, preferenze di visibilità e comunicazione.
3. **Club:** creazione, descrizione, area geografica approssimativa, amministratori, iscrizione tramite invito o richiesta approvata.
4. **Eventi:** creazione, modifica, annullamento, data/ora, fuso, titolo, descrizione, luogo di ritrovo pubblico, visibilità (`public` / `club_only`).
5. **Gruppi dentro l'evento:** nome, distanza in metri o km, intervallo di passo espresso internamente in secondi/km, capogruppo opzionale, limite di partecipanti opzionale.
6. **RSVP:** adesione a un solo gruppo dello stesso evento, disdetta, cambio gruppo, risposta negativa opzionale ed elenco delle proprie adesioni.
7. **Ricorrenza settimanale semplice:** creazione ripetuta di eventi a partire da un template, con possibilità di modificare una singola occorrenza senza alterare le altre.
8. **Liste e contatori:** numero partecipanti per gruppo, conteggi corretti e possibilità di vedere i nomi nel rispetto della visibilità configurata.
9. **Link di condivisione:** apertura via browser mobile e azione di condivisione per WhatsApp.
10. **Dashboard organizzatore:** prossimi eventi, gruppi, partecipanti e gestione delle disdette/annullamenti.
11. **Interfaccia responsive e PWA di base:** l'installazione è facoltativa; pagina evento veloce su telefono.
12. **Sicurezza e requisiti minimi privacy:** controlli di accesso, protezione spam, reportistica tecnica, consenso comunicazioni, export e cancellazione account.

**P1 — utili, ma non bloccanti per il primo test**

- Notifiche email di cambi e cancellazioni, con preferenze utente.
- Capienza massima e lista d'attesa (solo se richiesta dai gruppi pilota).
- Promemoria prima dell'evento.
- Duplicazione rapida di un evento.
- Filtri semplici nel calendario del club.
- Storico delle presenze e percentuali di adesione in forma aggregata.
- Possibilità di designare più capigruppo.

### 4.2 V1 — Dopo validazione del MVP

- Proposte di corsa spontanee create da utenti.
- Scoperta per città/zona, fascia oraria, distanza e passo.
- Filtri e notifiche opt-in delle nuove uscite pertinenti.
- Segnalazione di eventi e utenti, blocco, moderazione e gestione dei reclami.
- Calendario locale con iniziative di più club, per eventi pubblici.
- Preferenze personali di ritmo non obbligatorie (nessun ranking o giudizio di valore).

### 4.3 Evoluzione commerciale — Solo dopo trazione reale

- Eventi sponsorizzati chiaramente etichettati.
- Test di scarpe organizzati con negozi sportivi.
- Partnership con manifestazioni e iscrizioni a gare, con tracciamento trasparente delle conversioni.
- Dashboard B2B per sponsor con metriche **aggregate** e prive di dati identificativi.
- Eventuale gestione pagamenti tramite provider esterno; mai memorizzare direttamente carte.
- Integrazioni Strava/altre piattaforme solo se utenti e casi d'uso ne giustificano la complessità e secondo API/termini ufficiali.

### 4.4 Fuori perimetro iniziale

Tracking GPS, Live Location, coaching AI, piani personalizzati, classifiche, wearable, feed social, chat in-app, video, ads programmatiche, marketplace completo, traduzioni multilingua complete, app iOS/Android native, pagamenti, gestione eventi competitivi con cronometraggio.

---

## 5. Regole di business e casi limite

1. **Una sola adesione positiva per utente e per evento.** Il runner non può occupare due gruppi contemporaneamente.
2. Il passaggio da gruppo A a B deve essere **atomico**; se B è pieno, l'utente resta in A e riceve un messaggio chiaro.
3. Se un gruppo ha capienza massima, impedirne lo sforamento anche con richieste concorrenti. Non affidare il controllo soltanto al frontend.
4. La disdetta libera immediatamente il posto. L'eventuale lista d'attesa viene implementata solo quando introdotta come funzione esplicita.
5. L'evento annullato non accetta nuove adesioni; le adesioni esistenti si conservano nello storico con stato appropriato.
6. Modificare una singola occorrenza di una ricorrenza non modifica automaticamente le altre occorrenze.
7. Gli orari devono essere salvati in UTC per le istanze e associati a un fuso IANA (inizialmente `Europe/Rome`); la generazione delle ricorrenze deve rispettare cambi ora legale/solare.
8. Gli intervalli di passo sono solo descrittivi, non validazioni delle prestazioni dell'utente.
9. Evitare duplicati di evento quando un job genera ricorrenze più volte: occorrenze univoche per template e data locale.
10. La visibilità degli eventi e delle liste partecipanti deve essere decisa esplicitamente: un evento pubblico non implica automaticamente che i nomi di tutti i partecipanti siano pubblici.
11. Un amministratore di club non può interrogare, aggiornare o esportare dati di un altro club.
12. Eventi pubblicati da utenti, quando introdotti, devono essere soggetti a rate limiting e sistemi di segnalazione.
13. Le informazioni di un evento cancellato o modificato devono rimanere comprensibili per chi aveva già aderito.
14. Registrazione, cancellazione, disiscrizione dalle notifiche e modifiche di visibilità devono essere effettivamente applicate anche lato server.

---

## 6. User stories e criteri di accettazione

### US-01 — Creare un evento con più gruppi (MVP)

**Come** amministratore di un club, **voglio** creare un evento con tre gruppi differenti **per** organizzare tutti nella stessa serata.

**Accettazione:**
- Il sistema permette almeno 1 gruppo e più gruppi nello stesso evento.
- Ogni gruppo può avere distanza, passo e capogruppo diversi.
- Nella pagina evento i gruppi sono distinguibili e selezionabili.
- Un amministratore di un altro club non può modificare l'evento.

### US-02 — Partecipare da un link WhatsApp (MVP)

**Come** runner invitato, **voglio** aprire un link, vedere percorso/ritmo e confermare **senza installare un'app**.

**Accettazione:**
- Il link apre la pagina evento su browser mobile.
- I dettagli di un evento pubblico sono visibili prima dell'autenticazione.
- Dopo l'autenticazione richiesta, il runner può selezionare il gruppo e confermare.
- Il riepilogo mostra il gruppo selezionato; la nuova adesione appare nei contatori.

### US-03 — Cambiare idea (MVP)

**Come** partecipante, **voglio** disdire o cambiare gruppo.

**Accettazione:**
- Una seconda richiesta non crea un secondo RSVP.
- Il cambio gruppo avviene senza adesioni duplicate.
- Con capienza raggiunta, il sistema non elimina la precedente adesione.
- Le liste e i contatori riflettono lo stato effettivo.

### US-04 — Ricorrenze (MVP)

**Come** amministratore, **voglio** programmare un'uscita ogni martedì.

**Accettazione:**
- Le occorrenze hanno data/ora corretta anche quando cambia l'ora legale.
- La rigenerazione non crea duplicati.
- L'annullamento di un martedì non annulla tutta la serie.

### US-05 — Proporre un'uscita spontanea (V1)

**Come** runner, **voglio** proporre una corsa in una zona pubblica e permettere ad altri di partecipare.

**Accettazione:**
- Posso indicare ora, distanza, passo, luogo pubblico e capienza opzionale.
- Solo gli utenti autorizzati vedono i dati non pubblici.
- Posso cancellare la proposta; gli aderenti vedono lo stato aggiornato.
- Gli utenti possono segnalare la proposta o bloccare il creatore.

---

## 7. Architettura tecnica proposta

Questa è un'**architettura preferita, non un vincolo immutabile**. Se il repository usa già tecnologie compatibili, valutare la continuità.

| Area | Proposta | Motivo |
|---|---|---|
| Frontend | React + Next.js + TypeScript | Interfaccia web mobile, condivisione e gestione pagine |
| UI | Componenti accessibili e stile coerente, mobile-first | Bassa frizione per partecipanti occasionali |
| API | ASP.NET Core (.NET LTS supportato) | Backend tipizzato, adatto a esperienza .NET |
| Persistenza | PostgreSQL + EF Core | Dati relazionali, vincoli e transazioni robuste |
| Geografia, quando utile | PostGIS | Ricerca geografica in V1, non necessaria per il primo MVP |
| Autenticazione | Passwordless via email, cookie/sessione sicuri | Ridurre frizione e gestione password |
| PWA | Web App Manifest + service worker prudente | Installazione opzionale; evitare dati evento obsoleti |
| Mappe, in V1 | MapLibre + cartografia autorizzata | Evitare dipendenza precoce da SDK proprietari |
| Infrastruttura | Deploy semplice, CI, backup e monitoraggio | Controllare costi e complessità |

**Struttura applicativa:** monolite modulare con separazione tra API, logica di dominio, persistenza, autenticazione e funzionalità di moderazione. Non adottare microservizi o code distribuite prematuramente.

**Accesso web/API:** scegliere un assetto di deployment che consenta cookie `HttpOnly`, `Secure`, corretta configurazione `SameSite`, protezione CSRF e CORS restrittivo. Non salvare token di accesso persistenti in `localStorage`.

**Prestazioni e affidabilità:** API idempotenti dove necessario; transazioni per RSVP; query paginate; indici su date, club ed eventi; health check; log strutturati senza dati sensibili; backup ripristinabili.

### 7.1 Modello dati concettuale

> Non è ancora uno schema SQL definitivo: usarlo come punto di partenza per le migrazioni e le invarianti.

- **User**: `Id`, `DisplayName`, `Email`, `City?`, `PreferredPaceSecPerKm?`, `CreatedAt`, `Status`.
- **Club**: `Id`, `Name`, `Slug`, `Description?`, `City?`, `Status`, `CreatedAt`.
- **ClubMembership**: `ClubId`, `UserId`, `Role`, `Status`, `JoinedAt` (univoco `ClubId + UserId`).
- **EventSeries**: `Id`, `ClubId`, `Weekday`, `LocalStartTime`, `TimeZoneId`, `StartDate`, `EndDate?`, `TemplateData`, `Enabled`.
- **RunEvent**: `Id`, `ClubId`, `SeriesId?`, `OccurrenceLocalDate?`, `Title`, `Description?`, `StartsAtUtc`, `TimeZoneId`, `MeetingPointLabel`, `MeetingPointApproxLocation?`, `Visibility`, `Status`, `CreatedByUserId`.
- **PaceGroup**: `Id`, `EventId`, `Name`, `DistanceMeters?`, `PaceMinSecPerKm?`, `PaceMaxSecPerKm?`, `LeaderUserId?`, `Capacity?`, `SortOrder`.
- **EventRsvp**: `Id`, `EventId`, `UserId`, `PaceGroupId?`, `Status`, `CreatedAt`, `UpdatedAt` (univoco `EventId + UserId`).
- **ClubInvitation**: `Id`, `ClubId`, `InvitedEmail?`, `TokenHash`, `ExpiresAt`, `UsedAt?`.
- **NotificationPreference**: `UserId`, `EventChangesEmail`, `RemindersEmail`, `DiscoveryEmails`, `UpdatedAt`.
- **AuditEntry**: `Id`, `ActorUserId?`, `Action`, `TargetType`, `TargetId`, `Timestamp` (non inserire token o payload sensibili).

Per **V1** aggiungere `CommunityRun` o una modellazione condivisa delle uscite spontanee, `UserBlock`, `Report` e moderazione. Evitare di forzare tali entità nel MVP prima di averne chiarito i workflow.

**Invarianti da applicare a DB/domain layer:** appartenenza gruppo ↔ evento; un RSVP per evento e utente; vincoli non negativi; `PaceMinSecPerKm <= PaceMaxSecPerKm`; capacità rispettata tramite transazioni; univocità delle occorrenze generate.

### 7.2 API HTTP indicative (MVP)

L'elenco è orientativo: convenzioni effettive e DTO da definire dopo il setup del repo.

| Metodo | Endpoint | Scopo |
|---|---|---|
| `GET` | `/api/clubs/{clubId}` | Dettaglio club con controllo visibilità |
| `POST` | `/api/clubs` | Crea club |
| `POST` | `/api/clubs/{clubId}/memberships` | Richiesta iscrizione o adesione tramite invito |
| `GET` | `/api/clubs/{clubId}/events` | Elenco eventi autorizzati |
| `POST` | `/api/clubs/{clubId}/events` | Crea evento e relativi gruppi |
| `GET` | `/api/events/{eventId}` | Dettaglio evento e gruppi secondo visibilità |
| `PATCH` | `/api/events/{eventId}` | Modifica autorizzata |
| `POST` | `/api/events/{eventId}/cancel` | Annullamento evento |
| `PUT` | `/api/events/{eventId}/rsvp` | Imposta/aggiorna adesione dell'utente corrente |
| `DELETE` | `/api/events/{eventId}/rsvp` | Annulla la propria adesione |
| `GET` | `/api/me/events` | Prossime partecipazioni personali |
| `POST` | `/api/clubs/{clubId}/series` | Programmazione settimanale |

**Nota:** l'API RSVP deve operare sull'utente autenticato; non accettare un `userId` arbitrario dal client. Mutazioni e accessi amministrativi richiedono controlli autorizzativi server-side. Definire paginazione e payload di errore standard.

### 7.3 Concorrenza e idempotenza (critiche)

Caso di prova: un gruppo ha **un solo posto libero** e due utenti provano a prenotarlo nello stesso momento.

- Il server deve evitare l'overbooking anche se entrambi vedono inizialmente un posto disponibile.
- Il controllo di capienza e la scrittura devono stare nella **stessa transazione**, con una strategia coerente di lock/serializzazione o aggiornamento condizionale.
- In caso di conflitto, una sola richiesta riesce; l'altra riceve un errore di dominio comprensibile (`409 Conflict` o equivalente) e i dati aggiornati.
- Ripetere la stessa richiesta di adesione non deve duplicare RSVP.
- Il cambio di gruppo non deve mai lasciare l'utente senza la precedente prenotazione se la nuova operazione fallisce.

Servono test di integrazione reali sul database per questi scenari, non soltanto test in-memory.

---

## 8. UX/UI: principi e schermate

### Principi

- Mobile-first, testi chiari, leggibilità e contrasto accessibile (target WCAG 2.2 AA dove applicabile).
- L'utente deve capire **quando, dove, quanti km, quale passo e chi è presente** prima di confermare.
- RSVP con pochi passaggi e feedback immediato.
- Per i runner l'installazione della PWA non deve mai essere obbligatoria.
- Evitare troppe notifiche, badge competitivi e meccanismi di pressione sociale.
- Schermate vuote utili: spiegano cosa fare se non ci sono eventi o uscite vicine.

### Schermate MVP

1. Landing con spiegazione e pulsante «Scopri un evento».
2. Pagina evento condivisibile, con schede per gruppi di passo.
3. Accesso leggero/passwordless.
4. «Le mie prossime corse».
5. Pagina del club e calendario uscite.
6. Form di creazione/modifica evento e gruppi.
7. Dashboard amministrativa degli RSVP.
8. Profilo, privacy e preferenze di comunicazione.

### Schermate V1

9. Esplora corse vicino a me (località inserita, senza geolocalizzazione obbligatoria).
10. Nuova uscita spontanea.
11. Dettaglio uscita spontanea e partecipazioni.
12. Segnala / blocca / gestisci segnalazioni.

---

## 9. Sicurezza, tutela delle persone e conformità

La piattaforma facilita incontri offline tra persone. La fiducia è parte del prodotto.

- **Minimizzazione dei dati:** raccogliere solo informazioni necessarie; località approssimative di default, nessuna posizione GPS live.
- **Privacy by default:** minimizzare la visibilità dei nomi e di informazioni personali fuori dai club; non esporre email, numeri o identità di partecipanti a utenti non autorizzati.
- **Autenticazione:** link di accesso monouso, token a scadenza breve e memorizzati in forma non reversibile, anti-enumerazione, anti-abuso e rate limiting.
- **Autorizzazioni:** verifiche server-side su ogni lettura/scrittura, prevenzione di IDOR e accessi cross-club.
- **Operazioni sensibili:** audit e protezioni contro CSRF, XSS, iniezioni e abuso degli endpoint.
- **Sicurezza degli incontri spontanei (V1):** luoghi pubblici, segnalazioni, blocco utenti, sospensioni e procedure di moderazione; avvisi pratici per chi incontra sconosciuti.
- **GDPR:** informativa privacy trasparente, identificazione del titolare, basi giuridiche appropriate, gestione diritti interessati, tempi di conservazione, cancellazione e portabilità/esportazione dati, processori/DPA ove necessari.
- **Comunicazioni commerciali:** consenso e preferenze separati da notifiche operative quando richiesto dalle norme applicabili; niente marketing nascosto.
- **Sponsorizzazioni (futuro):** messaggi pubblicitari chiaramente etichettati; evitare di vendere o comunicare dati individuali degli atleti agli sponsor.
- **Minori:** definire una policy esplicita prima dell'apertura pubblica; per il pilot valutare l'accesso riservato ai maggiorenni.
- **Qualità operativa:** backup periodici verificati, retention ragionevole, gestione incidenti, monitoraggio degli errori.

Questo documento non sostituisce una verifica legale specifica prima del lancio pubblico.

---

## 10. Business model: gratuita per i runner

### Ipotesi principale

**Distribuzione gratuita ai runner e, inizialmente, ai club**. Monetizzazione attraverso organizzazioni che ottengono un vantaggio commerciale dalle comunità di corsa.

| Fonte ricavo | Esempio | Quando testarla |
|---|---|---|
| Sponsorizzazioni locali | Negozio running sponsor dell'uscita del giovedì | Dopo la presenza di gruppi attivi |
| Eventi branded | Serata di test scarpe o prodotti | Dopo primi partner e organizzatori affidabili |
| Partnership con gare | Fee concordata per iscrizioni attribuite alla piattaforma | Quando esiste un pubblico locale sufficiente |
| Servizi per professionisti | Visibilità per coach/fisioterapisti selezionati | Dopo validazione domanda reale |
| Strumenti B2B | Club aziendali / welfare / iniziative sponsorizzate | Fase di espansione |

**Da evitare all'inizio:** banner e pubblicità programmatica. Senza grandi volumi rischiano di generare poco ricavo e peggiorare l'esperienza.

### Ipotesi economiche puramente illustrative

- 1.000 utenti attivi/mese × 15 impression × 2 € CPM = **30 €/mese lordi** ipotetici da pubblicità programmatica.
- 5 partner locali × 100 €/mese = **500 €/mese lordi** ipotetici da sponsorizzazioni.

Questi numeri **non sono previsioni, benchmark verificati o prezzi validati**. Non includono costi commerciali, operativi, fiscali e di acquisizione utenti. Vanno validati parlando direttamente con potenziali sponsor.

### Vincolo di prodotto

Il modello di ricavi non deve compromettere la fiducia degli utenti: etichette «Sponsorizzato», ranking trasparenti, opt-in marketing e nessuna vendita di dati personali.

---

## 11. Strategia di lancio e metriche

### Area pilota

Partire da **Prato / Firenze e dintorni**, con un gruppo di corsa che già si organizza via WhatsApp. È un'ipotesi di partenza operativa, **non** un limite geografico hardcoded nell'app.

### Piano di validazione (indicativo)

1. **Pilot 1:** un gruppo reale, gestione completa di un evento settimanale via link.
2. **Pilot 2:** coinvolgere altri 3–5 gruppi con ritmi e processi diversi.
3. **Pilot 3:** testare pubblicazione e adesioni alle uscite spontanee.
4. **Commerciale:** proporre a 2–3 attività locali una iniziativa sponsorizzata e misurare i risultati.

Le fasi sono condizioni di apprendimento, non scadenze rigide.

### KPI da definire e osservare

- Club attivi nell'ultima settimana.
- Eventi creati per settimana.
- Runner unici che hanno effettuato RSVP.
- **Conversione link evento → RSVP** (misurare solo con modalità privacy-responsabili).
- Percentuale di runner che partecipano/si iscrivono di nuovo entro 4 settimane.
- Disdette e cambi gruppo gestiti senza intervento manuale.
- Tempo impiegato dall'organizzatore per impostare l'uscita.
- In V1: proposte spontanee che ottengono almeno un aderente e uscite effettivamente svolte.
- In fase commerciale: sponsor paganti, ricavo ricorrente, partecipazioni attribuibili agli eventi sponsorizzati.

**North-star metrica proposta:** runner unici che partecipano ad almeno un'uscita organizzata tramite la piattaforma ogni mese. Distinguerla da semplici RSVP non effettivamente verificati.

---

## 12. Roadmap di sviluppo consigliata

### Milestone 0 — Fondamenta

- Monorepo o repository semplice e chiaro.
- Setup frontend, backend, database, migrazioni, test e CI.
- Design system minimo e ambiente di sviluppo via Docker Compose, se utile.
- Health check, logging e gestione errori standard.

**Done:** repository avviabile e testabile localmente con istruzioni riproducibili.

### Milestone 1 — Identità e club

- Accesso passwordless sicuro.
- Profilo utente essenziale.
- Creazione club, iscrizioni/inviti, gestione ruolo amministratore.
- Test autorizzazioni, inclusi tentativi cross-club.

**Done:** un amministratore può creare un club, invitare un membro e gestire l'accesso.

### Milestone 2 — Eventi e RSVP

- Evento e gruppi di passo.
- Pagina pubblica condivisibile.
- RSVP, cambio gruppo, disdetta, contatori.
- Test di concorrenza e idempotenza su PostgreSQL.

**Done:** un gruppo WhatsApp può utilizzare il sistema per una serata reale con più ritmi.

### Milestone 3 — Ricorrenze e dashboard

- Template settimanale e generazione idempotente.
- Eccezioni delle singole date.
- Dashboard organizzatore.
- PWA installabile facoltativamente e rifinitura mobile.

**Done:** un club usa il sistema per diverse settimane senza dover ricreare manualmente tutto.

### Milestone 4 — Pilot e miglioramenti

- Test con gruppi reali.
- Analisi dei problemi riscontrati dagli organizzatori.
- Miglioramenti UX, qualità, accessibilità, privacy e performance.
- Eventuali notifiche mirate.

**Done:** l'utilizzo ricorrente è dimostrabile, non soltanto dichiarato.

### Milestone 5 — Scoperta e uscite spontanee

- Creazione proposte, ricerca locale, adesioni.
- Misure anti-abuso, blocco, segnalazione e moderazione.
- Verifica della domanda effettiva.

**Done:** una persona può trovare un'uscita compatibile anche senza conoscere un club.

### Milestone 6 — Monetizzazione sperimentale

- Eventi sponsorizzati ben distinti.
- Pannello limitato per partner o gestione manuale iniziale.
- Metriche aggregate, privacy-responsabili e attribuzione delle iniziative.

**Done:** almeno un test commerciale misurabile con partner reale, senza penalizzare il runner.

---

## 13. Test minimi e qualità attesa

Prima di considerare completo il MVP, verificare almeno:

- Un utente non membro non accede a contenuti `club_only`.
- Un amministratore del club A non modifica eventi del club B.
- Un runner non può aderire a due gruppi dello stesso evento.
- Due prenotazioni contemporanee sull'ultimo posto non causano overbooking.
- Il cambio a un gruppo pieno non cancella l'iscrizione esistente.
- Retry di RSVP e ricorrenze non creano duplicati.
- La disdetta aggiorna immediatamente il numero di posti disponibili.
- Un evento annullato rifiuta nuove adesioni.
- La singola occorrenza modificata non cambia l'intera serie.
- Gli eventi ricorrenti conservano l'ora locale desiderata nel passaggio ora legale/solare.
- I link degli eventi pubblici funzionano da mobile e senza installazione.
- Gli eventi privati non diventano visibili per la sola conoscenza di un ID prevedibile.
- Accesso email monouso: token scaduti, riutilizzati e richieste eccessive sono rifiutati.
- L'interfaccia gestisce caricamento, errori di rete, mancanza di permessi, capienza e dati vuoti.

Test unitari per regole di dominio; test di integrazione con DB reale per autorizzazioni, vincoli e transazioni; smoke/end-to-end sui flussi principali.

---

## 14. Decisioni ancora aperte (da validare, non da inventare)

1. Nome definitivo, dominio e identità del prodotto.
2. Gestione iscrizione club: completamente aperta, su richiesta, o solo invito (supportare almeno i casi base senza complicare MVP).
3. Quanto mostrare pubblicamente dei partecipanti (default prudente, configurabile).
4. Provider email/hosting e budget mensile operativo.
5. Accesso passwordless puro o provider OAuth opzionale.
6. Capienza e waitlist: decidere sulla base dei bisogni dei club pilota.
7. Modello organizzativo e responsabilità del soggetto che gestirà la piattaforma.
8. Regole per l'utilizzo da parte dei minori e requisiti legali prima del rilascio pubblico.
9. Verifica di marchio/nome e disponibilità dei domini.
10. Disponibilità di sponsor locali e disponibilità effettiva a pagare.

**Indicazione per Claude:** non bloccare il prototipo per questioni reversibili; esplicitare assunzioni e preparare una soluzione facile da modificare.

---

## 15. Primo incarico consigliato per Claude Code

Quando si apre un nuovo repository, usare questo prompt operativo:

> Leggi `CLAUDE.md` come specifica iniziale. Voglio realizzare **solo il primo MVP di gestione dei running club**, senza funzionalità social o monetizzazione. Per prima cosa:
>
> 1. Analizza lo stato del repository e proponi una struttura semplice per frontend Next.js/TypeScript, backend ASP.NET Core e PostgreSQL.
> 2. Identifica le decisioni tecniche essenziali e definisci un piano di implementazione per le milestone 0–3, ognuna verificabile.
> 3. Proponi uno schema dati iniziale con vincoli, gestione dei ruoli e strategie anti-overbooking.
> 4. Descrivi le pagine e gli endpoint necessari al flusso «link WhatsApp → scelta gruppo → RSVP».
> 5. Dopo il piano, implementa **soltanto la milestone 0** (setup eseguibile, migrazioni di base se appropriate, CI/test minimi e README) e mostra come avviarla.
>
> Mantieni l'esperienza mobile-first, evita componenti non richiesti e non aggiungere funzionalità V1 prima di validare il MVP.

---

## 16. Sintesi esecutiva

Il prodotto parte da un'esigenza semplice: **sostituire il caos dei sondaggi WhatsApp con un sistema leggero per corse settimanali a più gruppi di passo**. WhatsApp rimane il canale di conversazione; il sito diventa il riferimento per orari, percorsi, ritmo e partecipazioni.

La seconda fase permette a chiunque di trovare persone con cui correre nella propria zona. Questa rete locale crea la possibilità, da validare, di un business sostenuto da partnership con negozi, eventi e sponsor, **mantenendo la partecipazione gratuita per i runner**.

**Priorità assoluta:** ottenere utilizzo ricorrente da parte di club reali. Il codice deve servire questa verifica, non sostituirla.

# 0006 — Annuncio e match (milestone 5a)

Data: 2026-10-29 · Stato: accettata · Design: `docs/superpowers/specs/2026-10-29-uscite-spontanee-e-match-design.md` · Piano: `docs/superpowers/plans/2026-10-29-milestone-5a-annuncio-e-match.md`

## Decisioni
- **Posizione = luogo pubblico.** Zona dell'annuncio e (in 5b) ritrovo si scelgono da un elenco curato (`PublicPlace`, seed nella migrazione: Prato e Firenze). Niente GPS, indirizzi liberi né PostGIS; la distanza è in linea d'aria (Haversine) tra luoghi, con prefiltro per riquadro geografico. L'API `GET /api/places` non restituisce coordinate.
- **Annuncio unico per utente** (`RunIntent`): passo, distanza, tipo di corsa, disponibilità (7 giorni × 3 fasce in una maschera di bit), raggio 5–30 km, «anche in coppia», scadenza (default 30 giorni, massimo 90), pausa/riattiva/elimina. Vincoli anche nel DB.
- **Solo maggiorenni:** la prima pubblicazione richiede `confirmAdult`; si salva `AdultConfirmedAt` e non si chiede più alle modifiche.
- **Punteggio** (`MatchScoring`, funzione pura, simmetrica): passo 35, distanza 20, orari 25, vicinanza 15, tipo 5; soglia minima 50. **Filtri obbligatori:** distanza entro il **raggio minore** dei due, almeno una fascia oraria in comune, divario di passo ≤ 60 s/km. Gli intervalli a un solo valore sono allargati a una larghezza minima. Ogni match porta motivazioni in italiano.
- **Visibilità solo reciproca:** per vedere i match serve un annuncio proprio attivo e non scaduto (409 altrimenti). Compaiono solo annunci attivi, non scaduti, di account attivi.
- **Privacy della risposta:** la scheda contiene nome visibile, città (non il luogo), distanza arrotondata al km, intervalli di passo/distanza, tipo e fasce in comune; mai coordinate né email. Verificato da un test.
- **Blocco reciproco e invisibile** già in 5a (serviva ai criteri di test): `POST/DELETE /api/users/{id}/block`; dopo il blocco nessuno dei due compare nell'elenco dell'altro e l'altro non riceve avvisi.
- **Nome visibile scelto:** il nome predefinito è la parte locale dell'email, che mostrata agli altri rivelerebbe un dato personale. Pubblicare l'annuncio è quindi rifiutato (400) finché il nome del profilo coincide con essa; il frontend lo segnala con un avviso e il rinvio al profilo.
- **Dati personali:** l'annuncio è incluso nell'export; la cancellazione dell'account elimina annuncio e blocchi a cascata.
- **Test:** i test dei match usano ciascuno una fascia oraria propria, così i candidati di un test non compaiono negli altri. La fixture svuota gli annunci all'avvio **solo se il database ha «test» nel nome**; i test girano su `runtivo_test`, non sul database di sviluppo.

## Limiti noti
- Coordinate dei luoghi **approssimate**: vanno verificate e l'elenco ampliato prima del lancio.
- Ancora niente «Mi interessa», uscita spontanea, conferma né condivisione (5b); niente segnalazioni, ruolo di amministratore della piattaforma e limiti ai nuovi account (5c).
- Senza email i match si vedono solo aprendo l'app.
- Valutazione in memoria su al massimo 500 candidati per richiesta: adeguato al pilot, da rivedere con volumi maggiori.
- Un nome scelto può comunque essere il nome e cognome reali: l'app non lo impedisce (è una scelta della persona), ma il testo del modulo suggerisce di mostrare solo ciò che si vuole.

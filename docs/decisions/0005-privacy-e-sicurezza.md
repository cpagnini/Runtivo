# 0005 — Privacy, sicurezza e rifiniture (milestone 4, prima parte)

Data: 2026-10-28 · Stato: accettata

Questa parte della milestone 4 raccoglie ciò che si poteva fare prima del pilot con club reali e senza scegliere il provider email. Restano fuori l'invio delle email e le modifiche che nascono dall'uso reale.

## Diritti dell'interessato (P0 del PRD)
- **Preferenze di comunicazione:** `GET/PUT /api/me/notifications`. Default prudente: solo i cambi alle uscite a cui si partecipa; promemoria e novità sono disattivati. Le preferenze sono salvate e potranno essere rispettate dall'invio quando esisterà.
- **Esportazione:** `GET /api/me/export` restituisce in JSON profilo, iscrizioni ai club, RSVP, preferenze e registro delle azioni dell'utente. Solo i dati della persona loggata.
- **Cancellazione:** `DELETE /api/me` con l'email come conferma. Elimina profilo, iscrizioni, RSVP, preferenze, inviti creati o ricevuti e token di accesso; il registro di audit resta ma perde il collegamento alla persona; i capigruppo assegnati vengono svuotati. Chi è l'**unico admin di un club con altri membri** riceve 409 e deve prima passare il ruolo; se il club ha solo lui, viene eliminato insieme a lui. Le uscite create dall'utente in un club che continua a esistere restano (contenuto del club); il loro campo `CreatedByUserId` resta un identificativo senza più persona collegata.
- **Passo preferito:** ora si può cancellare (`clearPreferredPace`).

## Sessione
La sessione vale solo finché l'account esiste ed è attivo (controllo a ogni richiesta autenticata). Cancellazione e sospensione hanno effetto subito, non al login successivo. Costo: una query indicizzata per richiesta, accettabile per la scala del pilot.

## Limiti e header
- **IP reale dietro il proxy:** con `App:TrustForwardedHeaders=true` il backend legge `X-Forwarded-For`. Senza, i limiti per IP vedrebbero tutti gli utenti come l'indirizzo del frontend. Va attivato **solo** se il backend non è raggiungibile direttamente da Internet (altrimenti l'header si può falsificare). In sviluppo è attivo.
- **Limite generale** di richieste per IP su tutte le API (`App:ApiRequestsPerMinute`, default 300) in aggiunta a quello più stretto sugli endpoint di accesso.
- **Header di sicurezza** su API e frontend: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`. **Content-Security-Policy non ancora adottata**: con Next.js richiede un nonce per gli script inline ed è da fare con una verifica dedicata.

## Correzione
La generazione delle serie poteva andare in **deadlock** di PostgreSQL con richieste concorrenti (500 intermittente, visto nei test): più transazioni inserivano le stesse date in ordine diverso. Ora la generazione è serializzata per serie con un lock di transazione (`pg_advisory_xact_lock`); l'indice univoco resta come garanzia finale.

## Accessibilità
Link «Vai al contenuto», rispetto di `prefers-reduced-motion`, aree di tocco di almeno 44 px per pulsanti e campi, checkbox grandi. Una verifica completa con un lettore di schermo e con gli strumenti automatici (target WCAG 2.2 AA) resta da fare prima di aprire al pubblico.

## Ancora da fare in milestone 4
- Invio email (provider, DPA, modelli) e rispetto delle preferenze.
- Verifica con club reali: tempi dell'organizzatore, problemi di comprensione, conversione link → RSVP (misurata senza dati personali).
- CSP, audit di accessibilità, test end-to-end del browser, backup e monitoraggio in produzione.
- Informativa privacy e policy sui minori (non sostituiscono una verifica legale).

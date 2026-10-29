# 0010 — Ricerca del punto di partenza, frase libera, testi neutri, nuova grafica

Data: 2026-10-29 · Stato: accettata · Estende la scelta «solo luoghi curati» della milestone 5a

## Ricerca del punto di partenza (Photon / OpenStreetMap)
- **Cosa:** nel form dell'annuncio si scrive il nome di un parco, una piazza, un quartiere o una città e si sceglie tra i suggerimenti. I luoghi dell'elenco curato restano come scorciatoie.
- **Perché Photon e non Google:** gratuito e senza chiave; Google richiede fatturazione e vieta di conservare i risultati. L'istanza pubblica (`photon.komoot.io`) è a uso equo: per un lancio vero servirà un'istanza propria o un piano a pagamento (`Places:Photon:BaseUrl`).
- **Privacy:**
  - Si accettano solo luoghi pubblici (parchi, impianti sportivi, stazioni, attrazioni) e località; **mai indirizzi civici né strade**.
  - La posizione si arrotonda a una griglia di circa 500 m prima di salvarla; l'API non restituisce mai le coordinate.
  - Il testo digitato (almeno 3 lettere) viene inviato a Photon: va citato nell'informativa privacy. Non si registra nei log.
  - Il luogo scelto non è inventabile dal client: la ricerca restituisce un **riferimento firmato** (Data Protection, valido 30 minuti) e `POST /api/places/resolve` lo scambia con un luogo.
- **Luoghi creati dalle ricerche** (`PublicPlace.IsCurated = false`) valgono **solo come punto di partenza** di un annuncio: non compaiono nell'elenco dei punti di ritrovo e non si possono usare come luogo di un'uscita (`/api/runs`, suggerimenti di incontro).
- Tetto di 40 ricerche al minuto per persona; se Photon non risponde, la ricerca restituisce un elenco vuoto.
- Photon rifiuta `lang=it` con 400: non si passa nessuna lingua.
- Paesi ammessi configurabili (`Places:Photon:CountryCodes`, predefinito `IT`); la zona di Prato/Firenze è solo una **preferenza** dei risultati, non un limite.

## Frase libera «Cosa cerchi»
- Facoltativa, massimo 200 caratteri, mostrata alle persone compatibili.
- Si rifiutano email, indirizzi web, numeri di telefono e nomi di altre app (WhatsApp, Instagram…): lo scambio dei contatti avviene dopo la conoscenza. Le segnalazioni restano lo strumento per gli abusi.
- Non entra nel calcolo del punteggio.

## Testi inclusivi
Si usano due strumenti, nell'ordine:
1. **Formule neutre**, quando esistono: «quando puoi correre», «adesioni», «chi amministra il club», «Hai creato l'account da poco», «questa persona». Niente aggettivi o participi riferiti alla persona («libero», «disposto», «iscritto»).
2. **Schwa (ə)** dove la formula neutra non c'è o appesantirebbe il testo: «unə amministratorə», «tuttə», «lə altrə», «lə membrə», «lə capogruppo», «Membrə» (etichetta del ruolo). Il plurale si scrive con la stessa ə (non con ɜ).
Eccezioni: `Capogruppo` e `Admin` (etichette dei ruoli) restano invariate perché non hanno genere. I valori tecnici (`Member`, `ClubOnly`) non cambiano.
**Da sapere:** alcuni lettori di schermo leggono la ə in modo imperfetto. Dove il testo è importante per chi usa un lettore di schermo si preferisce la formula neutra. Da seguire nei testi futuri.

## Grafica
Palette indaco→verde acqua con accento corallo, intestazione fissa con sfocatura, schede con ombre morbide, pulsanti con sfumatura, home con pannello d'apertura e due percorsi (compagnia / club), schede dei match con iniziale, indicatore di compatibilità e frase libera. Rispetta tema scuro e «riduci animazioni».

## Dopo l'accesso
La destinazione predefinita è la home (prima i club), così chi cerca compagnia non si trova in una schermata che sembra imporre un club.

## Non verificato
Photon pubblico provato a mano con ricerche vere; i test automatici usano un servizio simulato. Contrasto dei colori e uso con lettore di schermo non verificati formalmente.

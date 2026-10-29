# Attivare l'accesso con Google

Il pulsante «Continua con Google» compare solo se l'API ha le credenziali di un'app registrata nella console Google. Senza, resta solo il link via email. Servono circa 10 minuti e un account Google. **I nomi dei menu di Google cambiano ogni tanto: se un passo non corrisponde, cerca la voce simile.**

## 1. Crea il progetto e la schermata di consenso
1. Vai su <https://console.cloud.google.com> e crea un progetto, ad esempio «Runtivo».
2. Apri **API e servizi → Schermata consenso OAuth** (oggi anche «Google Auth Platform»).
3. Tipo di utenza: **Esterno**. Nome dell'app: *Runtivo*. Email di assistenza: la tua.
4. Ambiti (scope): aggiungi solo `openid` e `email` (non sensibili).
5. Finché l'app è in stato **Test**, possono accedere **solo gli indirizzi aggiunti come «Utenti di test»**: inserisci le tue email Gmail e quelle di chi proverà l'app.

## 2. Crea le credenziali
1. **API e servizi → Credenziali → Crea credenziali → ID client OAuth**.
2. Tipo di applicazione: **Applicazione web**.
3. **URI di reindirizzamento autorizzati**, esattamente (senza barra finale):
   `http://localhost:3000/api/auth/google/callback`
4. Crea e copia **ID client** e **Segreto client**.

## 3. Dai le credenziali all'API (senza metterle nel codice)
Dalla cartella del progetto, con i valori copiati al posto dei puntini:

```bash
dotnet user-secrets set "Auth:Google:ClientId" "...apps.googleusercontent.com" --project backend/src/Runtivo.Api
dotnet user-secrets set "Auth:Google:ClientSecret" "..." --project backend/src/Runtivo.Api
```

I segreti restano sul tuo PC (fuori dal repository). Riavvia l'API: sulla pagina di accesso compare «Continua con Google».

## Per la produzione (più avanti)
- Aggiungi come URI di reindirizzamento `https://<tuo-dominio>/api/auth/google/callback` e imposta `App:PublicBaseUrl` con lo stesso dominio.
- Fornisci le credenziali come variabili d'ambiente `Auth__Google__ClientId` e `Auth__Google__ClientSecret` del servizio che ospita l'API.
- Nella schermata di consenso servono l'indirizzo della home e quello dell'**informativa privacy**; con ambiti non sensibili (`openid`, `email`) si può pubblicare l'app senza la verifica approfondita di Google.
- Aggiorna l'informativa privacy: Google è fornitore dell'accesso.

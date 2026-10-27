# 0001 — Stack e architettura iniziale

Data: 2026-10-27 · Stato: accettata

## Decisione
- **Nome prodotto:** Runtivo (sostituisce "RunTogether"). Marchio e dominio da verificare.
- **Monolite modulare:** frontend Next.js/TypeScript + API ASP.NET Core (.NET 10 LTS) + PostgreSQL con EF Core. Nessun microservizio.
- **Un solo origin per il browser:** Next.js inoltra `/api/*` al backend (`rewrites` in `frontend/next.config.ts`, variabile `API_URL`). Cookie `HttpOnly` same-origin, niente CORS né `SameSite=None`.
- **Health:** `/health` (liveness, senza DB) e `/health/ready` (readiness, con DB).
- **Test su DB reale:** i test di integrazione leggono `RUNTIVO_TEST_DB`; se assente vengono ignorati, in CI girano contro un servizio PostgreSQL.
- **Migrazioni:** nessuna in milestone 0, il modello è vuoto. La prima migrazione arriva con la milestone 1 (utenti, club).
- **Database locale:** `docker-compose.yml` fornito; in alternativa un PostgreSQL 17 installato su Windows con la stessa connection string (`appsettings.Development.json`).

## Alternative scartate
- Next.js full-stack con ORM TypeScript: un solo deploy, ma si perde la tipizzazione del dominio e l'esperienza .NET indicate nel PRD.
- Database in cloud (Neon/Supabase) per i test: dipendenza di rete e di account per ogni sviluppatore e per la CI.

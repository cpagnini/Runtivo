# runtivo.

**Trova il tuo passo. Corri insieme.**

Gestione delle uscite di corsa per running club, con più gruppi di passo per serata. Specifica completa in [CLAUDE.md](CLAUDE.md), piano in [docs/piano-milestone.md](docs/piano-milestone.md).

## Requisiti
.NET SDK 10, Node 24, PostgreSQL 17 (via Docker oppure installato).

## Avvio locale
```bash
docker compose up -d db                       # oppure un PostgreSQL locale con la stessa connection string
dotnet run --project backend/src/Runtivo.Api  # API su http://localhost:5044
cd frontend && npm install && npm run dev     # web su http://localhost:3000
```
In sviluppo l'API applica le migrazioni all'avvio e scrive nel log i link di accesso (nessuna email reale).

Verifica: `http://localhost:5044/health` e `/health/ready`; la home su `http://localhost:3000`.

## Test
```bash
dotnet test backend/Runtivo.sln
# con database reale (usa un database dedicato ai test, con "test" nel nome: all'avvio i test svuotano gli annunci):
RUNTIVO_TEST_DB="Host=localhost;Port=5432;Database=runtivo_test;Username=runtivo;Password=runtivo_dev" dotnet test backend/Runtivo.sln
cd frontend && npm run lint && npm run build
```

## Struttura
- `backend/` API ASP.NET Core e test
- `frontend/` Next.js (inoltra `/api/*` al backend, vedi `frontend/next.config.ts`)
- `docs/decisions/` decision log

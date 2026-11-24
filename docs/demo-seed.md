# Demo seed (dev only)

Data for the client presentation, made **through the API only** (`src/backend/Tools/Ash.DemoSeed`). The project is
not referenced by `Ash.Api`, not published and not in any Docker image.

## Run

```
docker compose down -v
docker compose up -d
dotnet run --project src/backend/Database/Ash.Database -- --local
dotnet run --project src/backend/Tools/Ash.DemoSeed
```

with `ASPNETCORE_ENVIRONMENT=Development` (PowerShell: `$env:ASPNETCORE_ENVIRONMENT = "Development"`). Stop the API
first or not: the seed hosts its own copy of the API in-process, with the API's user secrets (localhost MySQL, MinIO).

It refuses to run:
- outside Development;
- when `ConnectionStrings:App` does not point to localhost;
- when `ash_core.cases` is not empty ("run docker compose down -v + migrator first").

## How it works

- The real API in-process (`WebApplicationFactory<Program>`, as the integration tests): every case, party, hearing,
  document, decision, archive, delegation, chat message and notification goes through the same endpoints, handlers and
  units of work as the UI — history events, the audit chain, case codes, office routing and notifications are what
  real usage produces. Users act through the dev header (`X-Dev-User`).
- Dates: the application's `IClock` is replaced by a controllable clock that walks through Aug–Nov 2026. MySQL column
  defaults (`CURRENT_TIMESTAMP`, `ON UPDATE CURRENT_TIMESTAMP`) follow it: the unit of work's connection runs
  `SET TIMESTAMP` when it opens. At the end the clock is the real "now" (this week's and next week's hearings, chats).
- Background jobs, the scan loop and e-mail are off; uploads are scanned by the scan job itself (NoOp in dev → "Gati").
- No statistics year is frozen. Every notification of the seed is marked read (every bell at 0).

## What it makes

- Zyra Vendore Durrës (DR) with Gjykata e Shkallës së Parë Durrës; kryetar.dr (Kryetar), avokat.dr (Avokat Shteti),
  sekondar.dr (Avokat sekondar). TR handles the Tirana first-instance and appeal courts. kryetar@ gets STATE_LAWYER.
- Institutions: Ministria e Financave dhe Ekonomisë (seeded), Ministria e Brendshme, Agjencia Shtetërore e Kadastrës,
  Bashkia Tiranë.
- 14 registrations (10 TR, 4 DR) + one continuation (X/2 at Gjykata e Apelit Tiranë): 3 closed (Fituar / Humbur /
  Pjesërisht fituar; Ankimuar and Formë e prerë), 1 archived, 1 delegated, 2 without a lawyer, assistants on 2 cases,
  a jurist on 1, a past hearing left not updated ("Seanca kaloi"), PDFs and one .docx, two chats.
- Prepared for the live demo: **E** (Tiranë, the ministry monitoring, avokat1, not delegated), **F** (Tiranë, avokat1,
  decision PDF uploaded, no decision recorded), **D** (Durrës, avokat.dr + sekondar.dr). The run ends with a table:
  label → case code → office → primary lawyer.

Tested by `DemoSeedTests` (counts, history on every case, bells at 0, audit chain verifies; the refusals).

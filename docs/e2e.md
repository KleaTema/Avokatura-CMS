# End-to-end tests (Playwright)

Browser tests of whole flows against the **local** API and dev database: `src/frontend/e2e/*.spec.ts`.
They are separate from the other checks: `npm run build`, `npm run lint` and `npm test` (Vitest) never run them.

| Spec | What it proves |
|---|---|
| `assign.spec.ts` | The Kryetar assigns from *Pa avokat* in exactly: click *Cakto avokatin* → pick the lawyer (open the list, choose) → confirm; the case leaves the tab and is ACTIVE. |
| `conflict.spec.ts` | Two users edit the claim at once: the second save shows the shared *ConflictAlert*; *Rifresko* reloads the case and keeps the typed value; saving again works. |
| `close-case.spec.ts` | avokat1 records the decision in the stepper (result, a value, *Formë e prerë*), uploads the reasoned decision PDF inside the dialog and waits until it is selected (*Gati*), cancels the scheduled hearing with *Anuloji dhe mbyll*, then archives from "Arkivo çështjen?" — the banner and "E mbyllur më …" show, the edit button is gone, and the page address never changed. |
| `notifications.spec.ts` | Task 040 acceptance: avokat1's page is open (all read first); in another browser context the Kryetar assigns a new case to avokat1 — within 2 seconds and without a reload avokat1's bell reads "Njoftimet, 1 të palexuara", the tab title "(1) ASH", the live toast shows. The popover lists it (unread, with the case code); a click opens `/cases/{id}/te-dhenat` and the count is gone; the *Njoftimet* page lists it and *Të palexuara* is empty. With `E2E_SHOTS=<folder>` it saves screenshots of the popover and the page at 1536 and 390 px. |
| `paging.spec.ts` | As monitor, the case list searched for "E2E" shows 10 rows and "1–10 nga N"; *Pas ›* → page 2 in the URL ("11–… nga N"); a case opened from it, two of its tabs, then "Kthehu te lista e çështjeve" → the same page 2 with the same search and first row; *‹ Para* → page 1 (and disabled there). Creates E2E cases first when fewer than 11 exist. |
| `permissions.spec.ts` | For protokoll, avokat1 (primary), avokat2 (secondary), kryetar and monitor, on one REGISTERED and one ACTIVE case: every action button is visible exactly when its code is in `permissions[]` of `GET /api/cases/{id}` for that user (a user who gets 404 sees "Çështja nuk u gjet ose nuk keni akses."). |

## Prerequisites

1. **Docker** with the local MySQL running (`docker compose up -d` in the repo root).
2. **The database migrated with the local seed**: `dotnet run --project src/backend/Database/Ash.Database -- --local`
   (the `--local` flag adds `LocalScripts`: the dev users of `901`, the courts and judges of `903`).
3. **The API** on http://localhost:5004 from the current working tree (`dotnet run` in `src/backend/Host/Ash.Api`,
   Development environment — the tests log in with the dev header / dev login page).
4. **The frontend dev server** on http://localhost:5173: `npm run dev` in `src/frontend`.
5. Once: `npm install` in `src/frontend` (installs `@playwright/test`). If Chromium is missing:
   `npx playwright install chromium`.

Other addresses: `E2E_API_URL` (default `http://localhost:5004`) and `E2E_BASE_URL` (default `http://localhost:5173`).
If either server is not reachable the run stops at once and says which one to start.

## Run

```
cd src/frontend
npm run e2e
```

One worker (the tests share the dev database). On a failure the screenshot and a trace are in
`src/frontend/e2e/test-results/` (git-ignored): `npx playwright show-trace <trace.zip>`.

## Test data

- **Created, never assumed.** Each test creates its own case through the API in a setup step
  (`e2e/support/api.ts`): Protokoll registers it (`POST /api/cases/register`) with a plaintiff and the ministry as
  defendant; tests that need an ACTIVE case let the Kryetar assign avokat1 (primary) and avokat2 (secondary). Nothing
  depends on cases already in the database, and no SQL is used.
- **Recognisable.** The summons number and act number start with `E2E-`, the object with `E2E test`, the plaintiff
  is "E2E Paditës …".
- **Cleaning up.** Cases are legal records: the API has no delete, so the e2e cases stay in the dev database. They do
  not disturb the tests (each test finds its own case by its unique act number). To tidy the dev database, recreate
  it (drop the Docker volume and migrate again) — never delete rows of a shared or production database.

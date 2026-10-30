---
name: ash-frontend-feature
description: Pattern for building a React page or component in the ASH case management frontend — generated API client, TanStack Query, permissions, Albanian UI text, dates, 409 conflicts, forms. Use when implementing any UI task (src/frontend, tasks named ui-*).
---

# Building a frontend feature (ASH)

## Structure
```
src/frontend/src/pages/<area>/<Page>.tsx        route-level page
src/frontend/src/pages/<area>/tabs/*.tsx        tabs of a page
src/frontend/src/components/*.tsx               reusable pieces
src/frontend/src/features/<area>/api.ts         query/mutation hooks for that area
src/frontend/src/api/                           GENERATED — never edit by hand (`npm run gen:api`)
```

## Data access
- Only through the generated typed client + TanStack Query hooks in `features/<area>/api.ts`:
```ts
export const useCase = (id: string) =>
  useQuery({ queryKey: ['case', id], queryFn: () => api.GET('/api/cases/{id}', { params: { path: { id } } }).then(unwrap) });
```
- After a mutation, invalidate the exact keys it affects (`['case', id]`, `['case-history', id]`), do not refetch everything.
- Lists use the keyset cursor: `useInfiniteQuery` with `getNextPageParam: p => p.nextCursor ?? undefined`.

## Permissions
- Show an action only if its code is in `case.permissions` (case page) or `me.permissions` (global). Never decide by role name.
- The API is still the real check; hiding is for usability.

## Text, numbers, dates
- All visible text in Albanian. Keep strings in `src/frontend/src/i18n/sq.ts` (one object), not scattered.
- Dates: the API sends UTC ISO strings → show with `formatDateTime()` from `lib/dates.ts` (Europe/Tirane, `dd.MM.yyyy HH:mm`).
- Money: `Intl.NumberFormat('sq-AL', { style: 'currency', currency })`; m² as a separate field.

## Forms
- react-hook-form + zod; map server ProblemDetails field errors onto the form fields.
- Send back `rowVersion` from the loaded data on every update.
- 409 → show "Të dhënat u ndryshuan nga një përdorues tjetër." with a "Rifresko" button that refetches and keeps the user's unsaved input visible for copying.
- 404 on a case → "Çështja nuk u gjet ose nuk keni akses." (never reveal which).

## Search and filtering (always consider)
Every list, table and picker gets search unless it will never have more than ~10 rows.
- Up to ~200 rows loaded at once (offices, courts, lookup lists): client-side filter, no backend change.
- More rows or growing data (users, judges, institutions, cases, documents, chat, audit): server-side `?q=` with keyset paging.
- Match name, code and other short identifiers the user would type (short name, email, NIPT, case number).
- Accent- and case-insensitive: "shendetesise" finds "Shëndetësisë". Client-side: normalize with
  `s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()`; server-side relies on the ai collation.
- Server search: debounce 300 ms, min 2 characters, keep `q` in the URL (`?q=`) so refresh/back keep the search.
- Pickers with more than ~10 options: MUI Autocomplete with typing, not a plain Select.
- Empty result: "Nuk u gjet asnjë rezultat për „{q}“." with a button to clear the search.
- Search box placeholder lists what can be searched, e.g. "Kërko (emër, kod)".
## Dates and times
Always use components/AppDatePicker and AppTimePicker (DD.MM.YYYY, 24h, 5-minute steps). Click opens the picker; Tab focus does not. Never a plain text field for dates or times.
Not needed: lists that are always tiny (file types, code formats).
## Visual design system (always)
- Use only the MUI theme and `src/theme/tokens.ts`. No hex colors, font sizes, shadows or radii in pages or components.
- Every page starts with `PageHeader` (title + optional subtitle + actions). Content lives in `SectionCard`s on the gray page background.
- Buttons: at most ONE gold button (`variant="contained" color="secondary"`) per page, for the page's main action. Other actions: blue contained/outlined or text buttons. Destructive actions: `color="error"` with a confirmation dialog.
- Statuses, priorities and deadlines are always shown with `StatusChip`; never invent new chip colors in a page.
- Tables use `DataTable` (header band, hover, skeleton while loading, `EmptyState` when empty). Case codes are blue links.
- Icons: one outline icon set only (the one chosen in the theme task), 20–22 px.
- Typography: h1 page title, h3 card titles, body1/body2 content, caption for meta text (dates, "15 minuta më parë"). Never skip levels for looks.
- Text contrast ≥ 4.5:1. Never show information by color alone: chips always have text, trends always have an arrow.
- Numbers right-aligned in tables; dates via `formatDate` / `formatDateTime`; relative times ("2 orë më parë") only in activity feeds, with the exact time in a tooltip.
- Charts (when needed): recharts, colors only from tokens (primary, primaryLight, success, warning, gold, gray), legend always visible, values readable without hover.
- No fake data or decorative widgets: show a section only when its endpoint exists.
- Check every new page at 1536×1024 and 390×844 (Playwright screenshot) before reporting done.
## Done when
- `npm run lint` and `npm run build` pass, no `any`, no console errors.
- Works with keyboard only; MUI components keep their accessible labels.

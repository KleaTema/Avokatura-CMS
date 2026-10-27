# 011 — Frontend skeleton

**Phase:** 0 · Foundation  
**Depends on:** [006](006-identity-me-endpoint.md)

## Goal
Create the React app with layout, role-based menu, typed API client and a dev login screen.

## Context
- Menus come from `GET /api/identity/me` → `menus`. Order: Dashboard, Çështje, Chat, Njoftime, Profili, Agjenda, Raporte, Administrimi, Log out.
- All UI text Albanian. Dates shown in Europe/Tirane, format dd.MM.yyyy HH:mm.

## Files to create or change
- src/frontend (Vite + React + TS)
- src/frontend/src/api/ (generated types from `/openapi/v1.json` with openapi-typescript + openapi-fetch)
- src/frontend/src/app/Layout.tsx, routes.tsx, AuthProvider.tsx
- src/frontend/src/pages/DevLogin.tsx
- src/frontend/src/lib/dates.ts (UTC → Europe/Tirane)

## Steps
1. Vite proxy `/api` → backend in dev.
2. TanStack Query provider, MUI theme, React Router.
3. Dev login: dropdown of seeded users; stores email and sends `X-Dev-User`. Production: OIDC login (oidc-client-ts) behind `VITE_AUTH_MODE`.
4. Menu rendered only from `menus`; unknown routes → 404 page; 401 → login; 403 → 'Nuk keni të drejta' page.
5. npm script `gen:api` regenerates the client.

## Acceptance criteria
- [ ] Logging in as Protokoll shows exactly 4 menu items + Log out; as Kryetar shows the full set.
- [ ] `npm run build` and `npm run lint` pass.

## Out of scope
- Feature pages.

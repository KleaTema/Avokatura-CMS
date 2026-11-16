/// <reference types="vite/client" />

// Task 052: no build-time configuration — the app reads /config.json (runtimeConfig.ts) and the identity providers
// from the API (GET /api/auth/config) at start, so ONE build serves every environment.

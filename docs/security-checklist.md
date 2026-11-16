# Security checklist (task 051)

Each item: what is required, how it is enforced, and the test that proves it. Filled phase by phase.

## Phase 1 — development-only features, production identity

| # | Item | How it is enforced | Test |
|---|---|---|---|
| 1.1 | X-Dev-User authentication only in Development | `DevFeatures.HeaderAuthEnabled`: the DevHeader scheme is registered only in Development (switchable off with `DevFeatures:HeaderAuth=false`) | `AuthenticationTests.Production_ignores_dev_header_and_has_no_dev_routes` |
| 1.2 | Dev routes absent outside Development (`POST /api/admin/jobs/{name}/run`, OpenAPI / Scalar) | mapped only when `DevFeatures.JobRunEndpointEnabled` / `IsDevelopment()` → 404 | same test (404, not 401/403) |
| 1.3 | One startup assertion for every dev feature | `DevFeatures.AssertSafe` (Program.cs, first thing): HeaderAuth, JobRunEndpoint or `Scanner:Mode=NoOp` outside Development → the app refuses to start | `AuthenticationTests.Production_with_a_dev_feature_switched_on_refuses_to_start` (3 cases), `DocumentUploadTests.NoOp_scanner_outside_Development_stops_the_startup` |
| 1.4 | Dev login page | the login page asks `GET /api/auth/config`: `mode = dev` only with the dev header (frontend: phase 2) | `Production_ignores_dev_header_and_has_no_dev_routes` (mode = oidc) |
| 2.a | Providers configured, complete, fail fast | `AuthProvidersOptions.Validate` at startup outside Development: at least one enabled; Name, Kind, Authority/MetadataAddress, ClientId, Audience, ValidIssuers, Scopes, DisplayName; EntraId: AllowedTenantIds; no duplicate name / issuer | `Production_without_an_enabled_provider_fails_at_startup`, unit `IdentityClaimMapTests.Validation_refuses_…` |
| 2.b | One JwtBearer scheme per provider, chosen by `iss`; issuer, audience, lifetime, signature (metadata keys, rollover), clock skew ≤ 2 min; unknown issuer → 401 | `AuthExtensions`: policy scheme "Ash" → provider scheme / Reject scheme | `Wrong_issuer_audience_tenant_signature_or_an_expired_token_is_401` |
| 2.b | Entra ID: only AllowedTenantIds | `OnTokenValidated` fails other `tid` | same test (other tenant → 401) |
| 2.b | Each provider on / off by configuration | schemes registered for enabled providers only | `Each_provider_is_accepted_when_enabled_and_refused_when_disabled` |
| 2.c | Claim map per provider | `IdentityClaimMap`: subject `oid`→`sub`; B2C `emails`→`email`; EntraId `email`→`preferred_username`/`upn` (only if an e-mail, warning logged); overridable `ClaimMap` | unit `IdentityClaimMapTests` (B2C local, B2C federated, EntraId member, EntraId B2B guest, fallback, override) |
| 2.d | Linking: first login by e-mail (active user, case insensitive, audited IDENTITY_LINKED), then by (provider, subject) only; never auto-create; one identity per provider | `ResolveUserHandler` + `user_identities` (script 028, unique provider+subject) | `First_login_links_by_email_later_logins_by_subject_and_an_unknown_user_is_never_created`, `AdminUsersTests.First_login_links_an_identity_by_email_then_resolves_by_provider_and_subject` |
| 2.d | Same user at two providers | one row per provider | `Each_provider_is_accepted_when_enabled_and_refused_when_disabled` (2 rows) |
| 2.d | Admin lists / unlinks identities (audited IDENTITY_UNLINKED), the subject never shown | `UserIdentityHandlers`, `GET/DELETE /api/identity/admin/users/{id}/identities` (admin.users) | `The_admin_lists_and_unlinks_identities_and_the_next_login_links_again_by_email` |
| 2.d | `users.external_subject` deprecated | not mapped by EF any more; kept in the table (drop after the first production release) | `AdminUsersTests.First_login_links_an_identity_…` (stays NULL) |
| 2.e | Query-string token only on `/hubs/*` | `TokenOf` / `OnMessageReceived` read `access_token` only for hub paths | `A_query_string_token_is_accepted_on_the_hubs_only` |
| 2.g | AKSHI setup guide | docs/deploy/identity.md | — |

## Phase 2 — frontend sign-in, idle logout, identities in the admin page

| # | Item | How it is enforced | Test |
|---|---|---|---|
| 2.f | MSAL per enabled provider, auth code + PKCE, redirect flow, tokens in sessionStorage | `app/authConfig.ts`: one `PublicClientApplication` per provider from `GET /api/auth/config` (@azure/msal-browser 4.30.0, MIT), `cacheLocation: 'sessionStorage'`, `loginRedirect` | e2e `auth-providers.spec.ts` › two providers… (client_id, response_type=code, code_challenge_method=S256) |
| 2.f | Login page: one button per provider (DisplayName); a single provider redirects at once; Development keeps the dev login | `DevLogin.tsx` | e2e › two providers…, › a single provider redirects at once…; the existing specs log in with the dev login |
| 2.f | The provider used is remembered for silent tokens (API + SignalR) and for its logout | sessionStorage `ash.provider`; `accessToken()` (acquireTokenSilent, interaction → redirect) in `authHeaders` and the hubs' `accessTokenFactory`; "Dil" → `logoutRedirect` of that provider | manual test 051 (needs a real tenant) |
| 11 | Idle logout: 30 min → 60-second warning → logout through the provider | `app/IdleLogout.tsx` in the app shell; `TODO(client-q-idle-timeout)` | e2e › 30 minutes idle… (Playwright fake clock) |
| 2.d | Admin: a user's identities, "Shkëput" (confirmed, audited) | `UsersIdentitiesDialog.tsx` | e2e › the administrator sees a user's sign-in identities; backend `The_admin_lists_and_unlinks_identities_…` |

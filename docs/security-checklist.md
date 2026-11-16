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

## Phase 3 — endpoint authorization, headers, CSP, CORS

| # | Item | How it is enforced | Test |
|---|---|---|---|
| 3.a | Every endpoint requires authorization except `/health`, `/api/auth/config`, the API description in Development (`/openapi`, `/scalar`) | fallback policy = authenticated user; the anonymous endpoints are read from `EndpointDataSource` and compared with the allow-list | `EndpointAuthorizationTests.Every_endpoint_requires_authorization_except_the_allow_list` |
| 3.b | Every endpoint with an object id (case, dossier, document, hearing, party, conversation, message, notification, handoff, assistant link, saved report): users of another office get 404 — never 2xx, never a 403 that reveals the object | `ICaseAccessService` / own-data checks in the handlers. The test generates every call from the endpoint metadata (a DR object, 6 TR users, generated bodies). A 403 is accepted only for the endpoint's own missing system permission. A 4xx before the access check is accepted only when the same call with a non-existent id answers the same (status and code). Covered endpoints are printed in the test output. | `Every_object_endpoint_answers_404_for_an_object_the_user_cannot_see` (lists the covered endpoints in its output) |
| 3.b | Exceptions by design | administration and the Monitor's supervisions (the permission sees every object); the Protokoll's `previousCasePublicId` endpoints work across offices (client decision Q1a) | same test (documented lists) |
| 3.c | Every mutating endpoint has a system permission, a case / document / dossier route (`ICaseAccessService`), or a documented reason (own data, CaseOnly chat policy); stale entries fail too | the test's `MutatingAllowList` | `Every_mutating_endpoint_has_a_permission_or_the_case_access_or_a_documented_reason` |
| fix | Saved reports deadlocked on a user's first request of the hour (separate connection waiting for the request's own lock on `users`) | `SavedReports` writes through the request's unit of work | the object test's seeding (the Monitor's first request saves a report) |
| 4.a | API headers: nosniff, Referrer-Policy no-referrer, Permissions-Policy (camera, microphone, geolocation off), CSP `default-src 'none'; frame-ancestors 'none'`, Cache-Control no-store on /api | `Security/SecurityHeadersMiddleware` (every response, 401/404 included) | `SecurityHeadersTests.Every_api_answer_carries_the_security_headers_and_no_store` |
| 4.a | HSTS only over HTTPS, never in Development | `UseHsts` outside Development (1 year, subdomains) | `Production_sends_HSTS_over_https_only` |
| 4.b | Frontend CSP: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; frame-src blob: <providers>; connect-src 'self' <providers>; font-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self' <providers>; frame-ancestors 'none'` | `tools/security/csp.mjs` builds it from the enabled providers' hosts → `vite preview` and `docs/deploy/nginx-frontend.conf` (regenerate: `node tools/security/csp.mjs <appsettings> docs/deploy/nginx-frontend.conf`). `'unsafe-inline'` for styles only: MUI/emotion inject styles at run time; a nonce needs per-request nonce injection (follow-up). `connect-src` has no bare `wss:` (it would allow any host; CSP3 `'self'` covers same-host wss). `font-src data:`: FullCalendar's icon font. zod runs jitless (`app/zodConfig.ts`) so it never probes `eval`. | e2e `csp.spec.ts` via `npm run e2e:csp` (built app, production headers): login, dashboard, case page with PDF preview, chat, agenda, report export — no violation |
| 4.c-fix | An endpoint's own CSP wins over the API default (the document preview's sandbox policy, task 035) | `SecurityHeadersMiddleware` sets the default only when none is set | `DocumentTimelineTests.Preview_is_inline_in_a_sandbox_and_only_for_pdf_png_jpg` |
| 4.c | CORS only for the configured origin(s) (`Cors:AllowedOrigins`, exact, no wildcard — checked at startup); none configured = no CORS header (production, same origin) | `Security/CorsSetup` | `Cors_answers_only_the_configured_origin_and_nothing_when_none_is_configured` |

## Phase 4 — rate limits, request limits, CSV injection, immediate effect, errors and logs

| # | Item | How it is enforced | Test |
|---|---|---|---|
| 5 | Rate limits per user (anonymous per IP), 429 ProblemDetails "Shumë kërkesa. Provoni pas pak." + Retry-After: document upload / Organika import (upload), every `/export` (export), global search + case list with `q` (search), registration-search + next-level preview (crossOffice, strictest), notify-not-closed (notify), handoff form (handoff), hub connections (hub); the chat's own send limit stays | `Security/RateLimiting`: a global limiter keyed by the matched route; limits in `RateLimits:{policy}` (appsettings) | `HardeningTests.Over_the_limit_a_user_gets_429_with_retry_after_and_another_user_is_not_affected` |
| 6 | Body ≤ 1 MB (Content-Length refused at once with 413, Kestrel's per-request limit for bodies without a length); the upload keeps its own limit (task 033), Organika 3 MB; JSON depth ≤ 32; multipart ≤ 16 headers per section, boundary ≤ 70 characters; another content type on a JSON endpoint → 415 | `Security/RequestLimits` (+ `RequestLimitsMiddleware`), `FormOptions`, the upload's `MultipartReader` | `HardeningTests.Bodies_over_1_MB_are_413_deep_json_and_other_content_types_are_refused` |
| 6-fix | An unreadable body (bad / too deep JSON) was a 500 in Development (minimal APIs throw `BadHttpRequestException` there) | `ApiExceptionHandler` maps it to its own status with a generic title | same test |
| 7 | CSV injection: a TEXT cell starting with `= + - @ TAB CR` gets a leading `'`; numbers are untouched | `CsvReportWriter.Cell` | `HardeningTests.A_formula_in_a_party_name_is_exported_as_text` (`=HYPERLINK("http://x","y")`) |
| 8 | Deactivation, role removal: next request 401 / 404 (not after the 60-second cache) | `UserAdminSupport.InvalidateAccess` removes the cached access now AND after the commit (`IUnitOfWork.OnCommitted`) | `HardeningTests.Deactivation_role_removal_and_an_ended_assistant_link_count_from_the_next_request` |
| 8 | Ended assignment / supervision / assistant link: case access is computed per request (no cross-request cache) | `ICaseAccessService` | same test (assistant link); `TransferLawyerTests` (transfer); `SupervisionTests.A_lawyer_supervision_follows_the_current_primary_and_ending_it_removes_the_cases` |
| 8 | Statistics of a Kryetar are not cached (they depend on the supervised cases right now) | `StatisticsQueries`: cached only without a Kryetar scope | — |
| 8 | "Shkëput" blocks that sign-in account (tombstone; 401 from the next request, never re-linked by e-mail; other accounts work); "Lejo sërish" with a reason; both audited (IDENTITY_UNLINKED / IDENTITY_ALLOWED) — decision 16.11.2026 | `user_identities.blocked_at` (script 029), `ResolveUserHandler`, `UserIdentityHandlers` | `HardeningTests.A_blocked_sign_in_account_is_401_at_once_other_accounts_work_and_allow_lets_it_link_again`, `AuthenticationTests.The_admin_lists_identities_and_unlinking_blocks_that_account` |
| 9 | Production errors: a generic ProblemDetails + traceId, no exception text or stack; developer page only in Development | `ApiExceptionHandler` (no exception detail in the body) | `HardeningTests.A_production_500_has_no_exception_text_only_a_generic_title_and_a_trace_id` |
| 9 | Logs: no bearer tokens, dev header / dev_user, access_token, passwords, connection strings or search text — over the WHOLE integration run | `LogRedaction` (task 040/047), request logging without query strings; every test host writes to `SuiteLogSink` | `HardeningTests.The_logs_of_the_whole_run_hold_no_tokens_dev_users_passwords_connection_strings_or_search_text` |

## Phase 5 — database privileges, dependencies, ZAP

| # | Item | How it is enforced | Test |
|---|---|---|---|
| 10 | App accounts (ash_app, ash_chat_app, ash_reporting, ash_audit_writer, ash_audit_digest): nothing server-wide, no DROP / ALTER / CREATE / INDEX / TRIGGER / EVENT / GRANT OPTION / SUPER / FILE / PROCESS | scripts 001, 008, 016, 019 (`SHOW GRANTS` in the test MySQL, same scripts as production) | `DatabasePrivilegeTests.An_application_account_cannot_change_the_schema_or_hand_out_rights` (5 accounts) |
| 10 | ash_reporting reads only; the audit accounts never UPDATE / DELETE ash_audit (the writer: INSERT + SELECT) | same | `The_reporting_account_only_reads`, `The_audit_accounts_can_never_update_or_delete_the_audit_log` |
| 10 | ash_migrator: its four schemas only, no GRANT OPTION | script 001 | `The_migrator_has_its_four_schemas_only_and_cannot_grant` |
| 10 | Every production account REQUIRE SSL; outside Development every connection string SslMode=Required / VerifyCA / VerifyFull, else the app refuses to start (names only in the message) | script 001; `Security/DatabaseSecurity.AssertSsl` (Program.cs) | `Every_production_account_requires_ssl`, `Outside_Development_a_connection_string_without_SslMode_Required_stops_the_startup` |
| 12 | Dependency scan: `dotnet list package --vulnerable --include-transitive` + `npm audit --omit=dev --audit-level=high`; high / critical → exit 1 | `tools/security/scan-deps.ps1` (CI: task 052) | run 2026-11-16: 0 high/critical in NuGet and npm |
| 12 | Licences of every production dependency (NuGet without test projects, npm without dev), from the local caches; non-permissive flagged | `tools/security/third-party-notices.mjs` → `THIRD-PARTY-NOTICES.md` | 230 packages, 6 to review (see the file) |
| 13 | OWASP ZAP baseline (passive), authenticated with the dev header through a ZAP replacer rule, Development only (the script checks `/api/auth/config`) | `tools/security/zap-baseline.ps1` → `docs/security/zap-baseline.html` | to be run (Docker) — findings: pending the report |

## Task 052 — deployment

| # | Item | How it is enforced | Test |
|---|---|---|---|
| D1 | Containers run non-root with a read-only root filesystem, no capabilities, no-new-privileges | `deploy/docker/*.Dockerfile` (`USER`), `deploy/docker-compose.production.yml` | container check (final run) |
| D2 | Configuration only from the environment; every missing setting listed at once; no secret in `appsettings.Production.json` | `Configuration/StartupValidator` | `The_startup_validator_lists_every_missing_setting_at_once`, `AppSettings_Production_holds_no_secret` |
| D3 | X-Forwarded-For / -Proto believed only from the configured proxies | `Security/ReverseProxy` | `Forwarded_headers_are_used_from_a_known_proxy_and_ignored_from_anyone_else` |
| D4 | The anonymous login configuration holds public values only | `Auth/AuthConfigEndpoint` | `The_anonymous_login_configuration_holds_public_values_only` |
| D5 | CSP with the identity host from the environment; the app warns when it differs from the API's providers | `deploy/frontend/docker-entrypoint.sh`, `app/runtimeConfig.ts` | container check, manual test 052.2 |
| D6 | Images scanned (Trivy HIGH/CRITICAL with a fix fail), SBOMs per build, secrets only as environment secrets | `.github/workflows/ci.yml`, `release.yml` | CI |
| D7 | `/api/version` needs a login; only the health probes are anonymous | `Configuration/AppVersion` | `Version_needs_a_login_and_the_health_probes_do_not` |

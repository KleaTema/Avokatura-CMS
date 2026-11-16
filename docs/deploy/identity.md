# Identity providers — setup for AKSHI

The system signs users in through **Azure AD B2C**, **Microsoft Entra ID**, or **both**. AKSHI chooses; the system
needs configuration only (task 051). Switching later, e.g. to Entra External ID, is also configuration only.
`TODO(akshi-q-identity)`: the choices below are open questions for AKSHI (docs/client-questions.md, AKSHI section).

## How the system uses the providers

- **Accounts:**
  - Users are created **only by the administrator** of the system, by e-mail. A sign-in never creates a user.
  - The first sign-in at a provider links that provider account to the active user with the same e-mail (case
    insensitive). It is recorded as `IDENTITY_LINKED` in the audit log.
  - Later sign-ins find the user by the provider's stable id (`oid`), so an e-mail change at the provider does not
    break the link.
  - A user may sign in through several providers, at most one account per provider. The administrator can unlink an
    account (*Përdoruesit → identitetet → Shkëput*).
- **Token checks:** the API validates every token's issuer, audience, lifetime and signature (keys from the provider's
  metadata, rolled over automatically), with at most 2 minutes of clock skew. An unknown issuer is refused.
- **Entra ID tenants:** a token must come from one of the configured tenants. Other tenants' users come in as B2B
  guests of our tenant.

## App registrations (per provider)

1. **API** ("ASH API"):
   - Expose an API: Application ID URI, e.g. `api://ash-api`.
   - Scope `access` (admins and users can consent).
   - This URI is the `Audience`.
2. **SPA** ("ASH Web"):
   - Platform **Single-page application**, auth code flow with PKCE. No implicit flow and no client secret.
   - Redirect URIs: `https://<frontend host>/` and `https://<frontend host>/auth/callback`.
   - Front-channel logout URL: `https://<frontend host>/login`.
   - API permissions: the API's `access` scope (grant admin consent).
   - This application id is the `ClientId`.

## Azure AD B2C

- **User flow:** **Sign in** only (not "Sign up and sign in"). Self sign-up must be **disabled**.
- **MFA:** **on** (always, or conditional per AKSHI policy).
- **Local accounts** (e-mail + password) for staff of non-government institutions: **invitation only**. The
  administrator of the system creates the user here; AKSHI (or a delegated admin) creates the B2C account with the same
  e-mail. Who does the inviting is an open question.
- **Optional:** federate B2C with the government Entra ID, so government staff sign in with their institutional account
  through the same flow.
- **Claims:**
  - application claims of the user flow: **Email Addresses** (`emails`) and **User's Object ID** (`oid`);
  - token compatibility setting: `iss` with the tenant id (`https://<tenant>.b2clogin.com/<tenant-id>/v2.0/`).
- **Configuration** (`Auth:Providers[]`, Kind `B2C`):

  | Setting | Value |
  |---|---|
  | `Authority` | `https://<tenant>.b2clogin.com/<tenant>.onmicrosoft.com/<policy>/v2.0` (the user-flow policy) |
  | `ValidIssuers` | `["https://<tenant>.b2clogin.com/<tenant-id>/v2.0/"]` |
  | `Scopes` | `["https://<tenant>.onmicrosoft.com/ash-api/access"]` |

## Microsoft Entra ID

- **Tenant:** the government tenant id goes into `AllowedTenantIds`. A token of any other tenant is refused; no
  multi-tenant acceptance.
- **Optional claim:** add `email` to the access token (Token configuration → Add optional claim → Access → email).
  Without it the system falls back to `preferred_username` only when it looks like an e-mail, and logs a warning.
- **Guests:** staff of other tenants are invited as **B2B guests** of our tenant. Their tokens carry our `tid`.
- **Configuration** (Kind `EntraId`):

  | Setting | Value |
  |---|---|
  | `Authority` | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
  | `ValidIssuers` | `["https://login.microsoftonline.com/<tenant-id>/v2.0"]` |
  | `AllowedTenantIds` | `["<tenant-id>"]` |
  | `Scopes` | `["api://ash-api/access"]` |

## Token lifetimes

- Access token: 60 minutes (the default); the SPA renews it silently.
- The system logs a user out after 30 minutes without activity, after a 60-second warning
  (`TODO(client-q-idle-timeout)`). Logging out also ends the provider's session.

## Claims the system needs

| Claim | B2C | Entra ID |
|---|---|---|
| stable id | `oid` (fallback `sub`) | `oid` (fallback `sub`) |
| e-mail | `emails` (array) | `email` (optional claim) |
| tenant | — | `tid` (must be allowed) |
| issuer / audience / expiry | `iss` / `aud` / `exp` | `iss` / `aud` / `exp` |

## Example configuration (both providers)

```json
"Auth": {
  "Providers": [
    { "Name": "b2c", "Kind": "B2C", "Enabled": true,
      "Authority": "https://ash.b2clogin.com/ash.onmicrosoft.com/B2C_1_signin/v2.0",
      "ClientId": "<spa app id>", "Audience": "<api app id or URI>",
      "ValidIssuers": ["https://ash.b2clogin.com/<tenant-id>/v2.0/"],
      "Scopes": ["https://ash.onmicrosoft.com/ash-api/access"],
      "DisplayName": "Hyr me email dhe fjalëkalim" },
    { "Name": "entra", "Kind": "EntraId", "Enabled": true,
      "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
      "ClientId": "<spa app id>", "Audience": "api://ash-api",
      "ValidIssuers": ["https://login.microsoftonline.com/<tenant-id>/v2.0"],
      "AllowedTenantIds": ["<tenant-id>"],
      "Scopes": ["api://ash-api/access"],
      "DisplayName": "Hyr me llogarinë e institucionit (Microsoft)" }
  ]
}
```

- **Fail fast:** outside Development the API refuses to start if no provider is enabled, if a required setting is
  missing, or if any development-only feature is switched on.
- **Provider names:** the `Name` of a provider is stored with every linked account (`user_identities.provider_name`).
  Do not rename a provider once users have signed in through it.

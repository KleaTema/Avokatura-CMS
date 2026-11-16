# ASH — deployment

Task 052. For AKSHI's operators and the developers who prepare a release. **Përmbledhje në shqip: në fund.**

## What runs

| Piece | Image / source | Notes |
|---|---|---|
| `web` | `ash-frontend:<version>` (nginx-unprivileged, port 8080) | The SPA, `/config.json`, CSP, and proxies `/api` and `/hubs` to the API. Read-only root, UID 101. |
| `api` | `ash-api:<version>` (ASP.NET, port 8080) | Stateless. Read-only root, non-root. Logs are JSON lines on stdout. |
| `migrator` | `ash-migrator:<version>` | Runs once per upgrade, before the API, using the `ash_migrator` account. |
| `clamav` | `clamav/clamav:stable` | Virus scan of every upload (`clamav/clamd-ash.conf`). |
| `redis` | `redis:7.4-alpine` | Only with more than one API instance (the SignalR backplane). |
| Provided by AKSHI | | MySQL 8 (`mysql/`), S3-compatible storage, the SMTP relay, the TLS ingress, the B2C tenant, backups. |

`docker-compose.production.yml` is the reference wiring (`TODO(akshi-q-hosting)`: the real platform may differ). The release assets on GitHub hold this folder for the exact version: `ash-deploy-<version>.tar.gz`.

Images are tagged only with immutable versions, never `latest`. The default registry is `ghcr.io/<owner>` (`TODO(akshi-q-registry)`).

## Environments

| Environment | `ASH_ENVIRONMENT` | Login | Data |
|---|---|---|---|
| Development | `Development` | The dev login (X-Dev-User) and NoOp scanner are allowed **only** here. | Local Docker (`docker-compose.yml` in the repository root). |
| UAT | `Uat` | Its **own** B2C tenant or user flow (`TODO(akshi-q-uat-login)`). The dev login cannot be switched on: the API refuses to start. | Test data only. |
| Production | `Production` | B2C (production tenant). | Real data. |

Outside Production the footer shows "Mjedisi: Uat". Every environment has its own `.env`, its own database accounts and its own B2C app registrations.

## First installation

1. **MySQL 8** (the DBA):
   - put `mysql/mysql-production.cnf` into `/etc/mysql/mysql.conf.d/ash.cnf` on the primary and the replica, then restart;
   - give MySQL a TLS certificate, since `require_secure_transport = ON`.
2. **Migrate** (creates the schemas and accounts):
   ```sh
   cp .env.example .env    # fill it in
   docker compose -f docker-compose.production.yml --env-file .env --profile migrate run --rm migrator
   ```
   For the very first run, `ASH_DB_MIGRATOR` uses an administrative account that the DBA provides. Every later run uses `ash_migrator`.
3. **Accounts** (the DBA): fill in `mysql/production-accounts.sql.template` and run it once as root. Do not keep the filled-in copy.
4. **Storage**: create the three buckets.
   - `ash-uploads-quarantine`: a lifecycle rule that expires objects after 1 day.
   - `ash-documents`: object lock on.
   - `ash-chat-files`: versioning on.
   - Give one access key read/write on those three buckets only.
5. **B2C**: see *B2C per environment* below.
6. **Start**:
   ```sh
   docker compose -f docker-compose.production.yml --env-file .env up -d
   ```
7. **Ingress**: TLS terminates at the ingress, which forwards to `ASH_WEB_BIND`.
   - Set `ASH_PROXY_NETWORK` to the ingress's network. Otherwise the client IP in the audit log is the proxy's.
   - Set `ASH_HSTS=off` if the ingress already sends HSTS.
8. Run the **smoke checklist** below.

## Upgrade

1. Read the release notes. If they list new `GRANT` lines and your accounts are not at host `10.%`, re-apply those grants to your host.
2. Back up (see *Backup*).
3. Set `ASH_VERSION=<new>` in `.env`.
4. Migrate: `docker compose … --profile migrate run --rm migrator`. It must exit with 0.
5. Start the new version: `docker compose … up -d`. The API is ready when `/health/ready` returns 200.
6. Run the smoke checklist.

Schema changes are **expand-then-contract** (`.github/skills/ash-sql-migration/SKILL.md`, rule 14):
- a release only *adds* to the schema;
- removals come in a later release.

So the previous API version always works with the new schema.

## Rollback

1. Set `ASH_VERSION=<previous>` and run `docker compose … up -d`. **Do not** run an older migrator: it has nothing to undo, and the new schema already serves the old version.
2. Restore the database only if data is damaged (see *Restore*). This loses everything written after the backup.

## Backup and restore

`TODO(akshi-q-backups)`: AKSHI's backup system and retention.

- **MySQL**:
  - a nightly full backup with the `ash_backup` account: `mysqldump --single-transaction --routines --events --triggers --source-data=2 --databases ash_core ash_chat ash_audit ash_ops`, or Percona XtraBackup;
  - plus the binlogs, kept 14 days, for point-in-time recovery;
  - encrypt the backups and keep a copy off site;
  - test a restore every quarter.
- **Storage**: replicate or version the buckets. Documents are under object lock and cannot be deleted before the lock expires.
- **Restore**:
  1. stop `api`;
  2. restore the dump, then replay the binlogs up to the moment you choose (`mysqlbinlog --stop-datetime`);
  3. start the **same** version that wrote the data;
  4. run the smoke checklist.

## B2C per environment

Each environment (UAT, Production) has its own tenant or user flow and **two** app registrations:

- **ash-api**: exposes the scope `access`. Its Application ID is `ASH_B2C_API_AUDIENCE`, and the full scope URI is `ASH_B2C_API_SCOPE`.
- **ash-spa** (single-page application):
  - redirect URI `https://<host>/` and logout URL `https://<host>/login`;
  - its client id is `ASH_B2C_SPA_CLIENT_ID`;
  - it has API permission to `ash-api/access`, with admin consent.
- **User flow**:
  - sign-in only (`B2C_1_signin`), with no self sign-up;
  - its authority is `ASH_B2C_AUTHORITY`.
- **`ASH_IDP_HOSTS`**: must be the authority's host (e.g. `tenant.b2clogin.com`), because the CSP allows only that host. If it differs, the login page shows **"Konfigurimi i hyrjes nuk përputhet me CSP"** and the sign-in is blocked.
- **Linking users**: an administrator links each B2C account to an ASH user (task 051).

## Smoke checklist (after every installation, upgrade, rollback)

- [ ] `curl -fsS https://<host>/health/live` returns `Healthy`. `curl -fsS https://<host>/health/ready` returns 200.
- [ ] In the API log, the first lines show the right `Version` and `Environment`, and there is no `MySQL server setting` warning.
- [ ] `curl -s https://<host>/config.json` shows the right `environment`, the right `version`, and `cspHosts` equal to the B2C host.
- [ ] `curl -sI https://<host>/` shows `Content-Security-Policy` naming the B2C host, `Strict-Transport-Security` (from nginx or the ingress), `X-Content-Type-Options: nosniff`, and `X-Frame-Options: DENY`.
- [ ] The login page does **not** show "Konfigurimi i hyrjes nuk përputhet me CSP", and B2C sign-in works.
- [ ] The footer shows the release, and outside Production the environment.
- [ ] Open a case. Upload a small PDF: it becomes *Gati* after the scan. Download it.
- [ ] The chat shows a message live in a second browser (the WebSocket through the proxy works).
- [ ] The audit log shows the real client IP of your login, not the proxy's.
- [ ] A test e-mail notification arrives (UAT: a test mailbox).

## Operations notes

- **Configuration** comes only from the environment, never from files inside the image. A missing setting stops the API, and it lists **every** missing setting at once.
- **Health**:
  - `/health/live` is the process only. Use it for the restart probe.
  - `/health/ready` checks every dependency: the databases, the MySQL settings, storage, ClamAV and the jobs. Use it for the traffic probe.
- **Several API instances**:
  - set `ASH_API_INSTANCES` above 1 and `ASH_REDIS`, which the SignalR backplane needs, or the API refuses to start;
  - background jobs run **once** across all instances: each run takes a MySQL `GET_LOCK('job:<name>', 0)` (task 041), and an instance that does not get the lock skips that run;
  - uploads go to object storage, so the instances need no shared disk.
- **Logs**:
  - JSON on stdout, one event per line, with `CorrelationId` (also the `X-Request-Id` response header), `Environment` and `Version`;
  - query strings are never logged.

---

## Përmbledhje (shqip)

- **Çfarë instalohet:** tre imazhe: `ash-frontend` (nginx), `ash-api` dhe `ash-migrator`. Përveç tyre nevojiten ClamAV, dhe Redis vetëm kur ka më shumë se një instancë API. MySQL, ruajtja S3, SMTP, TLS/ingress, B2C dhe kopjet rezervë i siguron AKSHI.
- **Instalimi i parë:**
  1. DBA vendos `mysql/mysql-production.cnf` dhe aktivizon TLS.
  2. Plotësoni `.env` sipas `.env.example`.
  3. Ekzekutoni migratorin.
  4. DBA ekzekuton `production-accounts.sql.template` me fjalëkalimet reale.
  5. Krijoni tre kovat e ruajtjes.
  6. Konfiguroni B2C.
  7. Nisni me `docker compose … up -d`.
  8. Kryeni listën e kontrollit.
- **Përditësimi:**
  1. Bëni kopje rezervë.
  2. Vendosni `ASH_VERSION` të ri.
  3. Ekzekutoni migratorin. Duhet të dalë me kodin 0.
  4. Nisni versionin e ri dhe prisni që `/health/ready` të kthejë 200.
  5. Kryeni listën e kontrollit.
- **Kthimi mbrapa:** kthehuni te versioni i mëparshëm i imazheve. Skema mbetet, sepse ndryshimet janë "zgjero, pastaj ngushto". Restaurimi i bazës bëhet vetëm kur të dhënat janë dëmtuar.
- **Kopjet rezervë:** çdo natë bëhet kopje e plotë e MySQL. Binlog-et ruhen 14 ditë për rikthim në një moment të caktuar. Kovat ruhen me versionim ose replikim. Restaurimi provohet çdo tremujor.
- **B2C:** çdo mjedis (UAT, Prodhim) ka tenant-in ose rrjedhën e vet dhe dy regjistrime aplikacioni (`ash-api`, `ash-spa`). `ASH_IDP_HOSTS` duhet të jetë hosti i autoritetit B2C. Përndryshe faqja e hyrjes shfaq "Konfigurimi i hyrjes nuk përputhet me CSP".
- **UAT:** hyrja e zhvillimit (X-Dev-User) nuk lejohet. API nuk niset nëse aktivizohet. UAT përdor B2C-në e vet (`TODO(akshi-q-uat-login)`).
- **Lista e kontrollit:** shihni seksionin *Smoke checklist* më sipër. Kontrolloni shëndetin, `/config.json`, kokat CSP/HSTS, hyrjen me B2C pa paralajmërimin CSP, versionin në fund të faqes, ngarkimin dhe shkarkimin e një PDF, bisedën live, IP-në reale në auditim dhe një email testues.

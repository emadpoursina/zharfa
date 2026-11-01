# Deploy — Docker Compose (Demo 1)

Services: `db` (PostgreSQL 18), `api` (ASP.NET Core), `web` (static frontend +
nginx reverse proxy for `/api`).

## Install

```bash
cp deploy/.env.example deploy/.env   # then set POSTGRES_PASSWORD + AI provider values
docker compose -f deploy/docker-compose.yml up --build -d
curl -s http://localhost:8080/api/health   # proxied to the API health endpoint
```

Open http://localhost:8080 — Persian RTL frontend (demonstration data).

### Demo accounts

Seed demonstration accounts (documented demonstration credentials, change before any real use):
- Manager: `manager` / `manager123` (view dashboard, inventory alerts, receivables, AI chat)
- Administrator: `admin` / `admin123` (view dashboard, admin provider status, reconciliation)

### Configuration & environment variables

All configuration is supplied via environment variables (`deploy/.env` or server env):
- `AI_PROVIDER_NAME`: Display name of the cloud AI provider (e.g. OpenAI).
- `AI_PROVIDER_ENDPOINT`: API base URL including any `/v1` path (e.g. `https://api.openai.com/v1`).
- `AI_PROVIDER_MODEL`: Model identifier (e.g. `gpt-4o-mini`).
- `AI_PROVIDER_API_KEY`: API secret key.
  * Note: Provider configuration is env-only; an admin-only redacted status endpoint (`GET /api/admin/provider`) returns `name`, `endpoint`, `model`, and `configured` status, and NEVER the secret key.
  * The local Ollama seam is an interface only (never implemented in Demo 1).
- `INVENTORY_LOW_STOCK_THRESHOLD`: Optional threshold for low-stock inventory alerts (default `10`, one server-side threshold for all products).
- `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`: PostgreSQL connection credentials.
- `ZHARFA_PORT`: Published port for the reverse proxy (default `8080`).

## Migration & seed behavior

- Migrations run automatically at API startup:
  * Forward-only SQL migrations located in `db/migrations`.
  * Tracked in the `schema_migrations` table.
  * Each migration file runs inside its own database transaction.
  * The API fails fast on any migration error and halts startup.
- Seeds apply exactly once when the `app_user` table is empty (from `db/seed`).
- NO manual DB edits and NO Mabna access at any point (FR-014).

## Restore / backup

```bash
docker compose -f deploy/docker-compose.yml exec db pg_dump -U zharfa -d zharfa > zharfa.sql   # backup
docker compose -f deploy/docker-compose.yml exec -T db psql -U zharfa -d zharfa < zharfa.sql   # restore
```

A documented PostgreSQL backup + restore procedure must be RUN AND VERIFIED ONCE
on a Docker host before Demo 1 sign-off (FR-018) with the result recorded by the
tester — do not claim it was run here (there is no Docker daemon on this host) (PRD §7.7).

## Desktop Shell

The desktop shell connects to this deployment by setting:
`ZHARFA_FRONTEND_URL=http://localhost:8080` for the WebView2 shell.

## Notes

- Nothing here connects to any legacy system; Demo 1 has no Mabna access.
- AI keys and DB credentials live in `deploy/.env` / Docker secrets only.
- The desktop shell connects to this deployment by setting
  `ZHARFA_FRONTEND_URL=http://localhost:8080`.

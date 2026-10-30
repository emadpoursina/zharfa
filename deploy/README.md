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

## Restore / backup

```bash
docker compose -f deploy/docker-compose.yml exec db pg_dump -U zharfa -d zharfa > zharfa.sql   # backup
docker compose -f deploy/docker-compose.yml exec -T db psql -U zharfa -d zharfa < zharfa.sql   # restore
```

A documented PostgreSQL backup + restore procedure must be validated once
before Demo 1 sign-off (PRD §7.7).

## Notes

- Nothing here connects to any legacy system; Demo 1 has no Mabna access.
- AI keys and DB credentials live in `deploy/.env` / Docker secrets only.
- The desktop shell connects to this deployment by setting
  `ZHARFA_FRONTEND_URL=http://localhost:8080`.

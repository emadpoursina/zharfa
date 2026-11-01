#!/usr/bin/env bash
# Start, stop and inspect the Zharfa Demo 1 stack (macOS / Linux).
#
#   ./deploy/zharfa.sh up       build (if needed) and start in the background
#   ./deploy/zharfa.sh down     stop; database data is kept
#   ./deploy/zharfa.sh restart  rebuild and restart everything
#   ./deploy/zharfa.sh status   show container status
#   ./deploy/zharfa.sh logs     follow API logs (Ctrl+C to stop)
#   ./deploy/zharfa.sh reset    stop AND delete the database (asks first)
set -euo pipefail

cd "$(dirname "$0")/.."

FILES=(-f deploy/docker-compose.yml)
if [ -f deploy/docker-compose.9router.yml ]; then
  FILES+=(-f deploy/docker-compose.9router.yml)
fi

compose() { docker compose "${FILES[@]}" "$@"; }

require_env() {
  if [ ! -f deploy/.env ]; then
    echo "deploy/.env is missing." >&2
    echo "Create it with: cp deploy/.env.example deploy/.env  (then set POSTGRES_PASSWORD)" >&2
    exit 1
  fi
}

case "${1:-up}" in
  up)
    require_env
    compose up --build -d
    echo ""
    echo "Zharfa is starting. Open http://localhost:8080 (or the ZHARFA_PORT in deploy/.env)."
    ;;
  restart)
    require_env
    compose up --build -d --force-recreate
    ;;
  down)
    compose down
    ;;
  status)
    compose ps
    ;;
  logs)
    compose logs -f api
    ;;
  reset)
    read -r -p "This deletes the Zharfa database volume. Type 'yes' to continue: " answer
    if [ "$answer" = "yes" ]; then
      compose down -v
      echo "Database removed. Run './deploy/zharfa.sh up' to start fresh."
    else
      echo "Cancelled."
    fi
    ;;
  *)
    echo "Usage: $0 {up|down|restart|status|logs|reset}" >&2
    exit 1
    ;;
esac

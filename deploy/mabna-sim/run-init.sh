#!/usr/bin/env bash
# Runs inside the mabna-sim-init container. Applies *.sql from /init/generated
# and /init/generic merged and sorted by filename. If TotalSystem already
# exists only files starting with 9 (idempotent, e.g. logins) are re-run.
set -euo pipefail
SQLCMD=$(ls /opt/mssql-tools*/bin/sqlcmd | head -n1)
run() { "$SQLCMD" -C -S mabna-sim -U sa -P "$SA_PASSWORD" -b -f 65001 "$@"; }

exists=$(run -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name='TotalSystem'" | tr -d '[:space:]')
files=$(ls /init/generated/*.sql /init/generic/*.sql 2>/dev/null | awk -F/ '{print $NF" "$0}' | LC_ALL=C sort | cut -d' ' -f2-)
if ! ls /init/generated/0*.sql >/dev/null 2>&1; then
  echo "WARNING: deploy/mabna-sim/generated/ has no schema. Run deploy/mabna-sim/gen_schema.py first (see README)." >&2
fi
for f in $files; do
  base=$(basename "$f")
  if [ "$exists" != "0" ] && [[ "$base" != 9* ]]; then echo "skip $base (TotalSystem exists)"; continue; fi
  echo "== $base"
  if [[ "$base" == 9* ]]; then
    run -v RO_PASSWORD="$RO_PASSWORD" -i "$f"
  else
    run -x -i "$f"
  fi
done
echo "mabna-sim init done"

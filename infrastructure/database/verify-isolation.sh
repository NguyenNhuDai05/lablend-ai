#!/usr/bin/env bash
set -euo pipefail

contexts=(identity catalog inventory borrowing notification recommendation)
for service in "${contexts[@]}"; do
  password_var="${service^^}_DB_PASSWORD"
  actual=$(PGPASSWORD="${!password_var}" psql -h 127.0.0.1 -U "${service}_user" -d "${service}_db" -qAt -c 'SELECT current_user, current_database()')
  [[ "$actual" == "${service}_user|${service}_db" ]] || { echo 'Own-database authentication failed'; exit 1; }
  for other in "${contexts[@]}"; do
    [[ "$service" == "$other" ]] && continue
    if result=$(PGPASSWORD="${!password_var}" psql -h 127.0.0.1 -U "${service}_user" -d "${other}_db" -qAt -c 'SELECT 1' 2>&1); then
      echo "FAIL: $service accessed ${other}_db"; exit 1
    fi
    [[ "$result" == *'permission denied for database'* ]] || { echo "FAIL: unexpected isolation result for $service -> $other"; exit 1; }
  done
  echo "PASS: $service authenticates to own database and cannot connect to other five databases."
done

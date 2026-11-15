#!/usr/bin/env bash
set -euo pipefail

# Runs automatically only on a new PostgreSQL volume. Safe to explicitly rerun
# against this coursework instance: existing databases are preserved, role passwords reconciled.
for service in identity catalog inventory borrowing notification recommendation; do
  password_var="${service^^}_DB_PASSWORD"
  password="${!password_var:?Missing per-service database password}"
  psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1     --set=service_role="${service}_user" --set=service_db="${service}_db" --set=service_password="$password" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'service_role', :'service_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'service_role') \gexec
SELECT format('ALTER ROLE %I PASSWORD %L', :'service_role', :'service_password') \gexec
SELECT format('CREATE DATABASE %I OWNER %I', :'service_db', :'service_role')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'service_db') \gexec
SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'service_db') \gexec
SELECT format('GRANT ALL ON DATABASE %I TO %I', :'service_db', :'service_role') \gexec
SQL
  psql --username "$POSTGRES_USER" --dbname "${service}_db" --set=ON_ERROR_STOP=1     --set=service_role="${service}_user" <<'SQL'
REVOKE ALL ON SCHEMA public FROM PUBLIC;
SELECT format('ALTER SCHEMA public OWNER TO %I', :'service_role') \gexec
SQL
done
psql --username "$POSTGRES_USER" --dbname postgres --set=ON_ERROR_STOP=1 <<'SQL'
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
REVOKE CONNECT ON DATABASE template1 FROM PUBLIC;
SQL

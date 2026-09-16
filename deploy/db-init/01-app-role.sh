#!/bin/bash
# Creates the restricted runtime role the API connects as. Postgres row-level security does not apply to a
# table's owner, so migrations run as the owner (POSTGRES_USER) while the API runs as rfid_app.
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
  DO \$\$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rfid_app') THEN
      CREATE ROLE rfid_app LOGIN PASSWORD '${APP_DB_PASSWORD:-rfid_app}';
    END IF;
  END \$\$;
  GRANT CONNECT ON DATABASE "$POSTGRES_DB" TO rfid_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE "$POSTGRES_USER" GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO rfid_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE "$POSTGRES_USER" GRANT USAGE, SELECT ON SEQUENCES TO rfid_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE "$POSTGRES_USER" GRANT USAGE ON SCHEMAS TO rfid_app;
EOSQL

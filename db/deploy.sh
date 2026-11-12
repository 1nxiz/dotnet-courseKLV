#!/usr/bin/env bash
# =====================================================================
# Розгортання всіх трьох баз з нуля (Linux / macOS / Git Bash).
# Порядок: p1 schema -> procedures -> seed; p2 schema -> seed; security; p3 collections -> seed.
# Повторний запуск безпечний (усі скрипти ідемпотентні).
#
# Варіант А (рекомендовано) — бази в Docker-контейнерах, нічого ставити не треба:
#   docker run -d --name library-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Library_Pass123' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
#   docker run -d --name library-mongo -p 27017:27017 mongo:7
#   SQL_CONTAINER=library-sql MONGO_CONTAINER=library-mongo SA_PASSWORD='Library_Pass123' ./db/deploy.sh
#
# Варіант Б — локальні sqlcmd і mongosh:
#   SQL_SERVER='localhost,1433' SA_PASSWORD='...' MONGO_URI='mongodb://localhost:27017' ./db/deploy.sh
# =====================================================================
set -euo pipefail
DB_DIR="$(cd "$(dirname "$0")" && pwd)"
SA_PASSWORD="${SA_PASSWORD:-Library_Pass123}"
LOANS_APP_PASSWORD="${LOANS_APP_PASSWORD:-Loans_App#2026}"
BOOKS_APP_PASSWORD="${BOOKS_APP_PASSWORD:-Books_App#2026}"

if [ -n "${SQL_CONTAINER:-}" ]; then
  docker exec "$SQL_CONTAINER" mkdir -p /tmp/library-db
  docker cp "$DB_DIR/." "$SQL_CONTAINER:/tmp/library-db"
  SQLCMD=(docker exec -i "$SQL_CONTAINER" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD")
  SQL_ROOT=/tmp/library-db
else
  SQLCMD=(sqlcmd -C -S "${SQL_SERVER:-localhost,1433}" -U sa -P "$SA_PASSWORD")
  SQL_ROOT="$DB_DIR"
fi

if [ -n "${MONGO_CONTAINER:-}" ]; then
  docker exec "$MONGO_CONTAINER" mkdir -p /tmp/library-db
  docker cp "$DB_DIR/." "$MONGO_CONTAINER:/tmp/library-db"
  MONGOSH=(docker exec -i "$MONGO_CONTAINER" mongosh --quiet "mongodb://localhost:27017")
  MONGO_ROOT=/tmp/library-db
else
  MONGOSH=(mongosh --quiet "${MONGO_URI:-mongodb://localhost:27017}")
  MONGO_ROOT="$DB_DIR"
fi

run_sql() {
  echo ">> sqlcmd $1"
  # -b: зупинитися на першій помилці; -I: QUOTED_IDENTIFIER ON (потрібно для фільтрованих індексів)
  "${SQLCMD[@]}" -b -I -i "$SQL_ROOT/$1" -v LoansAppPassword="$LOANS_APP_PASSWORD" BooksAppPassword="$BOOKS_APP_PASSWORD"
}
run_mongo() {
  echo ">> mongosh $1"
  "${MONGOSH[@]}" --file "$MONGO_ROOT/$1"
}

echo "== Проєкт №1: LoansDb (SQL Server)"
run_sql p1/schema.sql
run_sql p1/procedures.sql
run_sql p1/seed.sql

echo "== Проєкт №2: BooksDb (SQL Server)"
run_sql p2/schema.sql
run_sql p2/seed.sql

echo "== Логіни застосунків (найменші привілеї)"
run_sql security.sql

echo "== Проєкт №3: FeedbackDb (MongoDB)"
run_mongo p3/collections.js
run_mongo p3/seed.js

echo "== Готово. Перевірки: db/p1/checks.sql, db/p2/checks.sql, db/p3/checks.js"

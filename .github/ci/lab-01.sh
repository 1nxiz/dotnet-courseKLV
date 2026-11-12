#!/usr/bin/env bash
# Lab 01: перевірка скриптів баз даних (запускається після двох прогонів db/deploy.sh).
set -euo pipefail
SQL="docker exec -i mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $SA_PASSWORD -b -I"
ROOT=/tmp/library-db

echo ">> p1 checks.sql"
$SQL -i $ROOT/p1/checks.sql
echo ">> p2 checks.sql"
$SQL -i $ROOT/p2/checks.sql
echo ">> p3 checks.js"
docker exec -i mongo mongosh --quiet --file $ROOT/p3/checks.js

echo ">> idempotency: row counts after two deploys"
$SQL -h -1 -W -Q "SET NOCOUNT ON; SELECT 'Readers', COUNT(*) FROM LoansDb.dbo.Readers UNION ALL SELECT 'Loans', COUNT(*) FROM LoansDb.dbo.Loans UNION ALL SELECT 'LoanItems', COUNT(*) FROM LoansDb.dbo.LoanItems UNION ALL SELECT 'Books(p2)', COUNT(*) FROM BooksDb.dbo.Books UNION ALL SELECT 'BookGenres', COUNT(*) FROM BooksDb.dbo.BookGenres"
docker exec -i mongo mongosh --quiet --eval 'const d=db.getSiblingDB("FeedbackDb"); ["reviews","discussions","messages","bookRatings"].forEach(c=>print(c, d[c].countDocuments()))'

echo ">> cross-database foreign keys must be absent"
$SQL -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM LoansDb.sys.foreign_keys; SELECT COUNT(*) FROM BooksDb.sys.foreign_keys;"

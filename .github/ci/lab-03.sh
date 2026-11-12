#!/usr/bin/env bash
# Lab 03: Books.Api — міграції, сідінг, пагінація/фільтри, валідація 400, 409 дубліката ISBN, 404, eager/explicit.
set -euo pipefail
source "$CI_DIR/common.sh"
API=http://localhost:5102
start_service books Services/Books/Books.Api 5102

expect 200 "$API/api/books?page=2&pageSize=5&sortBy=price&sortDir=desc"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   totalCount',d['totalCount'],'items',len(d['items']),'hasPrevious',d['hasPrevious']);assert d['totalCount']>=8 and len(d['items'])<=5 and d['hasPrevious']"
expect 400 "$API/api/books?sortBy=hack"
expect 200 "$API/api/books?search=Clean&minPrice=10&maxPrice=100"
expect 200 $API/api/books/1
expect 404 $API/api/books/999999
expect 400 -X POST $API/api/books -H 'Content-Type: application/json' -d '{"title":"","isbn":"bad","price":-1,"authorId":0}'
grep -q '"errors"' /tmp/resp.json
expect 201 -X POST $API/api/books -H 'Content-Type: application/json' -d '{"title":"Refactoring","isbn":"978-0-13-475759-9","price":55,"authorId":4,"initialCopies":2,"description":"Improving the Design of Existing Code","pages":448,"genreIds":[4]}'
expect 409 -X POST $API/api/books -H 'Content-Type: application/json' -d '{"title":"Refactoring","isbn":"978-0-13-475759-9","price":55,"authorId":4}'
expect 409 -X POST $API/api/books/8/write-off -H 'Content-Type: application/json' -d '{"quantity":100}'
expect 200 -X PUT $API/api/books/2/price -H 'Content-Type: application/json' -d '{"price":33.5}'
expect 200 $API/api/books/genres/5
expect 200 "$API/api/books/genres/software-architecture/recent?since=2026-09-01"
expect 200 $API/api/books/authors/4/books
expect 200 $API/api/books/genres/statistics
echo ">> eager vs explicit loading in SQL log"
grep -c "Executed DbCommand" /tmp/svc-books.log || true

echo ">> restart: seeding must not duplicate"
pkill -f "Books.Api" || true; sleep 3
start_service books2 Services/Books/Books.Api 5102
expect 200 "$API/api/books?pageSize=100"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   totalCount after restart',d['totalCount']);assert d['totalCount']==9"
expect 200 $API/api/books/genres
python3 -c "import json;assert len(json.load(open('/tmp/resp.json')))==6"
echo "Lab 03 smoke checks passed"

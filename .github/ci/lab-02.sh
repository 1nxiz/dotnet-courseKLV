#!/usr/bin/env bash
# Lab 02: Loans.Api — CRUD, транзакційна операція, 404/409/400 у форматі ProblemDetails, Swagger.
set -euo pipefail
source "$CI_DIR/common.sh"
API=http://localhost:5101
start_service loans Services/Loans/Loans.Api 5101

expect 200 $API/swagger/v1/swagger.json
expect 200 "$API/api/loans?page=1&pageSize=2"
grep -q '"totalCount"' /tmp/resp.json
expect 404 $API/api/loans/999999
grep -q '"status":404' /tmp/resp.json
expect 400 -X POST $API/api/loans -H 'Content-Type: application/json' -d '{"readerId":0,"items":[]}'
expect 201 -X POST $API/api/readers -H 'Content-Type: application/json' \
  -d '{"email":"liam.obrien@example.com","fullName":"Liam O'"'"'Brien","cardNumber":"LIB-0100"}'
grep -q "O'Brien" /tmp/resp.json
expect 409 -X POST $API/api/readers -H 'Content-Type: application/json' \
  -d '{"email":"liam.obrien@example.com","fullName":"Dup","cardNumber":"LIB-0101"}'

# транзакція з відкатом: книга 8 має 0 примірників
expect 201 -X POST $API/api/loans -H 'Content-Type: application/json' -d '{"readerId":2,"items":[{"bookId":8,"quantity":1}]}'
LOAN=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['id'])")
expect 409 -X POST $API/api/loans/$LOAN/confirm
expect 200 $API/api/loans/$LOAN
grep -q '"status":"Submitted"' /tmp/resp.json && echo "   OK: status rolled back to Submitted"

# повний ланцюжок
expect 201 -X POST $API/api/loans -H 'Content-Type: application/json' -d '{"readerId":1,"items":[{"bookId":1,"quantity":1},{"bookId":7,"quantity":2}]}'
LOAN=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['id'])")
TOTAL=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['total'])")
RV=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['rowVersion'])")
expect 200 -X PUT $API/api/loans/$LOAN/details -H 'Content-Type: application/json' -d "{\"deliveryType\":\"Courier\",\"deliveryAddress\":\"Kyiv\",\"rowVersion\":\"$RV\"}"
expect 409 -X PUT $API/api/loans/$LOAN/details -H 'Content-Type: application/json' -d "{\"deliveryType\":\"Pickup\",\"rowVersion\":\"$RV\"}"
expect 204 -X POST $API/api/loans/$LOAN/confirm
expect 409 -X POST $API/api/loans/$LOAN/confirm
expect 409 -X POST $API/api/loans/$LOAN/issue
expect 200 -X POST $API/api/loans/$LOAN/pay -H 'Content-Type: application/json' -H 'Idempotency-Key: ci-1' -d "{\"amount\":$TOTAL,\"method\":\"Card\"}"
expect 200 -X POST $API/api/loans/$LOAN/pay -H 'Content-Type: application/json' -H 'Idempotency-Key: ci-1' -d "{\"amount\":$TOTAL,\"method\":\"Card\"}"
expect 204 -X POST $API/api/loans/$LOAN/issue
expect 409 -X POST $API/api/loans/$LOAN/cancel
expect 204 -X POST $API/api/loans/$LOAN/return
expect 204 -X DELETE $API/api/loans/$LOAN
expect 404 $API/api/loans/$LOAN
expect 200 $API/api/loans/books/1
echo "Lab 02 smoke checks passed"

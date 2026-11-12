#!/usr/bin/env bash
# Lab 08: RabbitMQ topology, integration events, idempotent consumers, retries, DLQ, trace propagation.
set -euo pipefail
source "$CI_DIR/common.sh"
GW=http://localhost:5000
MQ="http://guest:guest@localhost:15672/api"
SD=(services__loans-api__http__0=http://localhost:5101 services__books-api__http__0=http://localhost:5102
    services__feedback-api__http__0=http://localhost:5103 services__aggregator__http__0=http://localhost:5090
    services__loans-api__grpc__0=http://localhost:5111 services__books-api__grpc__0=http://localhost:5112
    services__feedback-api__grpc__0=http://localhost:5113)
sql() { docker exec mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD" -h -1 -W -Q "SET NOCOUNT ON; $1" | tr -d '\r' | head -1; }
json() { python3 -c "import json,sys;d=json.load(open('/tmp/resp.json'));print($1)"; }
# wait_for "<description>" "<command printing a value>" "<expected>" [timeout s]
wait_for() {
  local v=""; for i in $(seq 1 ${4:-30}); do v=$(eval "$2" 2>/dev/null || true); [ "$v" = "$3" ] && { echo "   OK $1: $v"; return 0; }; sleep 1; done
  echo "!! $1: expected '$3', got '$v'"; exit 1
}
publish() {  # publish <exchange> <routing key> <type> <message id> <payload json>
  python3 - "$@" <<'PY' > /tmp/pub.json
import json,sys
ex,rk,typ,mid,payload=sys.argv[1:6]
print(json.dumps({"properties":{"message_id":mid,"type":typ,"delivery_mode":2,"content_type":"application/json",
  "headers":{"x-correlation-id":"ci-manual"}},"routing_key":rk,"payload":payload,"payload_encoding":"string"}))
PY
  curl -s -u guest:guest -H 'Content-Type: application/json' -d @/tmp/pub.json "http://localhost:15672/api/exchanges/%2F/$1/publish"; echo
}

start_service loans Services/Loans/Loans.Api 5101
start_service books Services/Books/Books.Api 5102
start_service feedback Services/Feedback/Feedback.WebApi 5103
start_service aggregator Platform.Aggregator 5090 "${SD[@]}"
start_service gateway Platform.ApiGateway 5000 "${SD[@]}"
sleep 3

echo ">> topology declared in code"
curl -s "$MQ/exchanges/%2F" | python3 -c "import json,sys;n=[e['name'] for e in json.load(sys.stdin)];print('   exchanges',[x for x in n if not x.startswith('amq') and x]);assert {'books-events','loans-events','platform.dlx'}<=set(n)"
for n in loans.books-sync feedback.books-sync books.loans-sync; do
  wait_for "consumers on $n" "curl -s $MQ/queues/%2F/$n | python3 -c 'import json,sys;print(min(json.load(sys.stdin)[\"consumers\"],1))'" "1" 40
done
curl -s "$MQ/queues/%2F" | python3 -c "
import json,sys;q={x['name']:x for x in json.load(sys.stdin)}
for n in ['loans.books-sync','feedback.books-sync','books.loans-sync']:
  print('   queue',n,'durable',q[n]['durable'],'dlx',q[n]['arguments'].get('x-dead-letter-exchange'),'| dlq', n+'.dlq' in q)
  assert q[n]['durable'] and n+'.dlq' in q and q[n]['arguments'].get('x-dead-letter-exchange')=='platform.dlx'"
curl -s "$MQ/queues/%2F/loans.books-sync/bindings" | python3 -c "import json,sys;print('   bindings',[b['routing_key'] for b in json.load(sys.stdin) if b['source']])"
for p in 5101 5102 5103; do curl -s http://localhost:$p/health/ready | grep -q '"messaging"' && echo "   :$p readiness includes messaging"; done

echo ">> price change in Books -> Loans copy updated, loan snapshots unchanged"
SNAP=$(sql "SELECT CAST(SUM(UnitPrice) AS VARCHAR(30)) FROM LoansDb.dbo.LoanItems WHERE BookId = 3")
expect 200 $GW/api/books/3
NEW=$(json "round(float(d['price'])+2.25,2)")
expect 200 -X PUT $GW/api/books/3/price -H 'Content-Type: application/json' -d "{\"price\":$NEW}"
wait_for "loans copy price of book 3" "curl -s $GW/api/loans/books/3 | python3 -c 'import json,sys;print(round(float(json.load(sys.stdin)[\"price\"]),2))'" "$NEW"
test "$SNAP" = "$(sql "SELECT CAST(SUM(UnitPrice) AS VARCHAR(30)) FROM LoansDb.dbo.LoanItems WHERE BookId = 3")" && echo "   LoanItems.UnitPrice snapshots unchanged ($SNAP)"
echo "   ProcessedEvents in LoansDb: $(sql 'SELECT COUNT(*) FROM LoansDb.dbo.ProcessedEvents')"

echo ">> rename in Books -> Loans copy and Feedback bookTitle; one trace HTTP -> publish -> consume"
expect 200 $GW/api/books/4
AUTHOR=$(json "d['authorId']"); YEAR=$(json "d.get('publishedYear') or 'null'")
TITLE="Renamed in CI $(date +%s)"
expect 200 -X PUT $GW/api/books/4 -H 'Content-Type: application/json' -H 'X-Correlation-Id: ci8-rename' -d "{\"title\":\"$TITLE\",\"authorId\":$AUTHOR,\"publishedYear\":$YEAR}"
wait_for "loans copy title of book 4" "curl -s $GW/api/loans/books/4 | python3 -c 'import json,sys;print(json.load(sys.stdin)[\"title\"])'" "$TITLE"
wait_for "feedback reviews bookTitle" "docker exec mongo mongosh --quiet FeedbackDb --eval 'print(db.reviews.distinct(\"bookTitle\",{bookId:4}).join(\"|\"))'" "$TITLE"
curl -s "$GW/api/reviews?bookId=4" | python3 -c "import json,sys;i=json.load(sys.stdin)['items'];print('   API reviews titles',{x['bookTitle'] for x in i})"
TR=$(grep '"@mt":"Published {EventType}' /tmp/svc-books.log | grep '"EventType":"BookRenamedEvent"' | tail -1 | python3 -c "import json,sys;print(json.loads(sys.stdin.read())['@tr'])")
echo "   trace of rename: $TR"
for s in loans feedback; do grep "\"@tr\":\"$TR\"" /tmp/svc-$s.log | grep -q 'Received {EventType}' && echo "   $s consumed in the same trace" || { echo "!! $s: no consumer span in trace $TR"; exit 1; }; done
grep '"CorrelationId":"ci8-rename"' /tmp/svc-feedback.log | grep -q 'Received' && echo "   CorrelationId ci8-rename reached feedback"

echo ">> duplicate delivery of the same EventId is skipped"
EID=$(python3 -c "import uuid;print(uuid.uuid4())")
T5=$(curl -s $GW/api/loans/books/5 | python3 -c "import json,sys;print(json.load(sys.stdin)['title'])")
P='{"bookId":5,"title":"'"$T5"'","oldPrice":10,"newPrice":55.5,"eventId":"'$EID'","occurredAt":"'$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)'","schemaVersion":1}'
publish books-events book.price_changed BookPriceChangedEvent "$EID" "$P"
publish books-events book.price_changed BookPriceChangedEvent "$EID" "$P"
wait_for "copy price of book 5" "curl -s $GW/api/loans/books/5 | python3 -c 'import json,sys;print(round(float(json.load(sys.stdin)[\"price\"]),2))'" "55.5"
wait_for "ProcessedEvents rows for $EID" "sql \"SELECT COUNT(*) FROM LoansDb.dbo.ProcessedEvents WHERE EventId = '$EID'\"" "1"
wait_for "duplicate logged" "grep '\"@mt\":\"Duplicate event {EventId}' /tmp/svc-loans.log | grep -c '$EID'" "1"

echo ">> malformed JSON -> DLQ (no retries)"
publish books-events book.price_changed BookPriceChangedEvent "$(python3 -c 'import uuid;print(uuid.uuid4())')" '{not json'
wait_for "messages in loans.books-sync.dlq" "curl -s $MQ/queues/%2F/loans.books-sync.dlq | python3 -c 'import json,sys;print(json.load(sys.stdin)[\"messages\"])'" "1"
grep -q '"@mt":"Permanent failure of {EventType}' /tmp/svc-loans.log && echo "   permanent failure logged"
publish books-events book.price_changed BookPriceChangedEvent "$(python3 -c 'import uuid;print(uuid.uuid4())')" '{"bookId":5,"title":"x","oldPrice":1,"newPrice":-3}'
wait_for "business-invalid event in DLQ" "curl -s $MQ/queues/%2F/loans.books-sync.dlq | python3 -c 'import json,sys;print(json.load(sys.stdin)[\"messages\"])'" "2"

echo ">> transient failure: SQL Server down during processing -> retries with x-retry-count, then success"
EID2=$(python3 -c "import uuid;print(uuid.uuid4())")
P2='{"bookId":5,"title":"'"$T5"'","oldPrice":55.5,"newPrice":66.6,"eventId":"'$EID2'","occurredAt":"'$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)'","schemaVersion":1}'
docker stop mssql >/dev/null
publish books-events book.price_changed BookPriceChangedEvent "$EID2" "$P2"
sleep 4
docker start mssql >/dev/null
wait_for "copy price after SQL restart" "curl -s http://localhost:5101/api/loans/books/5 | python3 -c 'import json,sys;print(round(float(json.load(sys.stdin)[\"price\"]),2))'" "66.6" 90
grep '"@mt":"{EventType} {EventId} failed: {Reason}; retry {Attempt}/{MaxRetries}"' /tmp/svc-loans.log | grep "$EID2" | head -2 | cut -c1-230

echo ">> loans-api stopped -> events wait in the queue -> processed after restart"
pkill -f "Loans.Api" || true; sleep 2
expect 200 $GW/api/books/6
NEW6=$(json "round(float(d['price'])+1.1,2)")
expect 200 -X PUT http://localhost:5102/api/books/6/price -H 'Content-Type: application/json' -d "{\"price\":$NEW6}"
wait_for "message waiting in loans.books-sync" "curl -s $MQ/queues/%2F/loans.books-sync | python3 -c 'import json,sys;print(json.load(sys.stdin)[\"messages\"])'" "1"
start_service loans Services/Loans/Loans.Api 5101
wait_for "copy price of book 6 after restart" "curl -s http://localhost:5101/api/loans/books/6 | python3 -c 'import json,sys;print(round(float(json.load(sys.stdin)[\"price\"]),2))'" "$NEW6"

echo ">> Loans publishes loan.status_changed -> Books counts TimesLoaned"
expect 200 http://localhost:5101/api/loans/3
B=$(json "d['items'][0]['bookId']"); Q=$(json "d['items'][0]['quantity']")
BEFORE=$(curl -s http://localhost:5102/api/books/$B | python3 -c "import json,sys;print(json.load(sys.stdin)['timesLoaned'])")
expect 204 -X POST http://localhost:5101/api/loans/3/issue
wait_for "TimesLoaned of book $B" "curl -s http://localhost:5102/api/books/$B | python3 -c 'import json,sys;print(json.load(sys.stdin)[\"timesLoaned\"])'" "$((BEFORE+Q))"
echo "   books ProcessedEvents: $(sql 'SELECT COUNT(*) FROM BooksDb.dbo.ProcessedEvents')"

pkill -f "Platform.ApiGateway" || true; pkill -f "Platform.Aggregator" || true
pkill -f "Loans.Api" || true; pkill -f "Books.Api" || true; pkill -f "Feedback.WebApi" || true
sleep 2

echo ">> Aspire AppHost with RabbitMQ (volume + management plugin)"
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run -c Release --no-build --project Platform.AppHost --launch-profile http > /tmp/svc-apphost.log 2>&1 &
for i in $(seq 1 120); do
  if curl -s -o /dev/null -w "%{http_code}" "$GW/api/aggregator/loans/1/full" | grep -q 200; then break; fi
  sleep 5
done
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
expect 200 $GW/api/books/2
NEW2=$(json "round(float(d['price'])+0.5,2)")
expect 200 -X PUT $GW/api/books/2/price -H 'Content-Type: application/json' -d "{\"price\":$NEW2}"
wait_for "Aspire: loans copy price of book 2" "curl -s $GW/api/loans/books/2 | python3 -c 'import json,sys;print(round(float(json.load(sys.stdin)[\"price\"]),2))'" "$NEW2" 60
docker ps --format '{{.Names}} {{.Image}} {{.Ports}}' | grep -i rabbit
echo "Lab 08 smoke checks passed"

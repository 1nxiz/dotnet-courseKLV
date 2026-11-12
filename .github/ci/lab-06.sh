#!/usr/bin/env bash
# Lab 06: L1/L2 cache (IMemoryCache + Redis), gRPC services, gRPC Aggregator, cache of aggregated response.
set -euo pipefail
source "$CI_DIR/common.sh"
GW=http://localhost:5000
SD=(services__loans-api__http__0=http://localhost:5101 services__books-api__http__0=http://localhost:5102
    services__feedback-api__http__0=http://localhost:5103 services__aggregator__http__0=http://localhost:5090
    services__loans-api__grpc__0=http://localhost:5111 services__books-api__grpc__0=http://localhost:5112
    services__feedback-api__grpc__0=http://localhost:5113)
GRPCURL="docker run --rm --network host fullstorydev/grpcurl:latest -plaintext"

start_service loans Services/Loans/Loans.Api 5101
start_service books Services/Books/Books.Api 5102
start_service feedback Services/Feedback/Feedback.WebApi 5103
start_service aggregator Platform.Aggregator 5090 "${SD[@]}"
start_service gateway Platform.ApiGateway 5000 "${SD[@]}"

echo ">> L1/L2 cache in books-api: second read is a memory hit, write invalidates"
expect 200 $GW/api/books/3
expect 200 $GW/api/books/3
sleep 1
grep '"CacheKey":"books:book:3"' /tmp/svc-books.log | grep -q '"CacheLevel":"memory"' && echo "   memory hit logged"
docker exec redis redis-cli --scan --pattern '*books:book:3*' | tee /tmp/keys.txt; test -s /tmp/keys.txt && echo "   key is in Redis (L2)"
OLD=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['price'])")
NEW=$(python3 -c "print(round(float('$OLD')+1.5,2))")
expect 200 -X PUT $GW/api/books/3/price -H 'Content-Type: application/json' -d "{\"price\":$NEW}"
expect 200 $GW/api/books/3
python3 -c "import json;p=json.load(open('/tmp/resp.json'))['price'];print('   price after PUT',p);assert abs(float(p)-$NEW)<0.001"
grep -q '"Reason":"BookPriceChanged"' /tmp/svc-books.log && echo "   invalidation logged"
curl -s http://localhost:5102/health && echo

echo ">> cache in loans-api and feedback-api"
expect 200 $GW/api/loans/1; expect 200 $GW/api/loans/1
grep '"CacheKey":"loans:loan:1"' /tmp/svc-loans.log | grep -q memory && echo "   loans memory hit"
expect 200 $GW/api/reviews/66a000000000000000000003; expect 200 $GW/api/reviews/66a000000000000000000003
grep '"CacheKey":"feedback:review:66a000000000000000000003"' /tmp/svc-feedback.log | grep -q memory && echo "   feedback memory hit"

echo ">> gRPC contracts via reflection + RpcException codes"
$GRPCURL localhost:5112 list
$GRPCURL localhost:5111 list
$GRPCURL localhost:5113 list
$GRPCURL -d '{"id":3}' localhost:5112 library.books.v1.BooksCatalog/GetBook
$GRPCURL -d '{"id":1}' localhost:5111 library.loans.v1.LoansService/GetLoan
$GRPCURL -d '{"book_ids":[3,4],"latest_per_book":2}' localhost:5113 library.feedback.v1.FeedbackService/GetBooksReviews
set +e
$GRPCURL -d '{"id":999999}' localhost:5112 library.books.v1.BooksCatalog/GetBook > /tmp/g.txt 2>&1; cat /tmp/g.txt
$GRPCURL -d '{"id":0}' localhost:5111 library.loans.v1.LoansService/GetLoan > /tmp/g2.txt 2>&1; cat /tmp/g2.txt
$GRPCURL -d '{"id":"bad"}' localhost:5113 library.feedback.v1.FeedbackService/GetReview > /tmp/g3.txt 2>&1; cat /tmp/g3.txt
set -e
grep -q "NotFound" /tmp/g.txt && grep -q "InvalidArgument" /tmp/g2.txt && grep -q "InvalidArgument" /tmp/g3.txt && echo "   NotFound / InvalidArgument OK"
grep -q '"GrpcMethod":"/library.books.v1.BooksCatalog/GetBook"' /tmp/svc-books.log && echo "   server interceptor logged gRPC calls"

echo ">> Aggregator over gRPC, CorrelationId in gRPC metadata, Redis cache of the composite"
docker exec redis redis-cli --scan --pattern '*aggregator:loan:1:full*' | xargs -r docker exec redis redis-cli DEL >/dev/null || true
CID="ci6-$(date +%s)"
expect 200 -H "X-Correlation-Id: $CID" "$GW/api/aggregator/loans/1/full"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   transport',d['transport'],'fromCache',d['fromCache'],'books',len(d['books']),'warnings',d['warnings']);assert d['transport']=='grpc' and not d['fromCache'] and d['warnings']==[] and all('catalog' in b for b in d['books'])"
sleep 1
for s in loans books feedback; do grep -q "$CID" /tmp/svc-$s.log && echo "   $s got $CID via gRPC metadata"; done
grep -q '"GrpcMethod":"/library.loans.v1.LoansService/GetLoan"' /tmp/svc-loans.log
expect 200 "$GW/api/aggregator/loans/1/full"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   second call fromCache',d['fromCache']);assert d['fromCache']"

echo ">> latency: gRPC vs HTTP (cache bypassed, 20 calls each after warm-up)"
for t in grpc http; do curl -s -o /dev/null "$GW/api/aggregator/loans/1/full?transport=$t&cache=false"; done
for t in grpc http; do
  total=0
  for i in $(seq 1 20); do
    ms=$(curl -s -o /dev/null -w "%{time_total}" "http://localhost:5090/api/aggregator/loans/1/full?transport=$t&cache=false")
    total=$(python3 -c "print($total+$ms)")
  done
  python3 -c "print('   $t avg', round($total/20*1000,1), 'ms')"
done
grep '"Transport":"grpc"' /tmp/svc-aggregator.log | tail -1 | cut -c1-200

echo ">> Redis down: services keep answering from DB"
docker stop redis >/dev/null
expect 200 $GW/api/books/5
expect 200 $GW/api/books/5
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
docker start redis >/dev/null
grep -q "L2 is bypassed" /tmp/svc-books.log && echo "   degradation logged"

echo ">> partial failure over gRPC: books-api down"
pkill -f "Books.Api" || true
sleep 2
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   warnings',d['warnings']);assert any('books-api' in w for w in d['warnings'])"
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
docker exec redis redis-cli --scan --pattern '*aggregator:loan:1:full*' | wc -l
pkill -f "Platform.ApiGateway" || true; pkill -f "Platform.Aggregator" || true
pkill -f "Loans.Api" || true; pkill -f "Feedback.WebApi" || true
sleep 2

echo ">> whole platform via Aspire AppHost (SQL, Mongo, Redis containers + gRPC endpoints)"
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run -c Release --no-build --project Platform.AppHost --launch-profile http > /tmp/svc-apphost.log 2>&1 &
for i in $(seq 1 120); do
  if curl -s -o /dev/null -w "%{http_code}" "$GW/api/aggregator/loans/1/full" | grep -q 200; then break; fi
  sleep 5
done
expect 200 "$GW/api/aggregator/loans/1/full?cache=false"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   via Aspire:',d['transport'],'books',len(d['books']),'warnings',d['warnings']);assert d['transport']=='grpc' and d['warnings']==[]"
expect 200 "$GW/api/books/1"
docker ps --format '{{.Names}} {{.Image}}' | grep -Ei "redis|sql|mongo"
echo "Lab 06 smoke checks passed"

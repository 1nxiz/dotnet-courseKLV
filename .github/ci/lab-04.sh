#!/usr/bin/env bash
# Lab 04: Feedback.WebApi — Clean Architecture + CQRS + MongoDB.
set -euo pipefail
source "$CI_DIR/common.sh"
API=http://localhost:5103
start_service feedback Services/Feedback/Feedback.WebApi 5103

expect 200 "$API/api/reviews?bookId=4&page=1&pageSize=10"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));print('   totalCount',d['totalCount']);assert d['totalCount']>=2"
expect 200 "$API/api/reviews?search=%D1%83%D0%B7%D0%B3%D0%BE%D0%B4%D0%B6%D0%B5%D0%BD%D1%96%D1%81%D1%82%D1%8C"
expect 200 $API/api/reviews/66a000000000000000000003
grep -q '"quote"' /tmp/resp.json && echo "!! quote leaked into DTO" || true
expect 400 $API/api/reviews/not-an-object-id
expect 404 $API/api/reviews/66a0000000000000000000ff
expect 400 -X POST $API/api/reviews -H 'Content-Type: application/json' -d '{"bookId":2,"bookTitle":"T","authorId":"u-x","authorName":"X","rating":10,"text":"?"}'
grep -q "Rating" /tmp/resp.json
if grep -q "Handled CreateReviewCommand" /tmp/svc-feedback.log; then echo "!! handler executed for invalid command"; exit 1; fi
echo "   OK: handler was not executed for rating=10"
expect 201 -X POST $API/api/reviews -H 'Content-Type: application/json' -d '{"bookId":2,"bookTitle":"Тигролови","authorId":"u-ci","authorName":"CI","rating":5,"text":"Класика пригод!"}'
ID=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['id'])")
expect 400 -X POST $API/api/reviews -H 'Content-Type: application/json' -d '{"bookId":2,"bookTitle":"Тигролови","authorId":"u-ci","authorName":"CI","rating":4,"text":"Вдруге"}'
expect 201 -X POST $API/api/reviews/$ID/comments -H 'Content-Type: application/json' -d '{"authorId":"u-2","authorName":"Андрій","text":"Згоден"}'
expect 409 -X PUT $API/api/reviews/$ID -H 'Content-Type: application/json' -H 'If-Match: "0"' -d '{"rating":4,"text":"Оновлено"}'
expect 200 -X PUT $API/api/reviews/$ID -H 'Content-Type: application/json' -H 'If-Match: "1"' -d '{"rating":4,"text":"Оновлено"}'
# оновлення документа з полем quote (гнучка схема) не губить поле
expect 201 -X POST $API/api/reviews/66a000000000000000000003/comments -H 'Content-Type: application/json' -d '{"authorId":"u-9","authorName":"Тест","text":"ok"}'
docker exec -i mongo mongosh --quiet --eval 'const r=db.getSiblingDB("FeedbackDb").reviews.findOne({_id:ObjectId("66a000000000000000000003")}); printjson(r); if(!r.quote) throw "quote lost"; if(typeof r.rating!=="number") throw "rating"; Object.keys(r).forEach(k=>{ if(k!=="_id" && k.startsWith("_")) throw "private field leaked "+k; })'
expect 201 -X POST $API/api/reviews/discussions -H 'Content-Type: application/json' -d "{\"bookId\":2,\"bookTitle\":\"Тигролови\",\"title\":\"Хто читав?\",\"authorId\":\"u-ci\",\"authorName\":\"CI\",\"tags\":[\"adventure\"],\"reviewId\":\"$ID\"}"
DID=$(python3 -c "import json;print(json.load(open('/tmp/resp.json'))['id'])")
for i in 1 2 3 4; do expect 201 -X POST $API/api/reviews/discussions/$DID/messages -H 'Content-Type: application/json' -d "{\"authorId\":\"u-ci\",\"authorName\":\"CI\",\"text\":\"msg $i\"}"; done
expect 200 "$API/api/reviews/discussions/$DID?pageSize=2"
python3 -c "import json;d=json.load(open('/tmp/resp.json'));m=d['messages'];print('   lastMessages',len(d['discussion']['lastMessages']),'page',len(m['items']),'next',m['nextCursor']);assert len(d['discussion']['lastMessages'])==3 and len(m['items'])==2 and m['nextCursor']"
expect 204 -X DELETE $API/api/reviews/$ID
docker exec -i mongo mongosh --quiet --eval 'printjson(db.getSiblingDB("FeedbackDb").reviews.getIndexes().map(i=>i.name))'
echo "Lab 04 smoke checks passed"

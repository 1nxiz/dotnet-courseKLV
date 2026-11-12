#!/usr/bin/env bash
# Допоміжні функції smoke-сценаріїв.
start_service() {   # start_service <name> <project> <port> [extra env...]
  local name=$1 project=$2 port=$3; shift 3
  echo ">> starting $name on :$port"
  env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:$port" "$@" \
    dotnet run --no-build -c Release --project "$project" > /tmp/svc-$name.log 2>&1 &
  for i in $(seq 1 90); do
    if curl -s -o /dev/null "http://localhost:$port/swagger/v1/swagger.json" || curl -s -o /dev/null "http://localhost:$port/health"; then
      echo "   $name is up"; return 0; fi
    sleep 2
  done
  echo "!! $name did not start"; tail -100 /tmp/svc-$name.log; return 1
}
expect() {   # expect <expected-status> <curl args...>  -> body in /tmp/resp.json
  local want=$1; shift
  local got
  got=$(curl -s -o /tmp/resp.json -w "%{http_code}" "$@")
  echo "   [$got] $*"
  if [ "$got" != "$want" ]; then echo "!! expected $want, got $got"; cat /tmp/resp.json; echo; return 1; fi
}

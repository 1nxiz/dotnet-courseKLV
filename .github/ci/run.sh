#!/usr/bin/env bash
# Загальний сценарій перевірки гілки лаби (запускається з кореня перевірюваної гілки).
set -euo pipefail
LAB="${1:-01}"
CI_DIR="$(cd "$(dirname "$0")" && pwd)"
export CI_DIR LAB

echo "::group::Repository tree"
find . -path ./.git -prune -o -type f -print | grep -v '/bin/' | grep -v '/obj/' | sort | head -400
echo "::endgroup::"

if [ -f db/deploy.sh ]; then
  echo "::group::Deploy databases (1st run)"
  bash db/deploy.sh
  echo "::endgroup::"
  echo "::group::Deploy databases (2nd run — idempotency)"
  bash db/deploy.sh
  echo "::endgroup::"
fi

SLN=$(ls *.sln 2>/dev/null | head -1 || true)
if [ -n "$SLN" ]; then
  echo "::group::dotnet build $SLN"
  dotnet build "$SLN" -c Release
  echo "::endgroup::"
  if grep -q "Tests" "$SLN"; then
    echo "::group::dotnet test"
    dotnet test "$SLN" -c Release --no-build --logger "console;verbosity=normal"
    echo "::endgroup::"
  fi
fi

if [ -f "$CI_DIR/lab-$LAB.sh" ]; then
  echo "::group::Lab $LAB smoke checks"
  bash "$CI_DIR/lab-$LAB.sh"
  echo "::endgroup::"
fi
echo "ALL CHECKS PASSED for lab $LAB"

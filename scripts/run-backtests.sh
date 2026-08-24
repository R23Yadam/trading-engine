#!/usr/bin/env bash
# Run many deterministic fast-backtest sessions and write HTML reports under ./reports/
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
mkdir -p reports
: "${BACKTEST_DURATION_SECONDS:=120}"
: "${BACKTEST_SEEDS:=1 2 3 4 5 6 7 8 9 10}"

for seed in $BACKTEST_SEEDS; do
  echo "=== Backtest seed=$seed ==="
  dotnet run --project "$ROOT/TradingSystem.Runner" -- \
    --backtest \
    --duration "$BACKTEST_DURATION_SECONDS" \
    --seed "$seed" \
    --report-html "$ROOT/reports/backtest-seed-${seed}.html"
done

echo "Done. Open reports/*.html in a browser or capture screenshots."

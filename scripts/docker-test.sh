#!/usr/bin/env bash
set -euo pipefail

#
# Runs integration tests against Docker Compose services.
# Spins up mosquitto + mqtt-translator, runs the tests, then tears down.
#
# Usage:
#   ./scripts/docker-test.sh
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$REPO_ROOT"

echo "==> Starting Docker Compose services..."
# Initialize dvm if available (not present in CI environments)
export DVM_DIR="${DVM_DIR:-$HOME/.dvm}"
if [[ -s "$DVM_DIR/dvm.sh" ]]; then
    set +u
    # shellcheck source=/dev/null
    source "$DVM_DIR/dvm.sh"
    dvm use 29.3.0
    set -u
fi
docker compose up -d --build

echo "==> Waiting for services to be ready..."
sleep 5

echo "==> Running integration tests..."
TEST_EXIT=0
dotnet test "$REPO_ROOT/src/mqtttranslator.tests/MszCool.MqttTopicsTranslator.Tests.csproj" \
    --logger 'console;verbosity=detailed' || TEST_EXIT=$?

echo "==> Tearing down Docker Compose services..."
docker compose down

if [[ $TEST_EXIT -ne 0 ]]; then
    echo "==> Tests FAILED (exit code: $TEST_EXIT)"
else
    echo "==> Tests PASSED"
fi

exit $TEST_EXIT

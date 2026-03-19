#!/usr/bin/env bash
set -euo pipefail

#
# Builds and pushes the Docker image to Docker Hub with a tag in the format
# yyyyMMdd.increment, where increment is auto-derived from existing tags.
#
# Usage:
#   ./scripts/docker-publish.sh [--force] <dockerhub-repo>
#
# Options:
#   --force   Skip running integration tests before publishing.
#
# Example:
#   ./scripts/docker-publish.sh mszcool/mqtt-topics-translator
#   ./scripts/docker-publish.sh --force mszcool/mqtt-topics-translator
#
# Prerequisites:
#   - docker login has been done (or credentials are configured)
#   - dvm / Docker CLI available
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Parse --force flag
FORCE=false
if [[ "${1:-}" == "--force" ]]; then
    FORCE=true
    shift
fi

# Initialize dvm (shell function, not a binary)
export DVM_DIR="${DVM_DIR:-$HOME/.dvm}"
# dvm.sh has unguarded variables, so disable nounset while sourcing
set +u
# shellcheck source=/dev/null
[ -s "$DVM_DIR/dvm.sh" ] && source "$DVM_DIR/dvm.sh"
dvm use 29.3.0
set -u

# Validate that we are on a release branch
CURRENT_BRANCH="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"
if [[ ! "$CURRENT_BRANCH" =~ ^release-[0-9]{8}$ ]]; then
    echo "ERROR: Publishing is only allowed from a 'release-<yyyyMMdd>' branch."
    echo "       Current branch: ${CURRENT_BRANCH}"
    exit 1
fi

# Run integration tests unless --force was specified
if [[ "$FORCE" == "true" ]]; then
    echo "==> --force specified, skipping tests."
else
    echo "==> Running integration tests before publishing..."
    "$SCRIPT_DIR/docker-test.sh"
    echo "==> Tests passed. Proceeding with publish."
fi

if [[ $# -lt 1 ]]; then
    echo "Usage: $0 [--force] <dockerhub-repo>"
    echo "  e.g. $0 mszcool/mqtt-topics-translator"
    exit 1
fi

DOCKER_REPO="$1"
DATE_PREFIX="$(date -u +%Y%m%d)"

echo "==> Querying existing tags for ${DOCKER_REPO} with prefix ${DATE_PREFIX}..."

# Try the Docker Hub v2 API to find existing tags for today.
# Works for public repos without auth; for private repos the user must be
# logged in and we fall back to increment 1.
MAX_INCREMENT=0
PAGE_URL="https://hub.docker.com/v2/repositories/${DOCKER_REPO}/tags/?page_size=100&name=${DATE_PREFIX}"

while [[ -n "$PAGE_URL" && "$PAGE_URL" != "null" ]]; do
    RESPONSE=$(curl -sf "$PAGE_URL" 2>/dev/null || echo "")
    if [[ -z "$RESPONSE" ]]; then
        echo "    Could not query Docker Hub API (private repo or not yet created). Starting at .1"
        break
    fi

    # Extract tags matching yyyyMMdd.N and find the highest N
    TAGS=$(echo "$RESPONSE" | grep -oP "\"name\":\\s*\"${DATE_PREFIX}\\.\\K[0-9]+" || true)
    for t in $TAGS; do
        if (( t > MAX_INCREMENT )); then
            MAX_INCREMENT=$t
        fi
    done

    # Follow pagination
    PAGE_URL=$(echo "$RESPONSE" | grep -oP '"next":\s*"\K[^"]+' || echo "")
done

NEXT_INCREMENT=$((MAX_INCREMENT + 1))
TAG="${DATE_PREFIX}.${NEXT_INCREMENT}"

echo "==> Next tag: ${DOCKER_REPO}:${TAG}"

echo "==> Building Docker image..."
cd "$REPO_ROOT"
docker build -t "${DOCKER_REPO}:${TAG}" -t "${DOCKER_REPO}:latest" -f Dockerfile .

echo "==> Pushing ${DOCKER_REPO}:${TAG}..."
docker push "${DOCKER_REPO}:${TAG}"

echo "==> Pushing ${DOCKER_REPO}:latest..."
docker push "${DOCKER_REPO}:latest"

echo "==> Done! Published ${DOCKER_REPO}:${TAG}"

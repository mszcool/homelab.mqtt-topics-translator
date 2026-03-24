#!/usr/bin/env bash
set -euo pipefail

#
# Builds and pushes the Docker image to Docker Hub.
#
# The image tag is determined in this order:
#   1. Explicit --tag TAG argument
#   2. Git tag on HEAD matching yyyyMMdd.N pattern
#   3. Auto-computed next tag from existing git tags for today's date
#
# Usage:
#   ./scripts/docker-publish.sh [--force] [--tag TAG] <dockerhub-repo>
#
# Options:
#   --force     Skip running integration tests before publishing.
#   --tag TAG   Use TAG as the image tag (e.g. 20260324.3).
#
# Examples:
#   ./scripts/docker-publish.sh mszcool/mqtt-topics-translator
#   ./scripts/docker-publish.sh --force mszcool/mqtt-topics-translator
#   ./scripts/docker-publish.sh --tag 20260324.0 mszcool/mqtt-topics-translator
#
# Prerequisites:
#   - docker login has been done (or credentials are configured)
#   - Docker CLI available (dvm is used if installed)
#

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Parse flags
FORCE=false
EXPLICIT_TAG=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --force)
            FORCE=true
            shift
            ;;
        --tag)
            EXPLICIT_TAG="$2"
            shift 2
            ;;
        *)
            break
            ;;
    esac
done

# Initialize dvm if available (not present in CI environments)
export DVM_DIR="${DVM_DIR:-$HOME/.dvm}"
if [[ -s "$DVM_DIR/dvm.sh" ]]; then
    set +u
    # shellcheck source=/dev/null
    source "$DVM_DIR/dvm.sh"
    dvm use 29.3.0
    set -u
fi

# Validate that we are on the release branch
CURRENT_BRANCH="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"
if [[ "$CURRENT_BRANCH" != "release" ]]; then
    echo "ERROR: Publishing is only allowed from the 'release' branch."
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
    echo "Usage: $0 [--force] [--tag TAG] <dockerhub-repo>"
    echo "  e.g. $0 mszcool/mqtt-topics-translator"
    exit 1
fi

DOCKER_REPO="$1"

# Determine the release tag
if [[ -n "$EXPLICIT_TAG" ]]; then
    TAG="$EXPLICIT_TAG"
    echo "==> Using explicit tag: ${TAG}"
else
    # Try to read a git tag on HEAD matching yyyyMMdd.N
    TAG="$(git -C "$REPO_ROOT" describe --tags --match "????????.*" --exact-match HEAD 2>/dev/null || echo "")"

    if [[ -n "$TAG" ]]; then
        echo "==> Found git tag on HEAD: ${TAG}"
    else
        # Auto-compute next tag from existing git tags for today
        DATE_PREFIX="$(date -u +%Y%m%d)"
        MAX=-1
        for t in $(git -C "$REPO_ROOT" tag -l "${DATE_PREFIX}.*"); do
            N="${t#${DATE_PREFIX}.}"
            if [[ "$N" =~ ^[0-9]+$ ]] && (( N > MAX )); then
                MAX=$N
            fi
        done
        NEXT=$(( MAX + 1 ))
        TAG="${DATE_PREFIX}.${NEXT}"
        echo "==> No git tag on HEAD. Computed next tag: ${TAG}"
        echo "==> Creating git tag ${TAG}..."
        git -C "$REPO_ROOT" tag "$TAG"
        echo "    (Push with: git push origin ${TAG})"
    fi
fi

echo "==> Building Docker image ${DOCKER_REPO}:${TAG}..."
cd "$REPO_ROOT"
docker build -t "${DOCKER_REPO}:${TAG}" -t "${DOCKER_REPO}:latest" -f Dockerfile .

echo "==> Pushing ${DOCKER_REPO}:${TAG}..."
docker push "${DOCKER_REPO}:${TAG}"

echo "==> Pushing ${DOCKER_REPO}:latest..."
docker push "${DOCKER_REPO}:latest"

echo "==> Done! Published ${DOCKER_REPO}:${TAG}"

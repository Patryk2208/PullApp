#!/bin/bash
# src/infrastructure/scripts/local-ci.sh
# Runs CI locally using act, then loads images into minikube

set -e

echo "Starting Local CI Pipeline..."

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

images=(trip-planner route-calc accounts)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(git rev-parse --show-toplevel)"

echo "Checking prerequisites..."

if ! command -v act &> /dev/null; then
    echo -e "${RED}act not installed. "
    exit 1
fi

if ! minikube status &> /dev/null; then
    echo -e "${YELLOW} Minikube not running. Starting...${NC}"
    minikube start
fi

echo -e "${GREEN}Running GitHub Actions locally...${NC}"
cd $REPO_ROOT
act -j build-and-test \
    --artifact-server-path /tmp/act-artifacts \
    --container-architecture linux/amd64

if [ $? -ne 0 ]; then
    echo "${RED}CI pipeline failed!${NC}"
    exit 1
fi

echo "${GREEN}CI passed, continuing...${NC}"

echo -e "${GREEN}Loading images into minikube...${NC}"

for image in "${images[@]}"; do
    echo "Building $image..."
    cd $REPO_ROOT/src/services/$image
    docker build -f Dockerfile  -t "pullapp/${image}:latest" .
    echo "Loading $image..."
    minikube image load "pullapp/${image}:latest"
done

COMMIT_HASH=$(git rev-parse --short HEAD)

echo -e "${GREEN}Tagging images with commit: $COMMIT_HASH${NC}"

for image in "${images[@]}"; do
    minikube ssh "docker tag pullapp/${image}:latest pullapp/${image}:${COMMIT_HASH}"
done

echo -e "${GREEN}Updating kustomize overlay...${NC}"

cd $REPO_ROOT/k8s/overlay/local
kustomize edit set image \
    trip-planner=pullapp/trip-planner:${COMMIT_HASH} \
    route-calc=pullapp/route-calc:${COMMIT_HASH} \
    accounts=pullapp/accounts:${COMMIT_HASH}

cd $SCRIPT_DIR
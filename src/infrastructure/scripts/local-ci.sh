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

images=(trip-planner route-calc accounts gateway)

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

for image in "${images[@]}"; do
    echo "Building $image..."
    cd $REPO_ROOT/src/services/$image
    docker build -f Dockerfile  -t "pullapp/${image}:latest" .
    echo "Loading $image..."
    minikube image load "pullapp/${image}:latest"
done

COMMIT_HASH=$(git rev-parse --short HEAD)


cd $REPO_ROOT/src/infrastructure/k8s/overlay/local
for image in "${images[@]}"; do
    echo -e "${GREEN}Tagging images with commit: $COMMIT_HASH${NC}"
    minikube ssh "docker tag pullapp/${image}:latest pullapp/${image}:${COMMIT_HASH}"
    echo -e "${GREEN}Updating kustomize overlay...${NC}"
    kustomize edit set image ${image}=pullapp/${image}:${COMMIT_HASH}s
done

cd $SCRIPT_DIR
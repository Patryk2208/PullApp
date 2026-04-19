#!/bin/bash
set -e

./scripts/local-ci.sh
./scripts/local-cd.sh

echo "Ready! Port forward: kubectl port-forward service/gateway 8080:80 -n pullapp"
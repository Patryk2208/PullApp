#!/bin/bash
set -e

echo "Deploying..."
kubectl apply -k infrastructure/k8s/overlays/local/

echo "Waiting for rollout..."
kubectl rollout status deployment/trip-planner -n pullapp
kubectl rollout status deployment/route-calc -n pullapp
kubectl rollout status deployment/accounts -n pullapp

echo "Deployed"
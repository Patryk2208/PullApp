#!/usr/bin/env bash
set -euo pipefail

NAMESPACE=monitoring
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Creating namespace"
kubectl create namespace "$NAMESPACE" --dry-run=client -o yaml | kubectl apply -f -

echo "==> Adding Helm repos"
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo add grafana              https://grafana.github.io/helm-charts
helm repo add open-telemetry       https://open-telemetry.github.io/opentelemetry-helm-charts
helm repo update

echo "==> Installing kube-prometheus-stack (Prometheus + Grafana)"
helm upgrade --install kube-prometheus-stack prometheus-community/kube-prometheus-stack \
  --namespace "$NAMESPACE" \
  --version 72.6.2 \
  -f "$SCRIPT_DIR/prometheus-stack.yaml"

echo "==> Installing Loki"
helm upgrade --install loki grafana/loki \
  --namespace "$NAMESPACE" \
  --version 6.30.1 \
  -f "$SCRIPT_DIR/loki.yaml"

echo "==> Installing Tempo"
helm upgrade --install tempo grafana/tempo \
  --namespace "$NAMESPACE" \
  --version 1.21.1 \
  -f "$SCRIPT_DIR/tempo.yaml"

echo "==> Installing OpenTelemetry Collector"
helm upgrade --install otel-collector open-telemetry/opentelemetry-collector \
  --namespace "$NAMESPACE" \
  --version 0.120.0 \
  -f "$SCRIPT_DIR/otel-collector.yaml"

echo ""
echo "Done. Check progress with:"
echo "  kubectl get pods -n $NAMESPACE"
echo ""
echo "Once all pods are Running, access Grafana:"
echo "  kubectl port-forward svc/kube-prometheus-stack-grafana 3000:80 -n $NAMESPACE"
echo "  open http://localhost:3000  (admin / pullapp-grafana)"

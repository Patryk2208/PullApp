SHELL      := /bin/bash
export BASH_ENV :=

REPO_ROOT  := $(shell git rev-parse --show-toplevel)
COMMIT     := $(shell git rev-parse --short HEAD)

SERVICES   := accounts gateway route-calc trip-planner driver-tracker notifications
NAMESPACE  := pullapp
OBS_NS     := monitoring

COMPOSE_DIR := src/infrastructure/compose
K8S_OVERLAY := src/infrastructure/k8s/overlay/local
OBS_DIR     := src/infrastructure/k8s/observability

OBS_VERSIONS := \
	PROM_STACK=72.6.2 \
	LOKI=6.30.1 \
	TEMPO=1.21.1 \
	OTEL=0.120.0

COMPOSE_BASE := docker compose \
	-f $(COMPOSE_DIR)/docker-compose.yml
COMPOSE_ALL  := $(COMPOSE_BASE) \
	-f $(COMPOSE_DIR)/docker-compose.databases.yml \
	-f $(COMPOSE_DIR)/docker-compose.cache.yml \
	-f $(COMPOSE_DIR)/docker-compose.messaging.yml
COMPOSE_FRONTEND := docker compose -f $(COMPOSE_DIR)/docker-compose.frontend.yml

# trip-planner's Dockerfile COPYs cross-service paths (services/trip-planner/... and
# schemas/...), so it must be built from the src/ root; every other service builds
# from its own dir. $(call svc_ctx,<svc>) yields the right context.
svc_ctx = $(if $(filter trip-planner,$(1)),src,src/services/$(1))

RED    := \033[0;31m
GREEN  := \033[0;32m
YELLOW := \033[1;33m
CYAN   := \033[0;36m
BOLD   := \033[1m
RESET  := \033[0m

# ── Help (default target) ─────────────────────────────────────────────────────

.DEFAULT_GOAL := help

.PHONY: help
help:
	@printf "$(BOLD)PullApp — top-level Makefile$(RESET)\n\n"
	@printf "$(CYAN)Cluster$(RESET)\n"
	@printf "  make start              Start minikube + install obs stack (first-time setup)\n"
	@printf "  make cluster-start      Start minikube only\n"
	@printf "  make cluster-stop       Stop minikube\n"
	@printf "  make cluster-delete     Delete the minikube cluster entirely\n"
	@printf "  make cluster-status     Show minikube + node status\n"
	@printf "\n$(CYAN)Observability (Helm)$(RESET)\n"
	@printf "  make obs-install        Install Prometheus+Grafana, Loki, Tempo, OTel Collector\n"
	@printf "  make obs-upgrade        Upgrade all obs Helm releases to pinned versions\n"
	@printf "  make obs-uninstall      Remove all obs Helm releases\n"
	@printf "  make obs-dashboards     Apply Grafana dashboard ConfigMaps\n"
	@printf "  make obs-status         Show obs pod status\n"
	@printf "\n$(CYAN)KEDA$(RESET)\n"
	@printf "  make keda-install       Install KEDA $(KEDA_VERSION) (required for route-calc autoscaling)\n"
	@printf "  make keda-uninstall     Remove KEDA\n"
	@printf "\n$(CYAN)Build (Docker images)$(RESET)\n"
	@printf "  make build              Build all service images\n"
	@printf "  make build-<svc>        Build a single service image\n"
	@for svc in $(SERVICES); do printf "                          build-$$svc\n"; done
	@printf "\n$(CYAN)CI — build + load into minikube$(RESET)\n"
	@printf "  make ci                 Build all, load into minikube, update kustomize tags\n"
	@printf "  make ci-<svc>           CI for a single service\n"
	@for svc in $(SERVICES); do printf "                          ci-$$svc\n"; done
	@printf "\n$(CYAN)CD — deploy to cluster$(RESET)\n"
	@printf "  make cd                 kubectl apply kustomize overlay + wait for rollouts\n"
	@printf "  make reset              Delete + re-apply all k8s resources (after infra changes)\n"
	@printf "\n$(CYAN)Compose deps (local infra)$(RESET)\n"
	@printf "  make infra              Start all local deps (db, cache, messaging)\n"
	@printf "  make infra-down         Stop and remove all local deps\n"
	@printf "  make infra-stop         Stop (preserve volumes)\n"
	@printf "  make infra-clean        Stop + remove volumes (nuclear)\n"
	@printf "  make infra-logs         Follow all compose logs\n"
	@printf "  make infra-ps           Show compose container status\n"
	@printf "  make infra-db           Start databases only\n"
	@printf "  make infra-cache        Start caches only\n"
	@printf "  make infra-messaging    Start messaging only\n"
	@printf "\n$(CYAN)Frontend (Next.js web app)$(RESET)\n"
	@printf "  make frontend           Build + run the web app in compose (→ :5000, needs pf-gateway)\n"
	@printf "  make frontend-dev       Run the web app dev server via pnpm\n"
	@printf "  make frontend-down      Stop the frontend container\n"
	@printf "  make frontend-logs      Follow frontend logs\n"
	@printf "\n$(CYAN)Rollout management$(RESET)\n"
	@printf "  make restart            Rolling restart all deployments\n"
	@printf "  make restart-<svc>      Rolling restart a single service\n"
	@for svc in $(SERVICES); do printf "                          restart-$$svc\n"; done
	@printf "\n$(CYAN)Port-forwards$(RESET)\n"
	@printf "  make pf-dev             :8080-5005 → all services (Postman-ready)\n"
	@printf "  make pf-stop            kill all kubectl port-forwards\n"
	@printf "  make pf-gateway         :8080 → gateway\n"
	@printf "  make pf-grafana         :3000 → Grafana\n"
	@printf "  make pf-prometheus      :9090 → Prometheus\n"
	@printf "  make pf-loki            :3100 → Loki\n"
	@printf "  make pf-tempo           :4317 → Tempo (OTLP gRPC)\n"
	@printf "  make pf-rabbit          :15672 → RabbitMQ management UI\n"
	@printf "\n$(CYAN)CI — GitHub Actions locally (act)$(RESET)\n"
	@printf "  make ci-full            Run all service workflows via act\n"
	@printf "  make ci-full-<svc>      Run a single service workflow via act\n"
	@for svc in $(SERVICES); do printf "                          ci-full-$$svc\n"; done
	@printf "\n$(CYAN)Combined workflows$(RESET)\n"
	@printf "  make run                Full from scratch: cluster + obs + keda + infra + build + deploy\n"
	@printf "\n$(CYAN)Visibility$(RESET)\n"
	@printf "  make status             Cluster + pod + compose summary\n"
	@printf "  make logs               Follow logs for all pullapp pods\n"
	@printf "  make logs-<svc>         Follow logs for a single service\n"
	@for svc in $(SERVICES); do printf "                          logs-$$svc\n"; done

# ── Prereq checks ─────────────────────────────────────────────────────────────

.PHONY: _check-docker _check-minikube _check-kubectl _check-kustomize _check-helm _check-act

_check-act:
	@command -v act >/dev/null 2>&1 || (printf "$(RED)act not found — https://github.com/nektos/act$(RESET)\n" && exit 1)

_check-docker:
	@command -v docker >/dev/null 2>&1 || (printf "$(RED)docker not found$(RESET)\n" && exit 1)
	@docker info >/dev/null 2>&1      || (printf "$(RED)Docker daemon not running$(RESET)\n" && exit 1)

_check-minikube: _check-docker
	@command -v minikube >/dev/null 2>&1 || (printf "$(RED)minikube not found$(RESET)\n" && exit 1)

_check-kubectl:
	@command -v kubectl >/dev/null 2>&1 || (printf "$(RED)kubectl not found$(RESET)\n" && exit 1)

_check-kustomize:
	@command -v kustomize >/dev/null 2>&1 || (printf "$(RED)kustomize not found$(RESET)\n" && exit 1)

_check-helm:
	@command -v helm >/dev/null 2>&1 || (printf "$(RED)helm not found$(RESET)\n" && exit 1)

_cluster-ensure: _check-minikube
	@minikube status >/dev/null 2>&1 || (printf "$(YELLOW)Minikube not running — starting...$(RESET)\n" && minikube start)

# ── Cluster ───────────────────────────────────────────────────────────────────

.PHONY: start cluster-start cluster-stop cluster-delete cluster-status

start: cluster-start obs-install keda-install
	@printf "$(GREEN)Cluster ready. Run 'make run' to build and deploy services.$(RESET)\n"

cluster-start: _check-minikube
	@minikube status >/dev/null 2>&1 \
		&& printf "$(YELLOW)Minikube already running$(RESET)\n" \
		|| minikube start
	@printf "$(CYAN)Raising inotify limits for Promtail...$(RESET)\n"
	@minikube ssh -- sudo sysctl fs.inotify.max_user_instances=512
	@minikube ssh -- sudo sysctl fs.inotify.max_user_watches=524288
	@kubectl get namespace $(NAMESPACE) >/dev/null 2>&1 \
		|| kubectl create namespace $(NAMESPACE)

cluster-stop:
	minikube stop

cluster-delete:
	@printf "$(RED)This will delete the entire minikube cluster. Continue? [y/N] $(RESET)" && read ans && [ "$${ans}" = "y" ]
	minikube delete

cluster-status: _check-kubectl
	@printf "$(BOLD)--- Minikube ---$(RESET)\n"
	@minikube status 2>/dev/null || printf "$(RED)not running$(RESET)\n"
	@printf "\n$(BOLD)--- Nodes ---$(RESET)\n"
	@kubectl get nodes 2>/dev/null || true

# ── Observability ─────────────────────────────────────────────────────────────

.PHONY: obs-install obs-upgrade obs-uninstall obs-status obs-dashboards obs-promtail

obs-install: _check-helm _check-kubectl
	@printf "$(CYAN)Adding Helm repos...$(RESET)\n"
	@helm repo add prometheus-community https://prometheus-community.github.io/helm-charts 2>/dev/null || true
	@helm repo add grafana              https://grafana.github.io/helm-charts              2>/dev/null || true
	@helm repo add open-telemetry       https://open-telemetry.github.io/opentelemetry-helm-charts 2>/dev/null || true
	@helm repo update
	@kubectl create namespace $(OBS_NS) --dry-run=client -o yaml | kubectl apply -f -
	@printf "$(CYAN)Installing kube-prometheus-stack...$(RESET)\n"
	helm upgrade --install kube-prometheus-stack prometheus-community/kube-prometheus-stack \
		--namespace $(OBS_NS) --version 72.6.2 \
		-f $(OBS_DIR)/prometheus-stack.yaml
	@printf "$(CYAN)Installing Loki...$(RESET)\n"
	helm upgrade --install loki grafana/loki \
		--namespace $(OBS_NS) --version 6.30.1 \
		-f $(OBS_DIR)/loki.yaml
	@printf "$(CYAN)Installing Tempo...$(RESET)\n"
	helm upgrade --install tempo grafana/tempo \
		--namespace $(OBS_NS) --version 1.21.1 \
		-f $(OBS_DIR)/tempo.yaml
	@printf "$(CYAN)Installing OTel Collector...$(RESET)\n"
	helm upgrade --install otel-collector open-telemetry/opentelemetry-collector \
		--namespace $(OBS_NS) --version 0.120.0 \
		-f $(OBS_DIR)/otel-collector.yaml
	@printf "$(CYAN)Installing Promtail...$(RESET)\n"
	helm upgrade --install promtail grafana/promtail \
		--namespace $(OBS_NS) --version 6.16.6 \
		-f $(OBS_DIR)/promtail.yaml
	@printf "$(GREEN)Observability stack installed.$(RESET)\n"
	@printf "  Grafana: make pf-grafana  (admin / pullapp-grafana)\n"

obs-upgrade: _check-helm
	helm upgrade kube-prometheus-stack prometheus-community/kube-prometheus-stack \
		--namespace $(OBS_NS) --version 72.6.2 \
		-f $(OBS_DIR)/prometheus-stack.yaml
	helm upgrade loki grafana/loki \
		--namespace $(OBS_NS) --version 6.30.1 \
		-f $(OBS_DIR)/loki.yaml
	helm upgrade tempo grafana/tempo \
		--namespace $(OBS_NS) --version 1.21.1 \
		-f $(OBS_DIR)/tempo.yaml
	helm upgrade otel-collector open-telemetry/opentelemetry-collector \
		--namespace $(OBS_NS) --version 0.120.0 \
		-f $(OBS_DIR)/otel-collector.yaml
	helm upgrade promtail grafana/promtail \
		--namespace $(OBS_NS) --version 6.16.6 \
		-f $(OBS_DIR)/promtail.yaml

obs-uninstall: _check-helm
	helm uninstall kube-prometheus-stack --namespace $(OBS_NS) 2>/dev/null || true
	helm uninstall loki                  --namespace $(OBS_NS) 2>/dev/null || true
	helm uninstall tempo                 --namespace $(OBS_NS) 2>/dev/null || true
	helm uninstall otel-collector        --namespace $(OBS_NS) 2>/dev/null || true
	helm uninstall promtail              --namespace $(OBS_NS) 2>/dev/null || true

obs-dashboards: _check-kubectl
	@printf "$(CYAN)Applying Grafana dashboards...$(RESET)\n"
	kubectl apply -f $(OBS_DIR)/grafana/dashboards/

obs-status: _check-kubectl
	@printf "$(BOLD)--- Observability pods ($(OBS_NS)) ---$(RESET)\n"
	kubectl get pods -n $(OBS_NS)

# ── KEDA ──────────────────────────────────────────────────────────────────────

KEDA_VERSION := 2.16.0

.PHONY: keda-install keda-uninstall

keda-install: _check-helm _check-kubectl
	@helm repo add kedacore https://kedacore.github.io/charts 2>/dev/null || true
	@helm repo update
	helm upgrade --install keda kedacore/keda \
		--namespace keda --create-namespace \
		--version $(KEDA_VERSION)
	@printf "$(GREEN)KEDA $(KEDA_VERSION) installed.$(RESET)\n"

keda-uninstall: _check-helm
	helm uninstall keda --namespace keda 2>/dev/null || true

# ── Build ─────────────────────────────────────────────────────────────────────

.PHONY: build

build: _check-docker
	@set -e; for svc in $(SERVICES); do \
		ctx=src/services/$$svc; [ "$$svc" = trip-planner ] && ctx=src; \
		docker build -f src/services/$$svc/Dockerfile -t pullapp/$$svc:latest $$ctx; \
	done

build-%: _check-docker
	docker build -f src/services/$*/Dockerfile -t pullapp/$*:latest $(call svc_ctx,$*)

# ── Build into minikube's docker daemon ───────────────────────────────────────
#
# We build straight into minikube's daemon (eval minikube docker-env) instead of
# `minikube image load`. `image load` does NOT overwrite an existing :latest tag,
# so with imagePullPolicy: Never the pods kept running stale images. Building in
# the daemon means the new image is there the moment the build finishes.

.PHONY: _images _tag-images

_images: _check-docker _check-minikube
	@printf "$(CYAN)Building images into minikube's docker daemon...$(RESET)\n"
	@eval $$(minikube -p minikube docker-env) && set -e && for svc in $(SERVICES); do \
		ctx=src/services/$$svc; [ "$$svc" = trip-planner ] && ctx=src; \
		printf "$(CYAN)  building $$svc ($$ctx)...$(RESET)\n"; \
		docker build -f src/services/$$svc/Dockerfile -t pullapp/$$svc:latest $$ctx; \
	done
	@printf "$(GREEN)Images built into minikube.$(RESET)\n"

_tag-images: _check-kustomize
	@cd $(K8S_OVERLAY) && for svc in $(SERVICES); do \
		kustomize edit set image $$svc=pullapp/$$svc:latest; \
	done

# ── CI — per-service and all ──────────────────────────────────────────────────

.PHONY: ci

ci: _images _tag-images
	@printf "$(CYAN)Restarting deployments...$(RESET)\n"
	kubectl rollout restart $(foreach svc,$(SERVICES),deployment/$(svc)) -n $(NAMESPACE)
	@for svc in $(SERVICES); do \
		printf "  waiting for $$svc... "; \
		kubectl rollout status deployment/$$svc -n $(NAMESPACE) --timeout=120s && printf "$(GREEN)ok$(RESET)\n"; \
	done
	@printf "$(GREEN)CI complete.$(RESET)\n"

ci-%: _check-docker _check-minikube _tag-images
	@printf "$(CYAN)Building $* into minikube ($(call svc_ctx,$*))...$(RESET)\n"
	@eval $$(minikube -p minikube docker-env) && \
		docker build -f src/services/$*/Dockerfile -t pullapp/$*:latest $(call svc_ctx,$*)
	@printf "$(CYAN)Restarting $*...$(RESET)\n"
	kubectl rollout restart deployment/$* -n $(NAMESPACE)
	kubectl rollout status  deployment/$* -n $(NAMESPACE) --timeout=120s
	@printf "$(GREEN)CI done for $*.$(RESET)\n"

# ── CD — deploy ───────────────────────────────────────────────────────────────

.PHONY: cd reset

cd: _check-kubectl _check-kustomize
	@printf "$(CYAN)Applying kustomize overlay...$(RESET)\n"
	kubectl apply -k $(K8S_OVERLAY)/
	@printf "$(CYAN)Waiting for rollouts...$(RESET)\n"
	@for svc in $(SERVICES); do \
		printf "  waiting for $$svc... "; \
		kubectl rollout status deployment/$$svc -n $(NAMESPACE) --timeout=120s && printf "$(GREEN)ok$(RESET)\n"; \
	done
	@printf "$(GREEN)Deploy complete.$(RESET)\n"
	@printf "  Gateway: make pf-gateway\n"

reset: _check-kubectl _check-kustomize
	@printf "$(YELLOW)Deleting all pullapp resources and re-applying...$(RESET)\n"
	kubectl delete -k $(K8S_OVERLAY)/ --ignore-not-found
	kubectl apply  -k $(K8S_OVERLAY)/
	@for svc in $(SERVICES); do \
		kubectl rollout status deployment/$$svc -n $(NAMESPACE) --timeout=120s; \
	done
	@printf "$(GREEN)Reset complete.$(RESET)\n"

# ── Compose deps ──────────────────────────────────────────────────────────────

.PHONY: infra infra-down infra-stop infra-clean infra-logs infra-ps \
        infra-db infra-cache infra-messaging _infra-env

_infra-env:
	@if [ ! -f $(COMPOSE_DIR)/.env ]; then \
		cp $(COMPOSE_DIR)/.env.example $(COMPOSE_DIR)/.env; \
		printf "$(YELLOW).env created from .env.example — update passwords before use$(RESET)\n"; \
	fi

infra: _check-docker _infra-env
	$(COMPOSE_ALL) up -d
	@printf "$(GREEN)Local deps running.$(RESET)\n"
	@printf "  RabbitMQ UI: make pf-rabbit\n"

infra-down: _check-docker
	$(COMPOSE_ALL) down

infra-stop: _check-docker
	$(COMPOSE_ALL) stop

infra-clean: _check-docker
	@printf "$(RED)This will delete all local dep volumes. Continue? [y/N] $(RESET)" && read ans && [ "$${ans}" = "y" ]
	$(COMPOSE_ALL) down -v

infra-logs: _check-docker
	$(COMPOSE_ALL) logs -f

infra-ps: _check-docker
	$(COMPOSE_ALL) ps

infra-db: _check-docker _infra-env
	$(COMPOSE_BASE) -f $(COMPOSE_DIR)/docker-compose.databases.yml up -d

infra-cache: _check-docker _infra-env
	$(COMPOSE_BASE) -f $(COMPOSE_DIR)/docker-compose.cache.yml up -d

infra-messaging: _check-docker _infra-env
	$(COMPOSE_BASE) -f $(COMPOSE_DIR)/docker-compose.messaging.yml up -d

# ── Frontend (Next.js web app, docker-compose) ────────────────────────────────
#
# The web app runs in compose on host networking and proxies /api + /sse to the
# gateway at 127.0.0.1:8080, so the gateway must be port-forwarded first
# (make pf-gateway). Serves on http://localhost:5000.

.PHONY: frontend frontend-down frontend-logs frontend-dev

frontend: _check-docker
	@printf "$(YELLOW)Frontend proxies to the gateway on :8080 — run 'make pf-gateway' in another shell first.$(RESET)\n"
	@printf "$(CYAN)Building + starting frontend...$(RESET)\n"
	$(COMPOSE_FRONTEND) up -d --build
	@printf "$(GREEN)Frontend running → http://localhost:5000$(RESET)\n"

frontend-down: _check-docker
	$(COMPOSE_FRONTEND) down

frontend-logs: _check-docker
	$(COMPOSE_FRONTEND) logs -f

frontend-dev:
	@printf "$(CYAN)Starting frontend dev server (pnpm)...$(RESET)\n"
	cd $(REPO_ROOT)/src/frontend/pullapp-frontend && pnpm install && pnpm dev

# ── Rollout management ────────────────────────────────────────────────────────

.PHONY: restart

restart: _check-kubectl
	@for svc in $(SERVICES); do \
		kubectl rollout restart deployment/$$svc -n $(NAMESPACE); \
	done
	@for svc in $(SERVICES); do \
		kubectl rollout status deployment/$$svc -n $(NAMESPACE) --timeout=120s; \
	done

restart-%: _check-kubectl
	kubectl rollout restart deployment/$* -n $(NAMESPACE)
	kubectl rollout status  deployment/$* -n $(NAMESPACE) --timeout=120s

# ── Port-forwards ─────────────────────────────────────────────────────────────

.PHONY: pf-dev pf-stop pf-gateway pf-grafana pf-prometheus pf-loki pf-tempo pf-rabbit

pf-dev:
	@printf "$(CYAN)Forwarding all services (Ctrl-C stops all):$(RESET)\n"
	@printf "  gateway        → http://localhost:8080\n"
	@printf "  accounts       → http://localhost:5001\n"
	@printf "  trip-planner   → http://localhost:5002\n"
	@printf "  notifications  → http://localhost:5003\n"
	@printf "  driver-tracker → http://localhost:5004\n"
	@printf "  route-calc     → http://localhost:5005\n"
	kubectl port-forward service/gateway        8080:80 -n $(NAMESPACE) &
	kubectl port-forward service/accounts       5001:80 -n $(NAMESPACE) &
	kubectl port-forward service/trip-planner   5002:80 -n $(NAMESPACE) &
	kubectl port-forward service/notifications  5003:80 -n $(NAMESPACE) &
	kubectl port-forward service/driver-tracker 5004:80 -n $(NAMESPACE) &
	kubectl port-forward service/route-calc     5005:80 -n $(NAMESPACE) &
	wait

pf-stop:
	@pkill -f "kubectl port-forward" 2>/dev/null && printf "$(CYAN)All port-forwards stopped$(RESET)\n" || printf "$(CYAN)No port-forwards running$(RESET)\n"

pf-gateway:
	@printf "$(CYAN)Forwarding gateway → http://localhost:8080$(RESET)\n"
	kubectl port-forward service/gateway 8080:80 -n $(NAMESPACE)

pf-grafana:
	@printf "$(CYAN)Forwarding Grafana → http://localhost:3000  (admin / pullapp-grafana)$(RESET)\n"
	kubectl port-forward svc/kube-prometheus-stack-grafana 3000:80 -n $(OBS_NS)

pf-prometheus:
	@printf "$(CYAN)Forwarding Prometheus → http://localhost:9090$(RESET)\n"
	kubectl port-forward svc/kube-prometheus-stack-prometheus 9090:9090 -n $(OBS_NS)

pf-loki:
	@printf "$(CYAN)Forwarding Loki → http://localhost:3100$(RESET)\n"
	kubectl port-forward svc/loki 3100:3100 -n $(OBS_NS)

pf-tempo:
	@printf "$(CYAN)Forwarding Tempo OTLP gRPC → localhost:4317$(RESET)\n"
	kubectl port-forward svc/tempo 4317:4317 -n $(OBS_NS)

pf-rabbit:
	@printf "$(CYAN)Forwarding RabbitMQ UI → http://localhost:15672$(RESET)\n"
	kubectl port-forward svc/compute-queue 15672:15672 -n $(NAMESPACE)

# ── Status + logs ─────────────────────────────────────────────────────────────

.PHONY: status logs $(addprefix logs-,$(SERVICES))

status: _check-kubectl
	@printf "$(BOLD)=== Cluster ===$(RESET)\n"
	@minikube status 2>/dev/null || printf "$(RED)minikube not running$(RESET)\n"
	@printf "\n$(BOLD)=== Pods ($(NAMESPACE)) ===$(RESET)\n"
	@kubectl get pods -n $(NAMESPACE) 2>/dev/null || printf "$(RED)kubectl unavailable$(RESET)\n"
	@printf "\n$(BOLD)=== Pods ($(OBS_NS)) ===$(RESET)\n"
	@kubectl get pods -n $(OBS_NS) 2>/dev/null || true
	@printf "\n$(BOLD)=== Compose deps ===$(RESET)\n"
	@$(COMPOSE_ALL) ps 2>/dev/null || printf "$(YELLOW)compose not running$(RESET)\n"

logs: _check-kubectl
	kubectl logs -n $(NAMESPACE) -l app.kubernetes.io/part-of=pullapp --all-containers --follow --max-log-requests=10

logs-%: _check-kubectl
	kubectl logs -n $(NAMESPACE) -l app=$* --all-containers --follow

# ── CI — GitHub Actions via act ──────────────────────────────────────────────

ACT_FLAGS := --pull=false

.PHONY: ci-full FORCE

FORCE:

ci-full: _check-act
	@for svc in $(SERVICES); do \
		printf "$(CYAN)act: running $$svc workflow...$(RESET)\n"; \
		act push -W .github/workflows/$$svc-ci.yaml $(ACT_FLAGS); \
	done
	@printf "$(GREEN)act: all workflows done.$(RESET)\n"

ci-full-%: _check-act FORCE
	act push -W .github/workflows/$*-ci.yaml $(ACT_FLAGS)

# ── Full workflow ─────────────────────────────────────────────────────────────

.PHONY: run

# Order matters: build images into minikube and set the kustomize tags BEFORE the
# first `cd`. Otherwise `cd` applies manifests whose images don't exist yet, every
# pod sits in ErrImageNeverPull, and the rollout wait burns ~120s per service
# before anything is built. Build first → `cd` then rolls out fast.
run: _cluster-ensure obs-install keda-install infra _images _tag-images cd
	@printf "\n$(GREEN)$(BOLD)PullApp is running.$(RESET)\n"
	@printf "  App API:    make pf-gateway   → http://localhost:8080\n"
	@printf "  Frontend:   make pf-gateway + make frontend → http://localhost:5000\n"
	@printf "  Grafana:    make pf-grafana   → http://localhost:3000\n"
	@printf "  Prometheus: make pf-prometheus → http://localhost:9090\n"

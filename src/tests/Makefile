VENV   := .venv
PYTHON := $(CURDIR)/$(VENV)/bin/python
PYTEST := $(CURDIR)/$(VENV)/bin/pytest

# Gateway URL — override with: make test-gateway BASE_URL=http://other-host:8080
BASE_URL ?= http://localhost:8080

.PHONY: help setup test-gateway test-gateway-health test-gateway-routing \
	test-accounts test-accounts-api test-accounts-comms \
	test-trip-planner test-driver-route test-driver-rides \
	test-passenger-requests test-passenger-rides

help:
	@echo "Gateway E2E Tests"
	@echo ""
	@echo "Prerequisites:"
	@echo "  kubectl port-forward service/gateway 8080:80 -n pullapp"
	@echo ""
	@echo "Targets:"
	@echo "  setup                  Install Python dependencies into .venv"
	@echo "  test-gateway           Run all gateway tests (health + routing)"
	@echo "  test-gateway-health    Run /health, /health/live, /health/ready only"
	@echo "  test-gateway-routing   Run upstream-reachability and path-matching tests"
	@echo ""
	@echo "Variables:"
	@echo "  BASE_URL  Gateway address (default: http://localhost:8080)"

setup:
	python3 -m venv $(VENV) && $(PYTHON) -m pip install -q -r e2e/requirements.txt

test-gateway: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/gateway/ -v -m "not dirty"

test-gateway-health: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/gateway/test_health.py -v

test-gateway-routing: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/gateway/test_routing.py -v

test-accounts: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/accounts/ -v

test-accounts-api: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/accounts/test_auth_api.py -v

test-accounts-comms: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/accounts/test_service_comms.py -v

test-trip-planner: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/trip_planner/ -v -m "not requires_route_calc"

test-driver-route: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/trip_planner/test_driver_route.py -v -m "not requires_route_calc"

test-driver-rides: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/trip_planner/test_driver_rides.py -v

test-passenger-requests: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/trip_planner/test_passenger_requests.py -v -m "not requires_route_calc"

test-passenger-rides: setup
	cd e2e && PULLAPP_GATEWAY_URL=$(BASE_URL) $(PYTEST) services/trip_planner/test_passenger_rides.py -v

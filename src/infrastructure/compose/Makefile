COMPOSE_BASE := docker compose -f docker-compose.yml
COMPOSE_ALL  := $(COMPOSE_BASE) \
                -f docker-compose.databases.yml \
                -f docker-compose.cache.yml \
                -f docker-compose.messaging.yml

.PHONY: up down stop restart logs ps \
        up-db up-cache up-messaging \
        down-db down-cache down-messaging \
        env clean

# ── Setup ─────────────────────────────────────────────────────────────────────

env:
	@if [ -f .env ]; then \
		echo ".env already exists, skipping"; \
	else \
		cp .env.example .env; \
		echo ".env created from .env.example — update passwords before use"; \
	fi

# ── Full stack ────────────────────────────────────────────────────────────────

up:
	$(COMPOSE_ALL) up -d

down:
	$(COMPOSE_ALL) down

stop:
	$(COMPOSE_ALL) stop

restart:
	$(COMPOSE_ALL) restart

logs:
	$(COMPOSE_ALL) logs -f

ps:
	$(COMPOSE_ALL) ps

# ── Per-group ─────────────────────────────────────────────────────────────────

up-db:
	$(COMPOSE_BASE) -f docker-compose.databases.yml up -d

up-cache:
	$(COMPOSE_BASE) -f docker-compose.cache.yml up -d

up-messaging:
	$(COMPOSE_BASE) -f docker-compose.messaging.yml up -d

down-db:
	$(COMPOSE_BASE) -f docker-compose.databases.yml down

down-cache:
	$(COMPOSE_BASE) -f docker-compose.cache.yml down

down-messaging:
	$(COMPOSE_BASE) -f docker-compose.messaging.yml down

# ── Nuclear ───────────────────────────────────────────────────────────────────

clean:
	$(COMPOSE_ALL) down -v
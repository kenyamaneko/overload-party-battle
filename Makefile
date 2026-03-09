.PHONY: build run test test-integration test-coverage clean restore help

# ─── Config ──────────────────────────────────────────────
SLN     := OverloadParty.Battle.slnx
SERVER  := src/OverloadParty.Battle.Server
TESTS   := tests/OverloadParty.Battle.Tests

# ─── Common Repo ─────────────────────────────────────────
COMMON_DIR  ?= $(CURDIR)/../overload-party-common
CLIENT_DIR  ?= $(CURDIR)/../overload-party-client
GATEWAY_DIR ?= $(CURDIR)/../overload-party-gateway

# ─── Build ───────────────────────────────────────────────
restore:  ## Restore NuGet packages
	dotnet restore $(SLN)

build:  ## Build the solution
	dotnet build $(SLN)

# ─── Run ─────────────────────────────────────────────────
run:  ## Run local dev server (port 9002, in-memory mock repos)
	ASPNETCORE_ENVIRONMENT=Development BATTLE_MODE=local \
	CARDS_JSON_PATH=$(CURDIR)/data/cards_gen.json \
		dotnet run --project $(SERVER)

# ─── Test ────────────────────────────────────────────────
TEST_DB_URL ?= Host=localhost;Port=5433;Database=testdb;Username=testuser;Password=testpass
COMPOSE_TEST := $(COMMON_DIR)/db/docker-compose.test.yml

test:  ## Run all tests (unit only, DB tests skipped)
	dotnet test $(TESTS)

test-integration:  ## Run tests including DB integration (starts container automatically)
	docker compose -f $(COMPOSE_TEST) up -d --wait
	TEST_DB_URL="$(TEST_DB_URL)" dotnet test $(TESTS) || (docker compose -f $(COMPOSE_TEST) down; exit 1)
	docker compose -f $(COMPOSE_TEST) down

test-coverage:  ## Run tests with code coverage report
	dotnet test $(TESTS) --collect:"XPlat Code Coverage" --results-directory .coverage

# ─── Code Generation ────────────────────────────────────
generate:  ## Generate cards.json / constants from common repo
	python3 $(COMMON_DIR)/scripts/generate_from_yaml.py \
		--gateway-dir $(GATEWAY_DIR) \
		--battle-dir $(CURDIR) \
		--client-dir $(CLIENT_DIR)

# ─── Misc ────────────────────────────────────────────────
clean:  ## Remove build artifacts
	dotnet clean $(SLN) -v q
	rm -rf .coverage

help:  ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

.DEFAULT_GOAL := help

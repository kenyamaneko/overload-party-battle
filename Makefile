.PHONY: build run test test-coverage clean restore help

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
test:  ## Run all tests
	dotnet test $(TESTS)

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

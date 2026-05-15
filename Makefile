.PHONY: build run test test-coverage clean restore update-common db-up db-down db-reset help

# ─── Config ──────────────────────────────────────────────
SLN     := OverloadParty.Battle.slnx
SERVER  := src/OverloadParty.Battle.Server
TESTS   := tests/OverloadParty.Battle.Tests

# ─── Common Repo ─────────────────────────────────────────
COMMON_DIR  ?= $(CURDIR)/../overload-party-common

# ─── Build ───────────────────────────────────────────────
restore:  ## Restore NuGet packages
	dotnet restore $(SLN)

build:  ## Build the solution
	dotnet build $(SLN)

# ─── DB ──────────────────────────────────────────────────
db-up:  ## Start local Postgres (docker compose)
	docker compose up -d postgres

db-down:  ## Stop local Postgres
	docker compose down

db-reset:  ## Drop volume and recreate DB
	docker compose down -v
	docker compose up -d postgres

# ─── Run ─────────────────────────────────────────────────
PORT ?= 9002

run: db-up  ## Run local dev server (port 9002, compose Postgres 接続)
	@lsof -ti :$(PORT) | xargs kill -9 2>/dev/null || true
	ASPNETCORE_ENVIRONMENT=Development \
	DATABASE_CONN="Host=localhost;Port=5432;Database=battle;Username=battle;Password=battle;Search Path=battle" \
	CARDS_JSON_PATH=$(COMMON_DIR)/packages/game-state-dotnet/cache/cards_gen.json \
		dotnet run --project $(SERVER)

# ─── Test ────────────────────────────────────────────────
test:  ## Run all tests (Testcontainers; requires Docker running)
	dotnet test $(TESTS)

test-coverage:  ## Run tests with code coverage report
	dotnet test $(TESTS) --collect:"XPlat Code Coverage" --results-directory .coverage

# ─── Dependencies ───────────────────────────────────────
GENERATED_PKGS := OverloadParty.GameDesignConstants OverloadParty.GameLogicConstants OverloadParty.GameState OverloadParty.ApiBattleRpc

update-common:  ## Update OverloadParty.* generated packages to the latest version
	dotnet nuget locals http-cache --clear
	@for pkg in $(GENERATED_PKGS); do rm -rf $(HOME)/.nuget/packages/$$(echo $$pkg | tr A-Z a-z); done
	dotnet restore $(SLN)
	@echo "Updated to:" && dotnet list $(SERVER) package --include-prerelease | grep OverloadParty\.

# ─── Misc ────────────────────────────────────────────────
clean:  ## Remove build artifacts
	dotnet clean $(SLN) -v q
	rm -rf .coverage

help:  ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

.DEFAULT_GOAL := help

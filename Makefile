.PHONY: build run test test-coverage clean restore update-common sync-card-master-data down help

# ─── Config ──────────────────────────────────────────────
SLN     := OverloadParty.Battle.slnx
SERVER  := src/OverloadParty.Battle.Server

# ─── Build ───────────────────────────────────────────────
restore:  ## Restore NuGet packages
	dotnet restore $(SLN)

build:  ## Build the solution
	dotnet build $(SLN)

# ─── Run ─────────────────────────────────────────────────
run:  ## Run the full local stack (app + infra) in compose; edit source and restart `battle` to reload
	docker compose up

down:  ## Stop the local stack and remove volumes
	docker compose down -v

# ─── Test ────────────────────────────────────────────────
test:  ## Run all tests (Testcontainers; requires Docker running)
	dotnet test $(SLN)

test-coverage:  ## Run tests with code coverage report
	dotnet test $(SLN) --collect:"XPlat Code Coverage" --results-directory .coverage

# ─── Dependencies ───────────────────────────────────────
GENERATED_PKGS := OverloadParty.GameDesignConstants OverloadParty.GameLogicConstants OverloadParty.GameState OverloadParty.ApiBattleRpc

update-common:  ## Update OverloadParty.* generated packages to the latest version
	dotnet nuget locals http-cache --clear
	@for pkg in $(GENERATED_PKGS); do rm -rf $(HOME)/.nuget/packages/$$(echo $$pkg | tr A-Z a-z); done
	dotnet restore $(SLN)
	@echo "Updated to:" && dotnet list $(SERVER) package --include-prerelease | grep OverloadParty\.

sync-card-master-data:  ## Sync the bundled card master data from the card repository (CARD_REPO, CARD_REF)
	bash scripts/sync_card_master_data.sh

# ─── Misc ────────────────────────────────────────────────
clean:  ## Remove build artifacts
	dotnet clean $(SLN) -v q
	rm -rf .coverage

help:  ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-18s\033[0m %s\n", $$1, $$2}'

.DEFAULT_GOAL := help

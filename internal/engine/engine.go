package engine

import (
	"context"
	"fmt"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
)

// GameEngine orchestrates all game logic. Stateless — all state lives in PostgreSQL.
type GameEngine struct {
	repo      repository.GameRepository
	cardCache *cache.CardCache
	effects   *effect.EffectRegistry
}

func NewGameEngine(repo repository.GameRepository, cardCache *cache.CardCache) *GameEngine {
	return &GameEngine{
		repo:      repo,
		cardCache: cardCache,
	}
}

// SetEffectRegistry is called after the effect system is initialized.
func (e *GameEngine) SetEffectRegistry(reg *effect.EffectRegistry) {
	e.effects = reg
}

// EffectRegistry returns the effect registry for external consumers (e.g., NPC AI).
func (e *GameEngine) EffectRegistry() *effect.EffectRegistry {
	return e.effects
}

// RunAutoAdvance processes automatic phases (draw) and persists the result.
// Used at game start and whenever the state is at an auto-phase.
// Returns (gameOver, winReason, finishErr). If gameOver is true, the caller
// should handle the game-over (e.g. call FinishGame).
func (e *GameEngine) RunAutoAdvance(ctx context.Context, gameID string) (bool, string, error) {
	game, err := e.repo.GetGame(ctx, gameID)
	if err != nil {
		return false, "", fmt.Errorf("get game: %w", err)
	}

	var gameOver bool
	var winReason string

	err = e.repo.UpdateGameState(ctx, gameID, func(state *model.GameState) error {
		var advErr error
		gameOver, winReason, advErr = AutoAdvancePhases(state, game, e.cardCache)
		return advErr
	})
	if err != nil {
		return false, "", fmt.Errorf("auto advance: %w", err)
	}

	if gameOver {
		// Active player loses (repository out)
		state, err := e.repo.GetGameState(ctx, gameID)
		if err != nil {
			return true, winReason, fmt.Errorf("get state after game over: %w", err)
		}
		winnerNum := model.OpponentNum(state.ActivePlayer)
		winnerID := model.PlayerIDForNum(game, winnerNum)
		if err := e.repo.FinishGame(ctx, gameID, winnerID); err != nil {
			return true, winReason, fmt.Errorf("finish game: %w", err)
		}
	}

	return gameOver, winReason, nil
}

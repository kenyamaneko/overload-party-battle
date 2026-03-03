package engine

import (
	"context"
	"encoding/json"
	"fmt"
	"log"

	"github.com/kenyamaneko/overload-party-common/model"
)

// ActionResult carries the outcome of processing an action.
type ActionResult struct {
	Events       []model.GameEvent
	StateUpdated bool
	GameOver     bool
	WinnerNum    int64
	WinReason    string
	NeedsDiscard bool
}

// ProcessAction is the main entry point for all player actions.
func (e *GameEngine) ProcessAction(ctx context.Context, gameID, playerID, actionType string, data json.RawMessage) (*ActionResult, error) {
	game, err := e.repo.GetGame(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get game: %w", err)
	}

	if game.Status != model.GameStatusPlaying {
		return nil, fmt.Errorf("game is not in playing state: %s", game.Status)
	}

	playerNum := model.PlayerNumForID(game, playerID)
	if playerNum == 0 {
		return nil, fmt.Errorf("player %s not in game %s", playerID, gameID)
	}

	var result *ActionResult

	err = e.repo.UpdateGameState(ctx, gameID, func(state *model.GameState) error {
		// Validate active player (except for chain responses)
		if actionType != model.ActionSetReactive && state.ActivePlayer != playerNum {
			return fmt.Errorf("not your turn")
		}

		// Validate action is allowed in current phase
		if !isActionAllowedInPhase(state.CurrentPhase, actionType) {
			return fmt.Errorf("action %s not allowed in phase %s", actionType, state.CurrentPhase)
		}

		// Dispatch to specific processor
		var actionResult *ActionResult
		var processErr error

		switch actionType {
		case model.ActionPlayCard:
			var req PlayCardRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse play_card request: %w", err)
			}
			actionResult, processErr = processPlayCard(state, game, playerNum, req, e.cardCache, e.effects)

		case model.ActionAttack:
			var req AttackRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse attack request: %w", err)
			}
			actionResult, processErr = processAttack(state, game, playerNum, req, e.cardCache, e.effects)

		case model.ActionScaleUp:
			var req ScaleUpRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse scale_up request: %w", err)
			}
			actionResult, processErr = processScaleUp(state, game, playerNum, req, e.cardCache)

		case model.ActionDistributeYield:
			var req DistributeYieldRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse distribute_yield request: %w", err)
			}
			actionResult, processErr = processDistributeYield(state, game, playerNum, req, e.cardCache)

		case model.ActionEndPhase:
			actionResult, processErr = processEndPhase(state, game, playerNum, e.cardCache)

		case model.ActionDiscardHand:
			var req DiscardHandRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse discard_hand request: %w", err)
			}
			actionResult, processErr = processDiscardHand(state, game, playerNum, req, e.cardCache)

		case model.ActionActivateEffect:
			var req ActivateEffectRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse activate_effect request: %w", err)
			}
			actionResult, processErr = processActivateEffect(state, game, playerNum, req, e.cardCache, e.effects)

		case model.ActionMigrate:
			var req MigrateRequest
			if err := json.Unmarshal(data, &req); err != nil {
				return fmt.Errorf("parse migrate request: %w", err)
			}
			actionResult, processErr = processMigrate(state, game, playerNum, req, e.cardCache)

		default:
			return fmt.Errorf("unknown action type: %s", actionType)
		}

		if processErr != nil {
			return processErr
		}

		// Check win conditions
		winnerNum, winReason, gameOver := CheckWinCondition(state, game)
		if gameOver {
			actionResult.GameOver = true
			actionResult.WinnerNum = winnerNum
			actionResult.WinReason = winReason
		}

		actionResult.StateUpdated = true
		result = actionResult
		return nil
	})

	if err != nil {
		return nil, err
	}

	// Persist events
	if result != nil {
		eventCount, _ := e.repo.GetEventCount(ctx, gameID)
		for i := range result.Events {
			result.Events[i].SequenceNumber = eventCount + int64(i) + 1
			if err := e.repo.AppendEvent(ctx, &result.Events[i]); err != nil {
				log.Printf("failed to append event (game=%s, seq=%d): %v", gameID, result.Events[i].SequenceNumber, err)
			}
		}

		// Handle game over
		if result.GameOver {
			winnerID := model.PlayerIDForNum(game, result.WinnerNum)
			if err := e.repo.FinishGame(ctx, gameID, winnerID); err != nil {
				log.Printf("failed to finish game (game=%s, winner=%s): %v", gameID, winnerID, err)
			}
		}
	}

	return result, nil
}

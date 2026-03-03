package engine

import (
	"encoding/json"
	"fmt"

	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

func processEndPhase(state *model.GameState, game *model.Game, playerNum int64, cc *cache.CardCache) (*ActionResult, error) {
	currentPhase := state.CurrentPhase

	switch currentPhase {
	case model.PhaseMain:
		if isFirstTurn(state.CurrentTurn) {
			// First turn: skip battle, go directly to end
			state.CurrentPhase = model.PhaseEnd
		} else {
			state.CurrentPhase = model.PhaseBattle
		}

	case model.PhaseBattle:
		state.CurrentPhase = model.PhaseEnd

	default:
		return nil, fmt.Errorf("cannot end phase %s manually", currentPhase)
	}

	// If we reached the end phase, process it
	if state.CurrentPhase == model.PhaseEnd {
		needsDiscard, err := ProcessEndPhase(state, game, cc)
		if err != nil {
			return nil, fmt.Errorf("process end phase: %w", err)
		}

		if needsDiscard {
			eventData, _ := json.Marshal(map[string]interface{}{
				"phase":        "end",
				"needsDiscard": true,
			})
			playerID := model.PlayerIDForNum(game, playerNum)
			return &ActionResult{
				NeedsDiscard: true,
				Events: []model.GameEvent{{
					GameID:    game.GameID,
					EventType: model.EventPhaseEnd,
					PlayerID:  &playerID,
					EventData: eventData,
					CreatedAt: time.Time{},
				}},
			}, nil
		}

		// Check Launch Failure before switching player
		if checkLaunchFailure(state, playerNum) {
			eventData, _ := json.Marshal(map[string]interface{}{
				"phase":        "end",
				"nextTurn":     state.CurrentTurn,
				"activePlayer": state.ActivePlayer,
			})
			playerID := model.PlayerIDForNum(game, playerNum)
			return &ActionResult{
				GameOver:  true,
				WinnerNum: model.OpponentNum(playerNum),
				WinReason: model.WinReasonLaunchFailure,
				Events: []model.GameEvent{{
					GameID:    game.GameID,
					EventType: model.EventTurnEnd,
					PlayerID:  &playerID,
					EventData: eventData,
					CreatedAt: time.Time{},
				}},
			}, nil
		}

		// No discard needed — switch player and auto-advance
		SwitchActivePlayer(state)

		// Auto-advance through draw
		gameOver, winReason, err := AutoAdvancePhases(state, game, cc)
		if err != nil {
			return nil, fmt.Errorf("auto advance: %w", err)
		}

		result := &ActionResult{}
		if gameOver {
			// Repository out — active player loses
			loserNum := state.ActivePlayer
			result.GameOver = true
			result.WinnerNum = model.OpponentNum(loserNum)
			result.WinReason = winReason
		}

		eventData, _ := json.Marshal(map[string]interface{}{
			"phase":        "end",
			"nextTurn":     state.CurrentTurn,
			"activePlayer": state.ActivePlayer,
			"currentPhase": state.CurrentPhase,
		})
		playerID := model.PlayerIDForNum(game, playerNum)
		result.Events = append(result.Events, model.GameEvent{
			GameID:    game.GameID,
			EventType: model.EventTurnEnd,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		})

		return result, nil
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"previousPhase": currentPhase,
		"currentPhase":  state.CurrentPhase,
	})
	playerID := model.PlayerIDForNum(game, playerNum)
	return &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventPhaseChange,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}, nil
}

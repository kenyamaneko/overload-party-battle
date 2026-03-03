package engine

import (
	"encoding/json"
	"fmt"

	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

type DiscardHandRequest struct {
	CardInstanceIDs []string `json:"cardInstanceIds"`
}

func processDiscardHand(state *model.GameState, game *model.Game, playerNum int64, req DiscardHandRequest, cc *cache.CardCache) (*ActionResult, error) {
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get hand: %w", err)
	}

	// Calculate required discards
	requiredDiscards := len(hand) - model.HandLimit
	if requiredDiscards <= 0 {
		return nil, fmt.Errorf("no discard needed, hand size is %d", len(hand))
	}

	if len(req.CardInstanceIDs) != requiredDiscards {
		return nil, fmt.Errorf("must discard exactly %d cards, got %d", requiredDiscards, len(req.CardInstanceIDs))
	}

	// Validate all cards exist in hand
	discardSet := make(map[string]bool)
	for _, id := range req.CardInstanceIDs {
		discardSet[id] = true
	}

	trash, err := state.GetTrash(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get trash: %w", err)
	}

	var newHand []model.HandCard
	for _, card := range hand {
		if discardSet[card.InstanceID] {
			trash = append(trash, card.CardID)
			delete(discardSet, card.InstanceID)
		} else {
			newHand = append(newHand, card)
		}
	}

	// Check that all requested discards were found
	if len(discardSet) > 0 {
		return nil, fmt.Errorf("some cards not found in hand")
	}

	if err := state.SetHand(playerNum, newHand); err != nil {
		return nil, fmt.Errorf("set hand: %w", err)
	}
	if err := state.SetTrash(playerNum, trash); err != nil {
		return nil, fmt.Errorf("set trash: %w", err)
	}

	// Check Launch Failure before switching player
	if checkLaunchFailure(state, playerNum) {
		eventData, _ := json.Marshal(map[string]interface{}{
			"discardedCount": requiredDiscards,
			"discardedIds":   req.CardInstanceIDs,
		})
		playerID := model.PlayerIDForNum(game, playerNum)
		return &ActionResult{
			GameOver:  true,
			WinnerNum: model.OpponentNum(playerNum),
			WinReason: model.WinReasonLaunchFailure,
			Events: []model.GameEvent{{
				GameID:    game.GameID,
				EventType: model.EventDiscardHand,
				PlayerID:  &playerID,
				EventData: eventData,
				CreatedAt: time.Time{},
			}},
		}, nil
	}

	// After discard, switch player and auto-advance
	SwitchActivePlayer(state)

	gameOver, winReason, err := AutoAdvancePhases(state, game, cc)
	if err != nil {
		return nil, fmt.Errorf("auto advance: %w", err)
	}

	result := &ActionResult{}
	if gameOver {
		loserNum := state.ActivePlayer
		result.GameOver = true
		result.WinnerNum = model.OpponentNum(loserNum)
		result.WinReason = winReason
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"discardedCount": requiredDiscards,
		"discardedIds":   req.CardInstanceIDs,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	result.Events = append(result.Events, model.GameEvent{
		GameID:    game.GameID,
		EventType: model.EventDiscardHand,
		PlayerID:  &playerID,
		EventData: eventData,
		CreatedAt: time.Time{},
	})

	return result, nil
}

// AutoDiscardOldest discards the oldest cards when a player times out the discard prompt.
func (e *GameEngine) AutoDiscardOldest(state *model.GameState, game *model.Game, playerNum int64) (*ActionResult, error) {
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get hand: %w", err)
	}

	requiredDiscards := len(hand) - model.HandLimit
	if requiredDiscards <= 0 {
		return nil, nil
	}

	// Discard the first N cards (oldest)
	var ids []string
	for i := 0; i < requiredDiscards; i++ {
		ids = append(ids, hand[i].InstanceID)
	}

	return processDiscardHand(state, game, playerNum, DiscardHandRequest{
		CardInstanceIDs: ids,
	}, e.cardCache)
}

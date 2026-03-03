package engine

import (
	"encoding/json"
	"fmt"

	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

type ActivateEffectRequest struct {
	InstanceID       string          `json:"instanceId"`
	TargetInstanceID *string         `json:"targetInstanceId,omitempty"`
	ChoiceData       json.RawMessage `json:"choiceData,omitempty"`
}

func processActivateEffect(state *model.GameState, game *model.Game, playerNum int64, req ActivateEffectRequest, cc *cache.CardCache, effects *effect.EffectRegistry) (*ActionResult, error) {
	if effects == nil {
		return nil, fmt.Errorf("effect system not initialized")
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	// Find the source resource
	source, _, idx := model.FindResourceByID(field, req.InstanceID)
	if source == nil {
		// Check support zone
		for i, sup := range field.Support {
			if sup != nil && sup.InstanceID == req.InstanceID {
				return activateSupportEffect(state, game, playerNum, field, i, req, cc, effects)
			}
		}
		return nil, fmt.Errorf("resource %s not found on field", req.InstanceID)
	}

	card := cc.Get(source.CardID)
	if card == nil {
		return nil, fmt.Errorf("card definition not found for %d", source.CardID)
	}

	// Look up effect handler by card number
	reg, ok := effects.Get(card.CardNo, effect.TriggerActivate)
	if !ok {
		return nil, fmt.Errorf("card %d has no activatable effect", card.CardNo)
	}

	// Check 1-turn limit
	if source.EffectUsedThisTurn {
		return nil, fmt.Errorf("effect already used this turn")
	}

	// Find target if specified
	var target *model.ResourceInstance
	if req.TargetInstanceID != nil {
		opponentNum := model.OpponentNum(playerNum)
		oppField, err := state.GetField(opponentNum)
		if err != nil {
			return nil, fmt.Errorf("get opponent field: %w", err)
		}
		// Search own field first, then opponent's
		target, _, _ = model.FindResourceByID(field, *req.TargetInstanceID)
		if target == nil {
			target, _, _ = model.FindResourceByID(oppField, *req.TargetInstanceID)
		}
	}

	// Execute effect
	ctx := &effect.EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  playerNum,
		Source:     source,
		Target:     target,
		CardCache:  cc,
		ChoiceData: req.ChoiceData,
	}

	effectResult, err := reg.Handler(ctx)
	if err != nil {
		return nil, fmt.Errorf("execute effect for card %d: %w", card.CardNo, err)
	}

	// Mark effect as used this turn
	source.EffectUsedThisTurn = true
	// Re-read field to get any modifications made by the effect
	field, _ = state.GetField(playerNum)
	if field != nil {
		updatedSource, _, _ := model.FindResourceByID(field, req.InstanceID)
		if updatedSource != nil {
			updatedSource.EffectUsedThisTurn = true
			// Re-place on field
			if idx >= 0 && idx < 3 {
				field.Frontend[idx] = updatedSource
			}
			_ = state.SetField(playerNum, field)
		}
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"cardNo":   card.CardNo,
		"sourceId": req.InstanceID,
		"targetId": req.TargetInstanceID,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	result := &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventActivateEffect,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}

	if effectResult != nil {
		result.Events = append(result.Events, effectResult.Events...)
	}

	return result, nil
}

func activateSupportEffect(state *model.GameState, game *model.Game, playerNum int64, field *model.Field, supportIdx int, req ActivateEffectRequest, cc *cache.CardCache, effects *effect.EffectRegistry) (*ActionResult, error) {
	sup := field.Support[supportIdx]
	card := cc.Get(sup.CardID)
	if card == nil {
		return nil, fmt.Errorf("card definition not found")
	}

	reg, ok := effects.Get(card.CardNo, effect.TriggerActivate)
	if !ok {
		return nil, fmt.Errorf("card %d has no activate effect", card.CardNo)
	}

	ctx := &effect.EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  playerNum,
		CardCache:  cc,
		ChoiceData: req.ChoiceData,
	}

	effectResult, err := reg.Handler(ctx)
	if err != nil {
		return nil, fmt.Errorf("execute support effect for card %d: %w", card.CardNo, err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"cardNo":   card.CardNo,
		"sourceId": req.InstanceID,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	result := &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventActivateEffect,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}

	if effectResult != nil {
		result.Events = append(result.Events, effectResult.Events...)
	}

	return result, nil
}

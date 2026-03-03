package engine

import (
	"encoding/json"
	"fmt"

	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

type ScaleUpRequest struct {
	InstanceID     string  `json:"componentInstanceId"`
	TargetRank     string  `json:"targetRank"`
	InstanceFamily *string `json:"instanceFamily,omitempty"`
}

func processScaleUp(state *model.GameState, game *model.Game, playerNum int64, req ScaleUpRequest, cc *cache.CardCache) (*ActionResult, error) {
	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	// Find the resource
	resource, zone, idx := model.FindResourceByID(field, req.InstanceID)
	if resource == nil {
		return nil, fmt.Errorf("resource %s not found on field", req.InstanceID)
	}

	card := cc.Get(resource.CardID)
	if card == nil {
		return nil, fmt.Errorf("card definition %d not found", resource.CardID)
	}

	// Check Resizable attribute
	if !card.Resizable {
		return nil, fmt.Errorf("card is not Resizable")
	}

	// Validate rank progression
	if err := validateRankProgression(resource.Rank, req.TargetRank); err != nil {
		return nil, err
	}

	// Instance Family handling
	if resource.Rank == model.RankSmall && req.TargetRank == model.RankMedium {
		// First scale-up: instance family required
		if req.InstanceFamily == nil {
			return nil, fmt.Errorf("instance family required for first scale-up")
		}
		if !isValidFamily(*req.InstanceFamily) {
			return nil, fmt.Errorf("invalid instance family: %s", *req.InstanceFamily)
		}
		resource.InstanceFamily = req.InstanceFamily
	}

	// Update rank
	resource.Rank = req.TargetRank

	// Recalculate max values based on new rank and family
	resource.MaxAV = CalculateMaxAV(resource, cc)
	// currentAV = maxAV - accumulated damage
	resource.CurrentAV = resource.MaxAV - resource.Damage

	if resource.CurrentTP != nil && !card.Elastic {
		newTP := RecalculateMaxTP(resource, card)
		resource.MaxTP = &newTP
		resource.CurrentTP = &newTP
	}
	if resource.CurrentYield != nil && !card.Elastic {
		newYield := RecalculateMaxYield(resource, card)
		resource.MaxYield = &newYield
		resource.CurrentYield = &newYield
	}

	// Update on field
	if zone == model.ZoneFrontend {
		field.Frontend[idx] = resource
	} else {
		field.Backend[idx] = resource
	}

	if err := state.SetField(playerNum, field); err != nil {
		return nil, fmt.Errorf("set field: %w", err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"instanceId":     req.InstanceID,
		"targetRank":     req.TargetRank,
		"instanceFamily": req.InstanceFamily,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	return &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventScaleUp,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}, nil
}

func validateRankProgression(current, target string) error {
	switch {
	case current == model.RankSmall && target == model.RankMedium:
		return nil
	case current == model.RankMedium && target == model.RankLarge:
		return nil
	case current == target:
		return fmt.Errorf("already at rank %s", current)
	case current == model.RankLarge:
		return fmt.Errorf("already at maximum rank")
	default:
		return fmt.Errorf("invalid rank progression: %s -> %s", current, target)
	}
}

func isValidFamily(family string) bool {
	return family == model.FamilyM || family == model.FamilyC || family == model.FamilyR
}

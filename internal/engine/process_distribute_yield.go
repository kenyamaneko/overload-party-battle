package engine

import (
	"encoding/json"
	"fmt"

	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

type DistributeYieldRequest struct {
	Distributions []YieldDistribution `json:"distributions"`
}

type YieldDistribution struct {
	InstanceID string `json:"componentInstanceId"`
	Amount     int64  `json:"amount"`
}

func processDistributeYield(state *model.GameState, game *model.Game, playerNum int64, req DistributeYieldRequest, cc *cache.CardCache) (*ActionResult, error) {
	// First turn of first player cannot monetize
	if isFirstTurn(state.CurrentTurn) {
		return nil, fmt.Errorf("cannot distribute yield on first turn")
	}

	if len(req.Distributions) == 0 {
		return nil, fmt.Errorf("no distributions specified")
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	insightPool := state.GetInsightPool(playerNum)
	totalDistributed := int64(0)
	budget := state.GetBudget(playerNum)

	for _, dist := range req.Distributions {
		if dist.Amount <= 0 {
			return nil, fmt.Errorf("distribution amount must be positive")
		}

		// Find the target resource (must be a compute resource for monetization)
		resource, zone, idx := model.FindResourceByID(field, dist.InstanceID)
		if resource == nil {
			return nil, fmt.Errorf("resource %s not found", dist.InstanceID)
		}

		card := cc.Get(resource.CardID)
		if card == nil {
			return nil, fmt.Errorf("card definition not found")
		}

		// Monetization is done through backend compute resources only (RULEBOOK §7)
		if zone != model.ZoneBackend || !model.IsComputeType(card.CardType) {
			return nil, fmt.Errorf("can only monetize through backend compute resources")
		}

		// Check throughput limit
		effectiveTP := CalculateEffectiveTP(resource, field, cc)
		remaining := effectiveTP - resource.MonetizedAmount
		if dist.Amount > remaining {
			return nil, fmt.Errorf("exceeds throughput capacity: available %d, requested %d", remaining, dist.Amount)
		}

		totalDistributed += dist.Amount
		resource.MonetizedAmount += dist.Amount

		// Elastic auto-scaling: Backend Compute cards increase TP on monetization
		if card.Elastic && card.ElasticIncrement > 0 {
			applyElasticBonus(resource, card, cc)
		}

		// Update resource on field (backend zone)
		field.Backend[idx] = resource
	}

	// Check total against Insight pool
	if totalDistributed > insightPool {
		return nil, fmt.Errorf("insufficient Insight pool: have %d, need %d", insightPool, totalDistributed)
	}

	// Apply changes
	state.SetInsightPool(playerNum, insightPool-totalDistributed)
	state.SetBudget(playerNum, budget+totalDistributed)

	if err := state.SetField(playerNum, field); err != nil {
		return nil, fmt.Errorf("set field: %w", err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"distributions": req.Distributions,
		"totalAmount":   totalDistributed,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	return &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventDistributeYield,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}, nil
}

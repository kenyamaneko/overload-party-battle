package engine

import (
	"fmt"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

// ProcessDrawPhase draws 1 card from repository to hand for the active player.
// Returns an error if repository is empty (triggers repository_out loss).
func ProcessDrawPhase(state *model.GameState, game *model.Game) error {
	playerNum := state.ActivePlayer

	repo, err := state.GetRepository(playerNum)
	if err != nil {
		return fmt.Errorf("get repository: %w", err)
	}

	if len(repo) == 0 {
		return fmt.Errorf("repository empty")
	}

	hand, err := state.GetHand(playerNum)
	if err != nil {
		return fmt.Errorf("get hand: %w", err)
	}

	// Draw the top card
	cardNo := repo[0]
	repo = repo[1:]

	hand = append(hand, model.HandCard{
		InstanceID: state.NextInstanceID(),
		CardID:     cardNo,
	})

	if err := state.SetHand(playerNum, hand); err != nil {
		return fmt.Errorf("set hand: %w", err)
	}
	if err := state.SetRepository(playerNum, repo); err != nil {
		return fmt.Errorf("set repository: %w", err)
	}

	return nil
}

// ProcessEndPhase handles end-of-turn processing.
// Returns true if a discard prompt is needed (hand > 6).
func ProcessEndPhase(state *model.GameState, game *model.Game, cc *cache.CardCache) (bool, error) {
	playerNum := state.ActivePlayer

	field, err := state.GetField(playerNum)
	if err != nil {
		return false, fmt.Errorf("get field: %w", err)
	}

	// 0. Collect maintenance cost
	totalMC := collectMaintenanceCost(field, cc)
	if totalMC > 0 {
		budget := state.GetBudget(playerNum)
		state.SetBudget(playerNum, budget-totalMC)
	}

	// 1. Yield Generation (after maintenance cost, before effect reset) — face-up only
	// Elastic DB cards scale their Yield before generation (auto-scaling on usage)
	totalYield := int64(0)
	for _, res := range field.Backend {
		if res == nil || !res.FaceUp {
			continue
		}
		if res.MigratingFrom != nil {
			continue // Migration target: no yield generation
		}
		// Elastic auto-scaling: Backend DB cards increase Yield each end phase
		card := cc.Get(res.CardID)
		if card != nil && model.IsDataType(card.CardType) && card.Elastic && card.ElasticIncrement > 0 {
			applyElasticBonus(res, card, cc)
		}
		yieldVal := CalculateEffectiveYield(res, field, cc)
		totalYield += yieldVal
	}
	if totalYield > 0 {
		currentInsight := state.GetInsightPool(playerNum)
		state.SetInsightPool(playerNum, currentInsight+totalYield)
	}

	// 2. Remove temporary effects with duration "this_turn"
	removeThisTurnEffects(field)

	// 2. Reset hasAttacked and effectUsedThisTurn flags
	resetTurnFlags(field)

	// 3. Reset monetizedAmount for next turn
	resetMonetizedAmount(field)

	if err := state.SetField(playerNum, field); err != nil {
		return false, fmt.Errorf("set field: %w", err)
	}

	// 4. Check hand size
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return false, fmt.Errorf("get hand: %w", err)
	}

	needsDiscard := len(hand) > model.HandLimit
	return needsDiscard, nil
}

// collectMaintenanceCost calculates the total maintenance cost for all resources on a field.
// Non-Elastic: MC × rank_multiplier (fixed)
// Elastic: MC=0 at base (free tier), scales with ElasticBonus above base stat
func collectMaintenanceCost(field *model.Field, cc *cache.CardCache) int64 {
	total := int64(0)
	for _, res := range field.Frontend {
		total += resourceMaintenanceCost(res, cc)
	}
	for _, res := range field.Backend {
		total += resourceMaintenanceCost(res, cc)
	}
	return total
}

func resourceMaintenanceCost(res *model.ResourceInstance, cc *cache.CardCache) int64 {
	if res == nil || !res.FaceUp {
		return 0
	}
	card := cc.Get(res.CardID)
	if card == nil {
		return 0
	}

	if card.Elastic {
		// Elastic: free_tier + pay-per-use model
		// MC = max(0, intrinsicStat - free_tier) × cost_per_request / 100
		// intrinsicStat = base × rank × family + effectiveElasticBonus (excludes external buffs)
		if card.FreeTier <= 0 || card.CostPerRequest <= 0 {
			return 0 // Serverless (cost_per_request=0) → always free
		}
		baseStat := elasticBaseStat(card)
		if baseStat == 0 {
			return 0
		}

		baseWithRank := baseStat * RankMultiplier(res.Rank)
		if res.InstanceFamily != nil {
			tpMult, _ := InstanceFamilyMultiplier(res.InstanceFamily)
			baseWithRank = Truncate(float64(baseWithRank) * tpMult)
		}
		intrinsic := baseWithRank + effectiveElasticBonus(res.ElasticBonus, card.FreeTier)
		if intrinsic <= card.FreeTier {
			return 0
		}
		return (intrinsic - card.FreeTier) * card.CostPerRequest / 100
	}

	// Non-Elastic: MC × rank_multiplier (fixed)
	mc := model.MaintenanceCostFor(card)
	if mc == 0 {
		return 0
	}
	return mc * RankMultiplier(res.Rank)
}

// elasticBaseStat returns the card's base stat (throughput or yield) before rank/family.
func elasticBaseStat(card *model.CardDefinition) int64 {
	if model.IsComputeType(card.CardType) || model.IsAIMLType(card.CardType) {
		stats, _ := model.ParseComputeStats(card.Stats)
		if stats != nil {
			return stats.Throughput
		}
	}
	if model.IsDataType(card.CardType) {
		stats, _ := model.ParseDataStats(card.Stats)
		if stats != nil {
			return stats.Yield
		}
	}
	return 0
}

// SwitchActivePlayer flips active_player and increments turn counter.
// Turn counter increments every switch: T1(先攻)→T2(後攻)→T3(先攻)→...
func SwitchActivePlayer(state *model.GameState) {
	state.ActivePlayer = model.OpponentNum(state.ActivePlayer)
	state.CurrentTurn++
	state.CurrentPhase = model.PhaseDraw
}

// AutoAdvancePhases processes automatic phases (draw) until reaching
// a player-controlled phase or game end.
func AutoAdvancePhases(state *model.GameState, game *model.Game, cc *cache.CardCache) (bool, string, error) {
	for {
		phase := state.CurrentPhase

		switch phase {
		case model.PhaseDraw:
			// Deploy countdown: flip face-down resources that have finished deploying
			processDeployCountdown(state, state.ActivePlayer, cc)

			// Migration completion: retire old resources whose migration period is over
			processMigrationCompletion(state, state.ActivePlayer)

			// Per-turn budget addition
			currentBudget := state.GetBudget(state.ActivePlayer)
			state.SetBudget(state.ActivePlayer, currentBudget+model.PerTurnBudget)

			if err := ProcessDrawPhase(state, game); err != nil {
				return true, model.WinReasonRepositoryOut, nil
			}
			state.CurrentPhase = nextPhase(phase)

		case model.PhaseMain:
			return false, "", nil // Player-controlled

		case model.PhaseBattle:
			return false, "", nil // Player-controlled

		case model.PhaseEnd:
			return false, "", nil // Handled separately (needs discard prompt)

		default:
			return false, "", fmt.Errorf("unknown phase: %s", phase)
		}
	}
}

// processDeployCountdown decrements DeployingTurnsLeft for face-down resources
// and flips them face-up when the countdown reaches 0.
func processDeployCountdown(state *model.GameState, playerNum int64, cc *cache.CardCache) {
	field, err := state.GetField(playerNum)
	if err != nil {
		return
	}

	changed := false

	// Frontend resources
	for _, res := range field.Frontend {
		if res == nil || res.FaceUp {
			continue
		}
		if res.DeployingTurnsLeft > 0 {
			res.DeployingTurnsLeft--
			changed = true
		}
		if res.DeployingTurnsLeft == 0 {
			res.FaceUp = true
			field.HasHadActiveResource = true
		}
	}

	// Backend resources
	for _, res := range field.Backend {
		if res == nil || res.FaceUp {
			continue
		}
		if res.DeployingTurnsLeft > 0 {
			res.DeployingTurnsLeft--
			changed = true
		}
		if res.DeployingTurnsLeft == 0 {
			res.FaceUp = true
			field.HasHadActiveResource = true
		}
	}

	// Support (Platform) cards
	for _, sup := range field.Support {
		if sup == nil || sup.DeployingTurnsLeft <= 0 {
			continue
		}
		sup.DeployingTurnsLeft--
		changed = true
		if sup.DeployingTurnsLeft == 0 {
			sup.FaceDown = false
		}
	}

	if changed {
		_ = state.SetField(playerNum, field)
	}
}

func removeThisTurnEffects(field *model.Field) {
	remove := map[string]bool{"this_turn": true, "until_next_own_turn_end": true}
	for _, res := range field.Frontend {
		if res != nil {
			res.TemporaryEffects = filterOutDurations(res.TemporaryEffects, remove)
		}
	}
	for _, res := range field.Backend {
		if res != nil {
			res.TemporaryEffects = filterOutDurations(res.TemporaryEffects, remove)
		}
	}
}

func filterOutDurations(effects []model.TemporaryEffect, remove map[string]bool) []model.TemporaryEffect {
	var kept []model.TemporaryEffect
	for _, eff := range effects {
		if !remove[eff.Duration] {
			kept = append(kept, eff)
		}
	}
	return kept
}

func resetTurnFlags(field *model.Field) {
	field.IncidentPlayedThisTurn = false
	for _, res := range field.Frontend {
		if res != nil {
			res.HasAttacked = false
			res.EffectUsedThisTurn = false
		}
	}
	for _, res := range field.Backend {
		if res != nil {
			res.EffectUsedThisTurn = false
		}
	}
	for _, sup := range field.Support {
		if sup != nil {
			sup.EffectUsedThisTurn = false
		}
	}
}

func resetMonetizedAmount(field *model.Field) {
	for _, res := range field.Frontend {
		if res != nil {
			res.MonetizedAmount = 0
		}
	}
	for _, res := range field.Backend {
		if res != nil {
			res.MonetizedAmount = 0
		}
	}
}

// applyElasticBonus increases an Elastic resource's ElasticBonus by its card's increment.
// No cap — growth is unlimited but subject to ln diminishing returns when read.
func applyElasticBonus(res *model.ResourceInstance, card *model.CardDefinition, cc *cache.CardCache) {
	res.ElasticBonus += card.ElasticIncrement
}

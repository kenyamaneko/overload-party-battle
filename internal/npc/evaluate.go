package npc

import (
	"encoding/json"

	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// decisionCtx holds all context needed for NPC category-based decisions.
type decisionCtx struct {
	field    *model.Field
	oppField *model.Field
	hand     []model.HandCard
	budget   int64
	ai       *StandardAI
}

// evaluateCard checks if a card's effect should be used and returns (priority, shouldUse, choiceData).
func (ai *StandardAI) evaluateCard(cardNo int64, trigger effect.TriggerType, ctx *decisionCtx) (int, bool, json.RawMessage) {
	if ai.effects == nil {
		return 0, false, nil
	}
	info := ai.effects.GetEffectInfo(cardNo, trigger)
	if info == nil {
		return 0, false, nil
	}

	// Check conditions (RequireBudget, RequireFactionCount, etc.)
	if !ai.checkConditions(info.Conditions, ctx) {
		return 0, false, nil
	}

	// Evaluate categories — use the highest priority among all categories
	maxPri := 0
	use := false
	for _, cat := range info.Categories {
		pri, ok := evaluateCategory(cat, info, ctx)
		if ok {
			use = true
			if pri > maxPri {
				maxPri = pri
			}
		}
	}

	if !use {
		return 0, false, nil
	}

	// Target selection
	var choiceData json.RawMessage
	if info.TargetType == effect.TargetChoice {
		target := ai.selectTarget(info, ctx)
		if target == nil {
			return 0, false, nil // No valid target
		}
		choiceData, _ = json.Marshal(map[string]string{"instanceId": *target})
	}

	// Search effects: let the engine auto-select if no choice provided
	// (SearchRepo now handles empty choiceData by picking first matching card)

	return maxPri, true, choiceData
}

// evaluateCategory returns (priority, shouldUse) for a single category.
func evaluateCategory(cat effect.EffectCategory, info *effect.EffectInfo, ctx *decisionCtx) (int, bool) {
	switch cat {
	case effect.CatBudgetGain:
		if ctx.budget < LowBudgetThreshold {
			return PriBudgetGainHigh, true
		}
		return PriBudgetGainLow, true

	case effect.CatBudgetPenalty:
		return PriBudgetPenalty, true

	case effect.CatInsightGain:
		return PriInsightGain, true

	case effect.CatInsightAbsorb:
		if countAllResources(ctx.oppField) > 0 {
			return PriInsightAbsorb, true
		}
		return 0, false

	case effect.CatDraw:
		if len(ctx.hand) <= 3 {
			return PriDrawHigh, true
		}
		return PriDrawLow, true

	case effect.CatSearch:
		if len(ctx.hand) <= 3 {
			return PriSearchHigh, true
		}
		return PriSearchLow, true

	case effect.CatAoEDamage:
		zone := info.TargetZone
		count := countResourcesInZone(ctx.oppField, zone)
		if count >= 2 {
			return PriAoEDamage, true
		}
		return 0, false

	case effect.CatSingleDamage:
		zone := info.TargetZone
		if countResourcesInZone(ctx.oppField, zone) > 0 {
			return PriSingleDamage, true
		}
		return 0, false

	case effect.CatDebuff:
		if countAllResources(ctx.oppField) > 0 {
			return PriDebuff, true
		}
		return 0, false

	case effect.CatBuff:
		if countAllResources(ctx.field) > 0 {
			return PriBuff, true
		}
		return 0, false

	case effect.CatHeal:
		if hasDamagedResource(ctx.field) {
			return PriHeal, true
		}
		return 0, false

	case effect.CatDeployFree:
		return PriDeployFree, true

	case effect.CatRecoverCard:
		return PriRecoverCard, true

	case effect.CatRevealTrap:
		if hasFaceDownSupport(ctx.oppField) {
			return PriRevealTrap, true
		}
		return 0, false

	case effect.CatDestroyPlatform:
		if hasPlatform(ctx.oppField, ctx.ai.cardCache) {
			return PriDestroyPlatform, true
		}
		return 0, false

	case effect.CatCancelAction:
		// Reactive — don't proactively use
		return 0, false

	case effect.CatSurvive:
		// Reactive — don't proactively use
		return 0, false
	}

	return 0, false
}

// checkConditions verifies all EffectConditions are met.
func (ai *StandardAI) checkConditions(conditions []effect.EffectCondition, ctx *decisionCtx) bool {
	for _, cond := range conditions {
		switch cond.Type {
		case "min_budget":
			if ctx.budget < cond.Value {
				return false
			}
		case "max_budget":
			if ctx.budget > cond.Value {
				return false
			}
		case "faction_count":
			count := effect.CountFactionCards(ctx.field, cond.Faction, ai.cardCache)
			if int64(count) < cond.Value {
				return false
			}
		case "opponent_backend":
			count := 0
			for _, r := range ctx.oppField.Backend {
				if r != nil && r.FaceUp {
					count++
				}
			}
			if count == 0 {
				return false
			}
		}
	}
	return true
}

// selectTarget picks an appropriate target based on effect categories.
func (ai *StandardAI) selectTarget(info *effect.EffectInfo, ctx *decisionCtx) *string {
	zone := info.TargetZone

	// Damage targets → weakest opponent resource (easiest to destroy)
	if info.HasCategory(effect.CatSingleDamage) {
		return weakestInZone(ctx.oppField, zone)
	}

	// Debuff targets → strongest opponent resource (most impactful debuff)
	if info.HasCategory(effect.CatDebuff) {
		return strongestInZone(ctx.oppField, zone, ai.cardCache)
	}

	// Heal targets → most damaged own resource
	if info.HasCategory(effect.CatHeal) {
		return mostDamagedOwn(ctx.field)
	}

	// Buff targets → strongest own resource (maximize value)
	if info.HasCategory(effect.CatBuff) {
		return strongestInZone(ctx.field, zone, ai.cardCache)
	}

	// Destroy platform → first platform
	if info.HasCategory(effect.CatDestroyPlatform) {
		return firstPlatformID(ctx.oppField, ai.cardCache)
	}

	// Fallback: weakest opponent resource
	return weakestInZone(ctx.oppField, zone)
}

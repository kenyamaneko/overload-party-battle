package engine

import (
	"math"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

// effectiveElasticBonus applies logarithmic diminishing returns to raw ElasticBonus.
// Formula: scale × ln(1 + rawBonus / scale)
// `scale` is the card's FreeTier — higher FreeTier means slower diminishing.
func effectiveElasticBonus(rawBonus int64, scale int64) int64 {
	if rawBonus <= 0 || scale <= 0 {
		return rawBonus
	}
	s := float64(scale)
	return int64(s * math.Log(1+float64(rawBonus)/s))
}

// CalculateEffectiveTP computes the current effective throughput of a compute resource.
// Priority: base → rank → instance_family → platform → attachment → temporary
func CalculateEffectiveTP(instance *model.ResourceInstance, field *model.Field, cc *cache.CardCache) int64 {
	if !instance.FaceUp || instance.CurrentTP == nil {
		return 0
	}

	card := cc.Get(instance.CardID)
	if card == nil {
		return *instance.CurrentTP
	}

	stats, err := model.ParseComputeStats(card.Stats)
	if err != nil {
		return *instance.CurrentTP
	}

	// 1. Base value
	baseTP := float64(stats.Throughput)

	// 2. Rank multiplier
	baseTP *= float64(RankMultiplier(instance.Rank))

	// 3. Instance Family modifier
	if instance.InstanceFamily != nil {
		tpMult, _ := InstanceFamilyMultiplier(instance.InstanceFamily)
		baseTP *= tpMult
	}

	result := Truncate(baseTP)

	// 4. Elastic auto-scaling bonus (ln diminishing returns)
	if instance.ElasticBonus > 0 && card.Elastic && card.FreeTier > 0 {
		result += effectiveElasticBonus(instance.ElasticBonus, card.FreeTier)
	} else {
		result += instance.ElasticBonus
	}

	// 5. Platform card effects
	result += CalculatePlatformBonus(instance, field, "tp", cc)

	// 5b. Resource's own passive TP bonuses
	result += CalculatePassiveTPBonus(instance, field, cc)

	// 6. Attachment effects
	result += CalculateAttachmentBonus(instance, "tp", cc)

	// 7. Temporary effects
	for _, eff := range instance.TemporaryEffects {
		if eff.EffectType == "buff_tp" {
			result += eff.Value
		} else if eff.EffectType == "debuff_tp" {
			result -= eff.Value
		}
	}

	if result < 0 {
		result = 0
	}
	return result
}

// CalculateEffectiveYield computes the current effective Yield generation of a data resource.
func CalculateEffectiveYield(instance *model.ResourceInstance, field *model.Field, cc *cache.CardCache) int64 {
	if !instance.FaceUp || instance.CurrentYield == nil {
		return 0
	}

	card := cc.Get(instance.CardID)
	if card == nil {
		return *instance.CurrentYield
	}

	stats, err := model.ParseDataStats(card.Stats)
	if err != nil {
		return *instance.CurrentYield
	}

	// 1. Base value
	baseYield := float64(stats.Yield)

	// 2. Rank multiplier
	baseYield *= float64(RankMultiplier(instance.Rank))

	// 3. Instance Family modifier
	if instance.InstanceFamily != nil {
		tpMult, _ := InstanceFamilyMultiplier(instance.InstanceFamily)
		baseYield *= tpMult
	}

	result := Truncate(baseYield)

	// 4. Elastic auto-scaling bonus (ln diminishing returns)
	if instance.ElasticBonus > 0 && card.Elastic && card.FreeTier > 0 {
		result += effectiveElasticBonus(instance.ElasticBonus, card.FreeTier)
	} else {
		result += instance.ElasticBonus
	}

	// 5. Platform card effects
	result += CalculatePlatformBonus(instance, field, "yield", cc)

	// 5b. Resource's own passive Yield bonuses
	result += CalculatePassiveYieldBonus(instance, field, cc)

	// 6. Attachment effects
	result += CalculateAttachmentBonus(instance, "yield", cc)

	// 7. Temporary effects
	for _, eff := range instance.TemporaryEffects {
		if eff.EffectType == "buff_yield" {
			result += eff.Value
		} else if eff.EffectType == "debuff_yield" {
			result -= eff.Value
		}
	}

	if result < 0 {
		result = 0
	}
	return result
}

// CalculateMaxAV computes the max AV based on card stats, rank, and family.
func CalculateMaxAV(instance *model.ResourceInstance, cc *cache.CardCache) int64 {
	card := cc.Get(instance.CardID)
	if card == nil {
		return instance.MaxAV
	}

	var baseAV float64
	if model.IsComputeType(card.CardType) {
		stats, err := model.ParseComputeStats(card.Stats)
		if err != nil {
			return instance.MaxAV
		}
		baseAV = float64(stats.Availability)
	} else if model.IsDataType(card.CardType) {
		stats, err := model.ParseDataStats(card.Stats)
		if err != nil {
			return instance.MaxAV
		}
		baseAV = float64(stats.Availability)
	}

	baseAV *= float64(RankMultiplier(instance.Rank))

	if instance.InstanceFamily != nil {
		_, avMult := InstanceFamilyMultiplier(instance.InstanceFamily)
		baseAV *= avMult
	}

	return Truncate(baseAV)
}

// RecalculateMaxTP recalculates the maximum TP after a rank/family change.
func RecalculateMaxTP(resource *model.ResourceInstance, card *model.CardDefinition) int64 {
	stats, err := model.ParseComputeStats(card.Stats)
	if err != nil {
		return 0
	}
	baseTP := float64(stats.Throughput)
	if stats.ThroughputMax != nil {
		baseTP = float64(*stats.ThroughputMax)
	}
	baseTP *= float64(RankMultiplier(resource.Rank))
	if resource.InstanceFamily != nil {
		tpMult, _ := InstanceFamilyMultiplier(resource.InstanceFamily)
		baseTP *= tpMult
	}
	return Truncate(baseTP)
}

// RecalculateMaxYield recalculates the maximum Yield after a rank/family change.
func RecalculateMaxYield(resource *model.ResourceInstance, card *model.CardDefinition) int64 {
	stats, err := model.ParseDataStats(card.Stats)
	if err != nil {
		return 0
	}
	baseYield := float64(stats.Yield)
	if stats.YieldMax != nil {
		baseYield = float64(*stats.YieldMax)
	}
	baseYield *= float64(RankMultiplier(resource.Rank))
	if resource.InstanceFamily != nil {
		tpMult, _ := InstanceFamilyMultiplier(resource.InstanceFamily)
		baseYield *= tpMult
	}
	return Truncate(baseYield)
}

// --- Helpers ---

// RankMultiplier returns 1, 2, or 3 for small, medium, large.
func RankMultiplier(rank string) int64 {
	switch rank {
	case model.RankSmall:
		return 1
	case model.RankMedium:
		return 2
	case model.RankLarge:
		return 3
	default:
		return 1
	}
}

// InstanceFamilyMultiplier returns (tpMult, avMult).
// M: balanced (1.0, 1.0), C: compute-optimized (1.5, 0.75), R: reliability-optimized (0.75, 1.5)
func InstanceFamilyMultiplier(family *string) (float64, float64) {
	if family == nil {
		return 1.0, 1.0
	}
	switch *family {
	case model.FamilyC:
		return 1.5, 0.75
	case model.FamilyR:
		return 0.75, 1.5
	default: // M or unknown
		return 1.0, 1.0
	}
}

// Truncate does integer truncation (floor towards zero).
func Truncate(val float64) int64 {
	return int64(val)
}

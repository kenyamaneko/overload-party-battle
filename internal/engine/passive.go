package engine

import (
	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// CalculatePassiveTPBonus calculates TP bonus from the card's passive effects.
func CalculatePassiveTPBonus(instance *model.ResourceInstance, field *model.Field, cc *cache.CardCache) int64 {
	if !instance.FaceUp {
		return 0
	}
	card := cc.Get(instance.CardID)
	if card == nil || len(card.PassiveEffects) == 0 {
		return 0
	}

	bonus := int64(0)
	for _, passiveEffect := range card.PassiveEffects {
		bonus += applyPassiveEffect(passiveEffect, instance, field, cc, "tp")
	}
	return bonus
}

// CalculatePassiveYieldBonus calculates Yield bonus from the card's passive effects.
func CalculatePassiveYieldBonus(instance *model.ResourceInstance, field *model.Field, cc *cache.CardCache) int64 {
	if !instance.FaceUp {
		return 0
	}
	card := cc.Get(instance.CardID)
	if card == nil || len(card.PassiveEffects) == 0 {
		return 0
	}

	bonus := int64(0)
	for _, passiveEffect := range card.PassiveEffects {
		bonus += applyPassiveEffect(passiveEffect, instance, field, cc, "yield")
	}
	return bonus
}

// applyPassiveEffect applies a single passive effect and returns the bonus value.
func applyPassiveEffect(pe model.PassiveEffect, instance *model.ResourceInstance, field *model.Field, cc *cache.CardCache, statType string) int64 {
	cfg := pe.GetConfig()

	switch pe.Type {
	case model.PassiveTPPerBackendDB:
		if statType != "tp" {
			return 0
		}
		return calculateTPPerBackendDB(instance, field, cfg, cc)

	case model.PassiveTPPerBackendData:
		if statType != "tp" {
			return 0
		}
		return calculateTPPerBackendData(instance, field, cfg, cc)

	case model.PassiveTPIfCardTypeOnField:
		if statType != "tp" {
			return 0
		}
		return calculateTPIfCardTypeOnField(field, cfg, cc)

	case model.PassiveYieldPerOtherDB:
		if statType != "yield" {
			return 0
		}
		return calculateDVPerOtherDB(instance, field, cfg, cc)

	case model.PassiveYieldIfCardOnField:
		if statType != "yield" {
			return 0
		}
		return calculateDVIfCardOnField(field, cfg, cc)

	default:
		return 0
	}
}

// calculateTPPerBackendDB: TP bonus per backend DB card only.
func calculateTPPerBackendDB(instance *model.ResourceInstance, field *model.Field, cfg *model.PassiveEffectConfig, cc *cache.CardCache) int64 {
	dbCount := effect.CountBackendDBs(field, cfg.Faction, cc)

	// Special handling for Multi-Model cards (count as 2)
	for _, res := range field.Backend {
		if res == nil || !res.FaceUp || (cfg.ExcludeSelf && res.InstanceID == instance.InstanceID) {
			continue
		}
		for _, multiModelCardNo := range cfg.MultiModelCards {
			if res.CardID == multiModelCardNo {
				dbCount += 1 // Add 1 more (already counted once by CountBackendDBs)
			}
		}
	}

	return int64(dbCount) * cfg.BonusPerCard
}

// calculateTPPerBackendData: TP bonus per backend data resource (DB + ObjectStorage) (e.g., #27 天気使い AI - 智の解放者<オープナー>).
func calculateTPPerBackendData(instance *model.ResourceInstance, field *model.Field, cfg *model.PassiveEffectConfig, cc *cache.CardCache) int64 {
	count := 0

	for _, res := range field.Backend {
		if res == nil || !res.FaceUp || (cfg.ExcludeSelf && res.InstanceID == instance.InstanceID) {
			continue
		}
		resCard := cc.Get(res.CardID)
		if resCard == nil {
			continue
		}
		if cfg.Faction != "" && resCard.Faction != cfg.Faction {
			continue
		}
		// Count both DB types and ObjectStorage
		if model.IsDataType(resCard.CardType) {
			count++
			// Special handling for Multi-Model cards (count as 2)
			for _, multiModelCardNo := range cfg.MultiModelCards {
				if res.CardID == multiModelCardNo {
					count++ // Add 1 more
				}
			}
		}
	}

	return int64(count) * cfg.BonusPerCard
}

// calculateTPIfCardTypeOnField: Flat TP bonus if specific card type exists (e.g., #6 SD Serverless - ラムネ, #76 調律部 Low-Code - アピエッタ).
func calculateTPIfCardTypeOnField(field *model.Field, cfg *model.PassiveEffectConfig, cc *cache.CardCache) int64 {
	for _, cardType := range cfg.CardTypes {
		if effect.HasCardTypeOnField(field, cardType, cfg.Faction, cc) {
			return cfg.FlatBonus
		}
	}
	return 0
}

// calculateDVPerOtherDB: DV bonus per other DB card (e.g., #8 SD DestributedDB - オオロバ).
// Only counts DB-type resources (Database, CacheDB), not ObjectStorage.
func calculateDVPerOtherDB(instance *model.ResourceInstance, field *model.Field, cfg *model.PassiveEffectConfig, cc *cache.CardCache) int64 {
	count := int64(0)
	for _, res := range field.Backend {
		if res == nil || !res.FaceUp || res.InstanceID == instance.InstanceID {
			continue
		}
		resCard := cc.Get(res.CardID)
		if resCard == nil {
			continue
		}
		if cfg.Faction != "" && resCard.Faction != cfg.Faction {
			continue
		}
		// Only count DB types, not ObjectStorage
		if model.IsDBType(resCard.CardType) {
			count++
		}
	}
	return count * cfg.BonusPerCard
}

// calculateDVIfCardOnField: DV bonus if specific card exists (e.g., #10 SD DB - ダイナソー + #124 SD Cache - だっくん).
func calculateDVIfCardOnField(field *model.Field, cfg *model.PassiveEffectConfig, cc *cache.CardCache) int64 {
	for _, cardNo := range cfg.SpecificCardNos {
		if effect.HasCardOnField(field, cardNo) {
			return cfg.FlatBonus
		}
	}
	return 0
}

// CalculatePlatformBonus calculates bonuses from Platform cards in support zone.
func CalculatePlatformBonus(instance *model.ResourceInstance, field *model.Field, statType string, cc *cache.CardCache) int64 {
	if !instance.FaceUp {
		return 0
	}

	bonus := int64(0)

	for _, sup := range field.Support {
		if sup == nil || sup.DeployingTurnsLeft > 0 {
			continue
		}

		platformCard := cc.Get(sup.CardID)
		if platformCard == nil || len(platformCard.PlatformEffects) == 0 {
			continue
		}

		for _, pe := range platformCard.PlatformEffects {
			bonus += applyPlatformEffect(pe, instance, statType, cc)
		}
	}

	return bonus
}

// applyPlatformEffect applies a single platform effect to the target instance.
func applyPlatformEffect(pe model.PlatformEffect, target *model.ResourceInstance, statType string, cc *cache.CardCache) int64 {
	cfg := pe.GetConfig()
	targetCard := cc.Get(target.CardID)
	if targetCard == nil {
		return 0
	}

	// Check faction match
	if cfg.TargetFaction != "" && targetCard.Faction != cfg.TargetFaction {
		return 0
	}

	// Check card type match
	if len(cfg.TargetCardTypes) > 0 {
		matched := false
		for _, ct := range cfg.TargetCardTypes {
			if targetCard.CardType == ct {
				matched = true
				break
			}
		}
		if !matched {
			return 0
		}
	}

	switch pe.Type {
	case model.PlatformTPBonus:
		if statType == "tp" {
			return cfg.Bonus
		}
	case model.PlatformYieldBonus:
		if statType == "yield" {
			return cfg.Bonus
		}
	case model.PlatformAVBonus:
		if statType == "av" {
			return cfg.Bonus
		}
	}

	return 0
}

// CalculateAttachmentBonus calculates bonuses from attached cards.
func CalculateAttachmentBonus(instance *model.ResourceInstance, statType string, cc *cache.CardCache) int64 {
	if !instance.FaceUp {
		return 0
	}
	bonus := int64(0)

	for _, att := range instance.Attachments {
		attCard := cc.Get(att.CardID)
		if attCard == nil || len(attCard.AttachmentEffects) == 0 {
			continue
		}

		for _, ae := range attCard.AttachmentEffects {
			if cfg := ae.GetConfig(); cfg.StatType == statType {
				bonus += cfg.Bonus
			}
		}
	}

	return bonus
}


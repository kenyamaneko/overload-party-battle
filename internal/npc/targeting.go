package npc

import (
	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

// --- Target selection helpers for NPC category-based decisions ---

// weakestInZone returns the instanceID of the opponent resource with the lowest effective AV
// in the given zone ("frontend", "backend", or "" for any).
func weakestInZone(field *model.Field, zone string) *string {
	var best *model.ResourceInstance
	var bestAV int64 = 1<<63 - 1

	if zone == "" || zone == model.ZoneFrontend {
		for _, r := range field.Frontend {
			if r == nil || !r.FaceUp {
				continue
			}
			av := model.CalculateEffectiveAV(r)
			if av < bestAV {
				bestAV = av
				best = r
			}
		}
	}
	if zone == "" || zone == model.ZoneBackend {
		for _, r := range field.Backend {
			if r == nil || !r.FaceUp {
				continue
			}
			av := model.CalculateEffectiveAV(r)
			if av < bestAV {
				bestAV = av
				best = r
			}
		}
	}

	if best != nil {
		return &best.InstanceID
	}
	return nil
}

// strongestInZone returns the instanceID of the opponent resource with the highest
// TP or Yield (whichever is present) in the given zone.
func strongestInZone(field *model.Field, zone string, cc *cache.CardCache) *string {
	var best *model.ResourceInstance
	var bestValue int64

	scan := func(resources [3]*model.ResourceInstance) {
		for _, r := range resources {
			if r == nil || !r.FaceUp {
				continue
			}
			val := resourceValue(r, cc)
			if val > bestValue {
				bestValue = val
				best = r
			}
		}
	}

	if zone == "" || zone == model.ZoneFrontend {
		scan(field.Frontend)
	}
	if zone == "" || zone == model.ZoneBackend {
		scan(field.Backend)
	}

	if best != nil {
		return &best.InstanceID
	}
	return nil
}

// mostDamagedOwn returns the instanceID of the own resource with the most damage.
func mostDamagedOwn(field *model.Field) *string {
	var best *model.ResourceInstance
	var bestDamage int64

	for _, r := range field.Frontend {
		if r != nil && r.FaceUp && r.Damage > bestDamage {
			bestDamage = r.Damage
			best = r
		}
	}
	for _, r := range field.Backend {
		if r != nil && r.FaceUp && r.Damage > bestDamage {
			bestDamage = r.Damage
			best = r
		}
	}

	if best != nil && best.Damage > 0 {
		return &best.InstanceID
	}
	return nil
}

// countAllResources counts face-up resources in frontend + backend.
func countAllResources(field *model.Field) int {
	count := 0
	for _, r := range field.Frontend {
		if r != nil && r.FaceUp {
			count++
		}
	}
	for _, r := range field.Backend {
		if r != nil && r.FaceUp {
			count++
		}
	}
	return count
}

// countResourcesInZone counts face-up resources in a specific zone.
func countResourcesInZone(field *model.Field, zone string) int {
	count := 0
	if zone == "" || zone == model.ZoneFrontend {
		for _, r := range field.Frontend {
			if r != nil && r.FaceUp {
				count++
			}
		}
	}
	if zone == "" || zone == model.ZoneBackend {
		for _, r := range field.Backend {
			if r != nil && r.FaceUp {
				count++
			}
		}
	}
	return count
}

// hasDamagedResource returns true if any own face-up resource has damage > 0.
func hasDamagedResource(field *model.Field) bool {
	for _, r := range field.Frontend {
		if r != nil && r.FaceUp && r.Damage > 0 {
			return true
		}
	}
	for _, r := range field.Backend {
		if r != nil && r.FaceUp && r.Damage > 0 {
			return true
		}
	}
	return false
}

// hasFaceDownSupport returns true if the opponent has any face-down support card.
func hasFaceDownSupport(field *model.Field) bool {
	for _, s := range field.Support {
		if s != nil && s.FaceDown {
			return true
		}
	}
	return false
}

// hasPlatform returns true if the opponent has any Platform-type support card.
func hasPlatform(field *model.Field, cc *cache.CardCache) bool {
	for _, s := range field.Support {
		if s == nil {
			continue
		}
		card := cc.Get(s.CardID)
		if card != nil && card.CardType == "Platform" {
			return true
		}
	}
	return false
}

// firstPlatformID returns the instanceID of the first Platform in the field's support zone.
func firstPlatformID(field *model.Field, cc *cache.CardCache) *string {
	for _, s := range field.Support {
		if s == nil {
			continue
		}
		card := cc.Get(s.CardID)
		if card != nil && card.CardType == "Platform" {
			return &s.InstanceID
		}
	}
	return nil
}

// resourceValue returns a heuristic value for a resource (TP or Yield).
func resourceValue(r *model.ResourceInstance, cc *cache.CardCache) int64 {
	if r.CurrentTP != nil && *r.CurrentTP > 0 {
		return *r.CurrentTP
	}
	if r.CurrentYield != nil && *r.CurrentYield > 0 {
		return *r.CurrentYield
	}
	card := cc.Get(r.CardID)
	if card == nil {
		return 0
	}
	if model.IsComputeType(card.CardType) {
		stats, _ := model.ParseComputeStats(card.Stats)
		if stats != nil {
			return stats.Throughput
		}
	} else if model.IsDataType(card.CardType) {
		stats, _ := model.ParseDataStats(card.Stats)
		if stats != nil {
			return stats.Yield
		}
	}
	return 0
}

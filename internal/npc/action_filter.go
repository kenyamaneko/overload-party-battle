package npc

import (
	"strconv"
	"strings"

	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-common/model"
)

// filterByType returns available actions of the given type.
func filterByType(actions []engine.AvailableAction, actionType string) []engine.AvailableAction {
	var result []engine.AvailableAction
	for _, a := range actions {
		if a.Type == actionType {
			result = append(result, a)
		}
	}
	return result
}

// pickBestZone selects the best zone from validZones based on card type strategy.
// Compute→frontend first, ObjectStorage→backend first, DB types→backend only, Support→support only.
// usedZones tracks zones already claimed in this planning pass.
func pickBestZone(validZones []string, cardDef *model.CardDefinition, usedZones map[string]bool) string {
	available := filterZones(validZones, usedZones)

	if model.IsComputeType(cardDef.CardType) {
		// Prefer frontend (can attack), fallback to backend (monetize DV)
		if z := firstWithPrefix(available, "frontend_"); z != "" {
			return z
		}
		if z := firstWithPrefix(available, "backend_"); z != "" {
			return z
		}
	} else if cardDef.CardType == "ObjectStorage" {
		// Prefer backend (DV gen), fallback to frontend
		if z := firstWithPrefix(available, "backend_"); z != "" {
			return z
		}
		if z := firstWithPrefix(available, "frontend_"); z != "" {
			return z
		}
	} else if model.IsDBType(cardDef.CardType) {
		// Backend only
		if z := firstWithPrefix(available, "backend_"); z != "" {
			return z
		}
	} else if model.IsSupportType(cardDef.CardType) || model.IsImmediateType(cardDef.CardType) {
		if z := firstWithPrefix(available, "support_"); z != "" {
			return z
		}
	}

	// Fallback: first available
	if len(available) > 0 {
		return available[0]
	}
	return ""
}

// pickSupportZone selects the first support zone from validZones that hasn't been used.
func pickSupportZone(validZones []string, usedZones map[string]bool) string {
	for _, z := range validZones {
		if strings.HasPrefix(z, "support_") && !usedZones[z] {
			return z
		}
	}
	return ""
}

// parseZoneStr converts "frontend_0" to slotPosition{Zone:"frontend", Index:0}.
func parseZoneStr(zone string) *slotPosition {
	parts := strings.SplitN(zone, "_", 2)
	if len(parts) != 2 {
		return nil
	}
	idx, err := strconv.Atoi(parts[1])
	if err != nil {
		return nil
	}
	return &slotPosition{Zone: parts[0], Index: idx}
}

// resolveCardNoForInstance looks up the CardID for a resource or support instance.
func resolveCardNoForInstance(instanceID string, field *model.Field) int64 {
	for _, r := range field.Frontend {
		if r != nil && r.InstanceID == instanceID {
			return r.CardID
		}
	}
	for _, r := range field.Backend {
		if r != nil && r.InstanceID == instanceID {
			return r.CardID
		}
	}
	for _, s := range field.Support {
		if s != nil && s.InstanceID == instanceID {
			return s.CardID
		}
	}
	return 0
}

// findBestTargetFromValid picks the target with lowest effective AV from validTargets.
func findBestTargetFromValid(validTargets []string, oppField *model.Field) string {
	if len(validTargets) == 0 {
		return ""
	}

	// Build instanceID → resource map
	resMap := make(map[string]*model.ResourceInstance, 6)
	for _, r := range oppField.Frontend {
		if r != nil {
			resMap[r.InstanceID] = r
		}
	}
	for _, r := range oppField.Backend {
		if r != nil {
			resMap[r.InstanceID] = r
		}
	}

	var bestID string
	var bestAV int64 = 1<<63 - 1
	for _, id := range validTargets {
		r := resMap[id]
		if r == nil {
			continue
		}
		av := model.CalculateEffectiveAV(r)
		if av < bestAV {
			bestAV = av
			bestID = id
		}
	}
	return bestID
}

// filterZones returns zones that haven't been used yet.
func filterZones(validZones []string, usedZones map[string]bool) []string {
	var result []string
	for _, z := range validZones {
		if !usedZones[z] {
			result = append(result, z)
		}
	}
	return result
}

// firstWithPrefix returns the first string in the slice that has the given prefix.
func firstWithPrefix(zones []string, prefix string) string {
	for _, z := range zones {
		if strings.HasPrefix(z, prefix) {
			return z
		}
	}
	return ""
}

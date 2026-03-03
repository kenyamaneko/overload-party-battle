package engine

import (
	"fmt"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// AvailableAction represents a single valid action the player can take.
// The Type field determines which optional fields are populated; unused fields
// are omitted from JSON via omitempty.
type AvailableAction struct {
	Type              string   `json:"type"`
	HandInstanceID    string   `json:"hand_instance_id,omitempty"`
	CardID            int64    `json:"card_id,omitempty"`
	ValidZones        []string `json:"valid_zones,omitempty"`
	SourceInstanceID  string   `json:"source_instance_id,omitempty"`
	ValidTargets      []string `json:"valid_targets,omitempty"`
	TargetRank        string   `json:"target_rank,omitempty"`
	NeedsFamily       bool     `json:"needs_family,omitempty"`
	RemainingCapacity int64    `json:"remaining_capacity,omitempty"`
	EffectTargetType  string   `json:"effect_target_type,omitempty"`
	RequiredCount     int      `json:"required_count,omitempty"`
	ChoiceOptions     []string `json:"choice_options,omitempty"`
}

// TurnControls represents game-flow controls that are not tied to any card.
// Sent as a separate WS message alongside game_state.
type TurnControls struct {
	CanEndPhase     bool `json:"can_end_phase"`
	DiscardRequired int  `json:"discard_required"`
}

// ComputeTurnControls determines the game-flow controls for the active player.
func ComputeTurnControls(state *model.GameState, hand []model.HandCard) TurnControls {
	tc := TurnControls{}

	switch state.CurrentPhase {
	case model.PhaseMain, model.PhaseBattle:
		tc.CanEndPhase = true
	}

	if state.CurrentPhase == model.PhaseEnd {
		required := len(hand) - model.HandLimit
		if required > 0 {
			tc.DiscardRequired = required
		}
	}

	return tc
}

// ComputeAvailableActions enumerates all valid actions for the active player
// in the current game state. This is a read-only computation.
func ComputeAvailableActions(
	state *model.GameState,
	game *model.Game,
	playerNum int64,
	myField *model.Field,
	oppField *model.Field,
	hand []model.HandCard,
	budget int64,
	insightPool int64,
	cc *cache.CardCache,
	effects *effect.EffectRegistry,
) []AvailableAction {
	var actions []AvailableAction

	switch state.CurrentPhase {
	case model.PhaseMain:
		actions = append(actions, enumeratePlayCardActions(myField, hand, cc, effects, state.CurrentTurn)...)
		actions = append(actions, enumerateScaleUpActions(myField, cc)...)
		actions = append(actions, enumerateDistributeYieldActions(state, myField, insightPool, cc)...)
		actions = append(actions, enumerateActivateEffectActions(myField, oppField, cc, effects)...)
		actions = append(actions, enumerateMigrateActions(myField, cc)...)

	case model.PhaseBattle:
		actions = append(actions, enumerateAttackActions(myField, oppField, cc)...)
		actions = append(actions, enumerateActivateEffectActions(myField, oppField, cc, effects)...)
	}

	return actions
}

// --- play_card ---

func enumeratePlayCardActions(myField *model.Field, hand []model.HandCard, cc *cache.CardCache, effects *effect.EffectRegistry, currentTurn int64) []AvailableAction {
	var actions []AvailableAction

	for _, hc := range hand {
		cardDef := cc.Get(hc.CardID)
		if cardDef == nil {
			continue
		}

		// Attachment cards handled separately
		if cardDef.CardType == "Attachment" {
			targets := enumerateAttachmentTargets(myField)
			if len(targets) > 0 {
				actions = append(actions, AvailableAction{
					Type:           model.ActionPlayCard,
					HandInstanceID: hc.InstanceID,
					CardID:         hc.CardID,
					ValidTargets:   targets,
				})
			}
			continue
		}

		// Incident: 1 per turn limit + blocked on T1 (first player cannot use)
		if cardDef.CardType == "Incident" {
			if myField.IncidentPlayedThisTurn || isFirstTurn(currentTurn) {
				continue
			}
		}

		var validZones []string

		// Frontend eligibility
		if model.IsFrontendEligible(cardDef.CardType) {
			for i := 0; i < 3; i++ {
				if myField.Frontend[i] == nil {
					validZones = append(validZones, fmt.Sprintf("frontend_%d", i))
				}
			}
		}

		// Backend eligibility
		if model.IsBackendEligible(cardDef.CardType) {
			for i := 0; i < 3; i++ {
				if myField.Backend[i] == nil {
					validZones = append(validZones, fmt.Sprintf("backend_%d", i))
				}
			}
		}

		// Support zone (Platform, Reactive, Strategy, Incident)
		if model.IsSupportType(cardDef.CardType) || model.IsImmediateType(cardDef.CardType) {
			for i := 0; i < 3; i++ {
				if myField.Support[i] == nil {
					validZones = append(validZones, fmt.Sprintf("support_%d", i))
				}
			}
		}

		if len(validZones) > 0 {
			action := AvailableAction{
				Type:           model.ActionPlayCard,
				HandInstanceID: hc.InstanceID,
				CardID:         hc.CardID,
				ValidZones:     validZones,
			}
			if effects != nil {
				if opts := effects.GetChoiceOptions(cardDef.CardNo, effect.TriggerDeploy); len(opts) > 0 {
					action.ChoiceOptions = opts
				}
			}
			actions = append(actions, action)
		}
	}

	return actions
}

func enumerateAttachmentTargets(myField *model.Field) []string {
	var targets []string
	for _, res := range myField.Frontend {
		if res != nil && res.FaceUp && len(res.Attachments) < model.MaxAttachments {
			targets = append(targets, res.InstanceID)
		}
	}
	for _, res := range myField.Backend {
		if res != nil && res.FaceUp && len(res.Attachments) < model.MaxAttachments {
			targets = append(targets, res.InstanceID)
		}
	}
	return targets
}

// --- attack ---

func enumerateAttackActions(myField *model.Field, oppField *model.Field, cc *cache.CardCache) []AvailableAction {
	// Determine valid targets on opponent's field (face-up only)
	hasFrontend := model.HasFrontendResources(oppField)
	var validTargets []string
	if hasFrontend {
		for _, res := range oppField.Frontend {
			if res != nil && res.FaceUp {
				validTargets = append(validTargets, res.InstanceID)
			}
		}
	} else {
		for _, res := range oppField.Backend {
			if res != nil && res.FaceUp {
				validTargets = append(validTargets, res.InstanceID)
			}
		}
	}

	if len(validTargets) == 0 {
		return nil
	}

	var actions []AvailableAction
	for _, res := range myField.Frontend {
		if res == nil || !res.FaceUp || res.HasAttacked {
			continue
		}
		if res.MigratingFrom != nil {
			continue // Migration target: locked
		}
		if model.HasTemporaryEffect(res, "cannot_operate") {
			continue
		}
		card := cc.Get(res.CardID)
		if card == nil || !model.IsComputeType(card.CardType) {
			continue
		}
		actions = append(actions, AvailableAction{
			Type:             model.ActionAttack,
			SourceInstanceID: res.InstanceID,
			ValidTargets:     validTargets,
		})
	}
	return actions
}

// --- scale_up ---

func enumerateScaleUpActions(myField *model.Field, cc *cache.CardCache) []AvailableAction {
	var actions []AvailableAction

	zones := [][3]*model.ResourceInstance{myField.Frontend, myField.Backend}
	for _, zone := range zones {
		for _, res := range zone {
			if res == nil || !res.FaceUp {
				continue
			}
			card := cc.Get(res.CardID)
			if card == nil || !card.Resizable {
				continue
			}
			if res.Rank == model.RankLarge {
				continue
			}

			targetRank := model.RankMedium
			if res.Rank == model.RankMedium {
				targetRank = model.RankLarge
			}

			actions = append(actions, AvailableAction{
				Type:             model.ActionScaleUp,
				SourceInstanceID: res.InstanceID,
				TargetRank:       targetRank,
				NeedsFamily:      res.Rank == model.RankSmall,
			})
		}
	}
	return actions
}

// --- distribute_yield ---

func enumerateDistributeYieldActions(state *model.GameState, myField *model.Field, insightPool int64, cc *cache.CardCache) []AvailableAction {
	if isFirstTurn(state.CurrentTurn) {
		return nil
	}
	if insightPool <= 0 {
		return nil
	}

	var actions []AvailableAction
	for _, res := range myField.Backend {
		if res == nil || !res.FaceUp {
			continue
		}
		if res.MigratingFrom != nil {
			continue // Migration target: locked
		}
		card := cc.Get(res.CardID)
		if card == nil || !model.IsComputeType(card.CardType) {
			continue
		}

		effectiveTP := CalculateEffectiveTP(res, myField, cc)
		remaining := effectiveTP - res.MonetizedAmount
		if remaining <= 0 {
			continue
		}

		actions = append(actions, AvailableAction{
			Type:              model.ActionDistributeYield,
			SourceInstanceID:  res.InstanceID,
			RemainingCapacity: remaining,
		})
	}
	return actions
}

// --- activate_effect ---

func enumerateActivateEffectActions(myField *model.Field, oppField *model.Field, cc *cache.CardCache, effects *effect.EffectRegistry) []AvailableAction {
	if effects == nil {
		return nil
	}

	var actions []AvailableAction

	// Check frontend + backend resources (face-up only)
	zones := [][3]*model.ResourceInstance{myField.Frontend, myField.Backend}
	for _, zone := range zones {
		for _, res := range zone {
			if res == nil || !res.FaceUp || res.EffectUsedThisTurn {
				continue
			}
			if res.MigratingFrom != nil {
				continue // Migration target: locked
			}
			if model.HasTemporaryEffect(res, "cannot_operate") {
				continue
			}

			card := cc.Get(res.CardID)
			if card == nil {
				continue
			}

			if !effects.HasEffect(card.CardNo, effect.TriggerActivate) {
				continue
			}

			action := AvailableAction{
				Type:             model.ActionActivateEffect,
				SourceInstanceID: res.InstanceID,
			}

			info := effects.GetEffectInfo(card.CardNo, effect.TriggerActivate)
			if info != nil {
				action.EffectTargetType = string(info.TargetType)
				if info.TargetType == effect.TargetChoice {
					action.ValidTargets = enumerateEffectTargets(info, myField, oppField)
				}
			}

			actions = append(actions, action)
		}
	}

	// Check support zone (skip deploying platforms)
	for _, sup := range myField.Support {
		if sup == nil || sup.DeployingTurnsLeft > 0 {
			continue
		}

		if !effects.HasEffect(sup.CardID, effect.TriggerActivate) {
			continue
		}

		action := AvailableAction{
			Type:             model.ActionActivateEffect,
			SourceInstanceID: sup.InstanceID,
		}

		info := effects.GetEffectInfo(sup.CardID, effect.TriggerActivate)
		if info != nil {
			action.EffectTargetType = string(info.TargetType)
			if info.TargetType == effect.TargetChoice {
				action.ValidTargets = enumerateEffectTargets(info, myField, oppField)
			}
		}

		actions = append(actions, action)
	}

	return actions
}

// enumerateEffectTargets determines valid target instance IDs based on EffectInfo.
// Uses the category to determine whether the effect targets allies or enemies,
// and TargetZone to filter by zone.
func enumerateEffectTargets(info *effect.EffectInfo, myField *model.Field, oppField *model.Field) []string {
	isEnemy := info.HasCategory(effect.CatSingleDamage) ||
		info.HasCategory(effect.CatDebuff) ||
		info.HasCategory(effect.CatDestroyPlatform)

	targetField := myField
	if isEnemy {
		targetField = oppField
	}

	var targets []string
	zone := info.TargetZone

	if zone == "" || zone == model.ZoneFrontend {
		for _, res := range targetField.Frontend {
			if res != nil && res.FaceUp {
				targets = append(targets, res.InstanceID)
			}
		}
	}
	if zone == "" || zone == model.ZoneBackend {
		for _, res := range targetField.Backend {
			if res != nil && res.FaceUp {
				targets = append(targets, res.InstanceID)
			}
		}
	}

	return targets
}

// --- migrate ---

func enumerateMigrateActions(myField *model.Field, cc *cache.CardCache) []AvailableAction {
	var actions []AvailableAction

	// Collect all face-up resources as potential sources
	type candidate struct {
		res        *model.ResourceInstance
		deployTurns int64
	}
	var sources []candidate
	var allResources []candidate

	scanZone := func(zone [3]*model.ResourceInstance) {
		for _, res := range zone {
			if res == nil || !res.FaceUp {
				continue
			}
			card := cc.Get(res.CardID)
			if card == nil {
				continue
			}
			c := candidate{res: res, deployTurns: card.DeployTurns}
			allResources = append(allResources, c)
			// Source: must not already be migrating (either direction)
			if res.MigrationTarget == nil && res.MigratingFrom == nil {
				sources = append(sources, c)
			}
		}
	}
	scanZone(myField.Frontend)
	scanZone(myField.Backend)

	for _, src := range sources {
		var validTargets []string
		for _, tgt := range allResources {
			if tgt.res.InstanceID == src.res.InstanceID {
				continue
			}
			// Target must not already be a migration destination or source
			if tgt.res.MigratingFrom != nil || tgt.res.MigrationTarget != nil {
				continue
			}
			// Target's deploy_turns must be >= source's deploy_turns
			if tgt.deployTurns < src.deployTurns {
				continue
			}
			validTargets = append(validTargets, tgt.res.InstanceID)
		}
		if len(validTargets) > 0 {
			actions = append(actions, AvailableAction{
				Type:             model.ActionMigrate,
				SourceInstanceID: src.res.InstanceID,
				ValidTargets:     validTargets,
			})
		}
	}

	return actions
}

package npc

import (
	"encoding/json"
	"sort"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// FactionAI is a configurable AI that adapts behavior based on faction-specific parameters.
// It embeds StandardAI for shared logic (effect evaluation, targeting, discard) and overrides
// deploy/scale/battle decisions with faction-tuned values.
type FactionAI struct {
	*StandardAI
	config factionConfig
}

type factionConfig struct {
	name           string
	instanceFamily string // preferred instance family on first scale-up ("M", "C", "R")
}

var _ Strategy = (*FactionAI)(nil)

// newFactionAI creates a FactionAI from the shared factionParamsTable.
func newFactionAI(faction string, cc *cache.CardCache, reg *effect.EffectRegistry) *FactionAI {
	p := factionParamsTable[faction]
	return &FactionAI{
		StandardAI: NewStandardAI(cc, reg),
		config: factionConfig{
			name:           faction,
			instanceFamily: p.InstanceFamily,
		},
	}
}

// NewSDAI creates an economy/synergy-focused AI for the SD faction.
func NewSDAI(cc *cache.CardCache, reg *effect.EffectRegistry) *FactionAI {
	return newFactionAI(model.FactionSD, cc, reg)
}

// NewTenkiAI creates a defensive/resilient AI for the Tenki faction.
func NewTenkiAI(cc *cache.CardCache, reg *effect.EffectRegistry) *FactionAI {
	return newFactionAI(model.FactionTenki, cc, reg)
}

// NewSugarAI creates an aggressive tempo AI for the Sugar faction.
func NewSugarAI(cc *cache.CardCache, reg *effect.EffectRegistry) *FactionAI {
	return newFactionAI(model.FactionSugar, cc, reg)
}

// NewTunersAI creates a resilient tank AI for the Tuners faction.
func NewTunersAI(cc *cache.CardCache, reg *effect.EffectRegistry) *FactionAI {
	return newFactionAI(model.FactionTuners, cc, reg)
}

// GetFactionAI returns a faction-specific AI for the given faction name.
// Falls back to StandardAI if the faction is not recognized.
func GetFactionAI(faction string, cc *cache.CardCache, reg *effect.EffectRegistry) Strategy {
	switch faction {
	case model.FactionSD:
		return NewSDAI(cc, reg)
	case model.FactionTenki:
		return NewTenkiAI(cc, reg)
	case model.FactionSugar:
		return NewSugarAI(cc, reg)
	case model.FactionTuners:
		return NewTunersAI(cc, reg)
	default:
		return NewStandardAI(cc, reg)
	}
}

// DecideMainPhaseActions overrides StandardAI with faction-specific
// deploy ordering and scale-up preferences.
func (ai *FactionAI) DecideMainPhaseActions(state *model.GameState, game *model.Game, npcPlayerNum int64, available []engine.AvailableAction) []NPCAction {
	var actions []NPCAction

	hand, err := state.GetHand(npcPlayerNum)
	if err != nil {
		return endPhaseOnly()
	}
	field, err := state.GetField(npcPlayerNum)
	if err != nil {
		return endPhaseOnly()
	}
	oppField, err := state.GetField(model.OpponentNum(npcPlayerNum))
	if err != nil {
		return endPhaseOnly()
	}

	ctx := &decisionCtx{
		field:    field,
		oppField: oppField,
		hand:     hand,
		budget:   state.GetBudget(npcPlayerNum),
		ai:       ai.StandardAI,
	}

	usedZones := make(map[string]bool)

	// 1. Use Strategy/Incident cards from hand
	actions = append(actions, doImmediateActions(ctx, available, ai.cardCache, ai.effects, usedZones)...)

	// 2. Deploy resource cards from hand (with faction ordering)
	if ai.config.name == model.FactionTenki {
		// Use Tenki-specific deploy logic with #27/#32 combo priority
		actions = append(actions, doTenkiDeployActions(ctx, available, ai.cardCache, usedZones)...)
	} else {
		actions = append(actions, doDeployActions(ctx, available, ai.cardCache, usedZones)...)
	}

	// 3. Activate field resource/support effects
	activateActions := ai.decideActivateActions(ctx, available)
	actions = append(actions, activateActions...)

	// 4. Scale up with faction-preferred instance family
	actions = append(actions, doScaleUpActions(available, ai.config.instanceFamily)...)

	// 5. Distribute Yield
	insightPool := state.GetInsightPool(npcPlayerNum)
	if insightPool > 0 {
		actions = append(actions, doDistributeYieldActions(available, insightPool)...)
	}

	// 6. End phase
	actions = append(actions, makeEndPhaseAction())
	return actions
}

// DecideBattlePhaseActions overrides StandardAI with faction-specific attack floor.
func (ai *FactionAI) DecideBattlePhaseActions(state *model.GameState, game *model.Game, npcPlayerNum int64, available []engine.AvailableAction) []NPCAction {
	oppField, err := state.GetField(model.OpponentNum(npcPlayerNum))
	if err != nil {
		return endPhaseOnly()
	}
	return doBattleActions(available, oppField)
}

// Name returns the faction name for logging.
func (ai *FactionAI) Name() string {
	return ai.config.name
}

// Config returns the faction config for testing/inspection.
func (ai *FactionAI) Config() factionConfig {
	return ai.config
}

// hasCardOnField returns true if the specified card number exists on the field.
func hasCardOnField(field *model.Field, cardNo int64) bool {
	if field == nil {
		return false
	}
	for _, res := range field.Frontend {
		if res != nil && res.CardID == cardNo {
			return true
		}
	}
	for _, res := range field.Backend {
		if res != nil && res.CardID == cardNo {
			return true
		}
	}
	return false
}

// countTenkiDBOnField counts Tenki data cards on the backend.
func countTenkiDBOnField(field *model.Field) int {
	if field == nil {
		return 0
	}
	count := 0
	tenkiDBCards := map[int64]bool{29: true, 30: true, 31: true, 32: true, 33: true}
	for _, res := range field.Backend {
		if res != nil && tenkiDBCards[res.CardID] {
			count++
		}
	}
	return count
}

// doTenkiDeployActions deploys cards with Tenki-specific priorities.
// Prioritizes #32 (百花の天穹<コスモ>) first, then #27 (智の解放者<オープナー>) if #32 is on field,
// then other Tenki data cards to boost #27's throughput.
func doTenkiDeployActions(ctx *decisionCtx, available []engine.AvailableAction, cc *cache.CardCache, usedZones map[string]bool) []NPCAction {
	playActions := filterByType(available, model.ActionPlayCard)

	type deployCandidate struct {
		action         engine.AvailableAction
		cardDef        *model.CardDefinition
		basePriority   int // 0=compute, 1=data, 2=support
		aozoraPriority int // higher = deploy first
	}

	var candidates []deployCandidate
	for _, a := range playActions {
		card := cc.Get(a.CardID)
		if card == nil {
			continue
		}
		if model.IsImmediateType(card.CardType) || card.CardType == "Attachment" {
			continue
		}

		// Base priority by type
		basePri := 2
		if model.IsComputeType(card.CardType) || model.IsAIMLType(card.CardType) {
			basePri = 0
		} else if model.IsDataType(card.CardType) {
			basePri = 1
		}

		// Tenki-specific priority
		aozoraPri := 50 // Default

		switch a.CardID {
		case 32: // 百花の天穹<コスモ> - HIGHEST PRIORITY (enables #27 combo)
			aozoraPri = 100
		case 27: // 智の解放者<オープナー> - HIGH if #32 on field, MEDIUM otherwise
			if hasCardOnField(ctx.field, 32) {
				aozoraPri = 90
			} else {
				aozoraPri = 70
			}
		case 30: // 天気使い DB - ハヤテ - HIGHER if 2+ Tenki data cards on field
			if countTenkiDBOnField(ctx.field) >= 2 {
				aozoraPri = 85
			} else {
				aozoraPri = 60
			}
		case 29, 31, 33: // Other Tenki data cards - MEDIUM-HIGH
			aozoraPri = 65
		}

		candidates = append(candidates, deployCandidate{a, card, basePri, aozoraPri})
	}

	// Sort by: aozora priority DESC, then base priority ASC
	sort.Slice(candidates, func(i, j int) bool {
		if candidates[i].aozoraPriority != candidates[j].aozoraPriority {
			return candidates[i].aozoraPriority > candidates[j].aozoraPriority
		}
		return candidates[i].basePriority < candidates[j].basePriority
	})

	var actions []NPCAction
	deployed := make(map[string]bool)
	for _, c := range candidates {
		if deployed[c.action.HandInstanceID] {
			continue
		}

		zone := pickBestZone(c.action.ValidZones, c.cardDef, usedZones)
		if zone == "" {
			continue
		}

		payload := map[string]interface{}{
			"cardInstanceId": c.action.HandInstanceID,
			"position":       parseZoneStr(zone),
		}
		if len(c.action.ChoiceOptions) > 0 {
			choice := deployChoiceFor(c.action.CardID)
			if choice == "" {
				choice = c.action.ChoiceOptions[0]
			}
			payload["choiceData"] = map[string]string{"option": choice}
		}
		data, _ := json.Marshal(payload)
		actions = append(actions, NPCAction{ActionType: model.ActionPlayCard, Data: data})
		deployed[c.action.HandInstanceID] = true
		usedZones[zone] = true
	}

	return actions
}

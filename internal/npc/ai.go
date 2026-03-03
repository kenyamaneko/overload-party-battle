package npc

import (
	"encoding/json"
	"sort"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// NPCAction represents a single action the NPC wants to take.
type NPCAction struct {
	ActionType string
	Data       json.RawMessage
}

// Strategy defines the interface for NPC decision-making.
// The available parameter provides pre-computed valid actions from the engine;
// the AI chooses which to take and how (targets, zones, etc.).
type Strategy interface {
	DecideMainPhaseActions(state *model.GameState, game *model.Game, npcPlayerNum int64, available []engine.AvailableAction) []NPCAction
	DecideBattlePhaseActions(state *model.GameState, game *model.Game, npcPlayerNum int64, available []engine.AvailableAction) []NPCAction
	DecideDiscard(state *model.GameState, npcPlayerNum int64) []string
}

// StandardAI is the default rule-based NPC strategy.
type StandardAI struct {
	cardCache *cache.CardCache
	effects   *effect.EffectRegistry
}

// Compile-time interface check.
var _ Strategy = (*StandardAI)(nil)

// NewStandardAI creates a new StandardAI instance.
func NewStandardAI(cardCache *cache.CardCache, effects *effect.EffectRegistry) *StandardAI {
	return &StandardAI{cardCache: cardCache, effects: effects}
}

// DecideMainPhaseActions returns actions for the main phase.
// Priority: immediate cards → deploy resources → activate effects → scale up → distribute Yield → end phase.
func (ai *StandardAI) DecideMainPhaseActions(state *model.GameState, game *model.Game, npcPlayerNum int64, available []engine.AvailableAction) []NPCAction {
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
		ai:       ai,
	}

	usedZones := make(map[string]bool)

	// 1. Use Strategy/Incident cards from hand (evaluated by effect category)
	actions = append(actions, doImmediateActions(ctx, available, ai.cardCache, ai.effects, usedZones)...)

	// 2. Deploy resource cards from hand
	actions = append(actions, doDeployActions(ctx, available, ai.cardCache, usedZones)...)

	// 3. Activate field resource/support effects
	activateActions := ai.decideActivateActions(ctx, available)
	actions = append(actions, activateActions...)

	// 4. Scale up existing resources
	actions = append(actions, doScaleUpActions(available, model.FamilyM)...)

	// 5. Distribute Yield (monetize)
	insightPool := state.GetInsightPool(npcPlayerNum)
	if insightPool > 0 {
		actions = append(actions, doDistributeYieldActions(available, insightPool)...)
	}

	// 6. End phase
	actions = append(actions, makeEndPhaseAction())
	return actions
}

// DecideBattlePhaseActions returns attack actions for the battle phase.
func (ai *StandardAI) DecideBattlePhaseActions(state *model.GameState, game *model.Game, npcPlayerNum int64, available []engine.AvailableAction) []NPCAction {
	oppField, err := state.GetField(model.OpponentNum(npcPlayerNum))
	if err != nil {
		return endPhaseOnly()
	}
	return doBattleActions(available, oppField)
}

// doBattleActions selects attack targets from pre-validated available actions.
func doBattleActions(available []engine.AvailableAction, oppField *model.Field) []NPCAction {
	attackActions := filterByType(available, model.ActionAttack)

	var actions []NPCAction
	for _, a := range attackActions {
		target := findBestTargetFromValid(a.ValidTargets, oppField)
		if target == "" {
			continue
		}

		data, _ := json.Marshal(map[string]string{
			"attackerInstanceId": a.SourceInstanceID,
			"targetInstanceId":   target,
		})
		actions = append(actions, NPCAction{ActionType: model.ActionAttack, Data: data})
	}

	actions = append(actions, makeEndPhaseAction())
	return actions
}

// DecideDiscard returns card instance IDs to discard when hand exceeds limit.
func (ai *StandardAI) DecideDiscard(state *model.GameState, npcPlayerNum int64) []string {
	hand, err := state.GetHand(npcPlayerNum)
	if err != nil {
		return nil
	}

	discardCount := len(hand) - model.HandLimit
	if discardCount <= 0 {
		return nil
	}

	// Sort by maintenance cost ascending — discard cheapest cards first
	type cardValue struct {
		instanceID string
		cost       int64
	}
	var values []cardValue
	for _, hc := range hand {
		card := ai.cardCache.Get(hc.CardID)
		cost := int64(0)
		if card != nil {
			cost = model.MaintenanceCostFor(card)
		}
		values = append(values, cardValue{instanceID: hc.InstanceID, cost: cost})
	}
	sort.Slice(values, func(i, j int) bool {
		return values[i].cost < values[j].cost
	})

	var ids []string
	for i := 0; i < discardCount && i < len(values); i++ {
		ids = append(ids, values[i].instanceID)
	}
	return ids
}

// --- Internal helpers ---

// doImmediateActions filters play_card actions for immediate-type cards,
// evaluates them by effect category, and returns actions sorted by priority.
func doImmediateActions(ctx *decisionCtx, available []engine.AvailableAction, cc *cache.CardCache, effects *effect.EffectRegistry, usedZones map[string]bool) []NPCAction {
	playActions := filterByType(available, model.ActionPlayCard)

	type candidate struct {
		action     engine.AvailableAction
		cardDef    *model.CardDefinition
		priority   int
		choiceData json.RawMessage
	}

	var candidates []candidate
	for _, a := range playActions {
		card := cc.Get(a.CardID)
		if card == nil || !model.IsImmediateType(card.CardType) {
			continue
		}

		pri, use, choice := ctx.ai.evaluateCard(card.CardNo, effect.TriggerActivate, ctx)
		if !use {
			continue
		}

		candidates = append(candidates, candidate{a, card, pri, choice})
	}

	sort.Slice(candidates, func(i, j int) bool {
		return candidates[i].priority > candidates[j].priority
	})

	var actions []NPCAction
	for _, c := range candidates {
		zone := pickSupportZone(c.action.ValidZones, usedZones)
		if zone == "" {
			continue
		}

		payload := map[string]interface{}{
			"cardInstanceId": c.action.HandInstanceID,
			"position":       parseZoneStr(zone),
		}
		if c.choiceData != nil {
			payload["choiceData"] = json.RawMessage(c.choiceData)
		}
		data, _ := json.Marshal(payload)
		actions = append(actions, NPCAction{ActionType: model.ActionPlayCard, Data: data})
		usedZones[zone] = true
	}
	return actions
}

// decideActivateActions evaluates activate effects from available actions.
func (ai *StandardAI) decideActivateActions(ctx *decisionCtx, available []engine.AvailableAction) []NPCAction {
	activateActions := filterByType(available, model.ActionActivateEffect)

	type candidate struct {
		action     engine.AvailableAction
		cardNo     int64
		priority   int
		choiceData json.RawMessage
	}

	var candidates []candidate
	for _, a := range activateActions {
		cardNo := resolveCardNoForInstance(a.SourceInstanceID, ctx.field)
		if cardNo == 0 {
			continue
		}

		pri, use, choice := ai.evaluateCard(cardNo, effect.TriggerActivate, ctx)
		if !use {
			continue
		}

		// If effect needs target choice but evaluateCard didn't provide one,
		// select from the pre-validated ValidTargets
		if a.EffectTargetType == string(effect.TargetChoice) && choice == nil {
			if len(a.ValidTargets) == 0 {
				continue
			}
			target := ai.selectTargetFromValid(cardNo, a.ValidTargets, ctx)
			if target == nil {
				continue
			}
			choice, _ = json.Marshal(map[string]string{"instanceId": *target})
		}

		candidates = append(candidates, candidate{a, cardNo, pri, choice})
	}

	sort.Slice(candidates, func(i, j int) bool {
		return candidates[i].priority > candidates[j].priority
	})

	var actions []NPCAction
	for _, c := range candidates {
		payload := map[string]interface{}{
			"instanceId": c.action.SourceInstanceID,
		}
		if c.choiceData != nil {
			payload["choiceData"] = json.RawMessage(c.choiceData)
		}
		data, _ := json.Marshal(payload)
		actions = append(actions, NPCAction{ActionType: model.ActionActivateEffect, Data: data})
	}
	return actions
}

// selectTargetFromValid picks the best target from the pre-validated ValidTargets list.
func (ai *StandardAI) selectTargetFromValid(cardNo int64, validTargets []string, ctx *decisionCtx) *string {
	if len(validTargets) == 0 {
		return nil
	}

	validSet := make(map[string]bool, len(validTargets))
	for _, id := range validTargets {
		validSet[id] = true
	}

	// Try using existing heuristic-based selectTarget
	info := ai.effects.GetEffectInfo(cardNo, effect.TriggerActivate)
	if info != nil {
		target := ai.selectTarget(info, ctx)
		if target != nil && validSet[*target] {
			return target
		}
	}

	// Fallback: first valid target
	return &validTargets[0]
}

// doDeployActions filters play_card actions for resource-type cards and deploys them.
func doDeployActions(ctx *decisionCtx, available []engine.AvailableAction, cc *cache.CardCache, usedZones map[string]bool) []NPCAction {
	playActions := filterByType(available, model.ActionPlayCard)

	type deployCandidate struct {
		action   engine.AvailableAction
		cardDef  *model.CardDefinition
		priority int // 0=compute, 1=data, 2=support
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

		pri := 2
		if model.IsComputeType(card.CardType) {
			pri = 0
		} else if model.IsDataType(card.CardType) {
			pri = 1
		}
		candidates = append(candidates, deployCandidate{a, card, pri})
	}
	sort.Slice(candidates, func(i, j int) bool {
		return candidates[i].priority < candidates[j].priority
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
			choice := deployChoiceFor(c.cardDef.CardNo)
			if choice == "" {
				choice = c.action.ChoiceOptions[0] // fallback to first option
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

// doScaleUpActions filters scale_up actions and applies instance family.
func doScaleUpActions(available []engine.AvailableAction, family string) []NPCAction {
	scaleActions := filterByType(available, model.ActionScaleUp)

	var actions []NPCAction
	for _, a := range scaleActions {
		payload := map[string]interface{}{
			"componentInstanceId": a.SourceInstanceID,
			"targetRank":          a.TargetRank,
		}
		if a.NeedsFamily {
			payload["instanceFamily"] = family
		}
		data, _ := json.Marshal(payload)
		actions = append(actions, NPCAction{ActionType: model.ActionScaleUp, Data: data})
	}
	return actions
}

// doDistributeYieldActions distributes Yield greedily across available distribute_yield actions.
func doDistributeYieldActions(available []engine.AvailableAction, insightPool int64) []NPCAction {
	yieldActions := filterByType(available, model.ActionDistributeYield)
	if len(yieldActions) == 0 {
		return nil
	}

	type distribution struct {
		ComponentInstanceID string `json:"componentInstanceId"`
		Amount              int64  `json:"amount"`
	}
	var dists []distribution
	remaining := insightPool

	for _, a := range yieldActions {
		if remaining <= 0 {
			break
		}
		amount := a.RemainingCapacity
		if amount > remaining {
			amount = remaining
		}
		if amount > 0 {
			dists = append(dists, distribution{
				ComponentInstanceID: a.SourceInstanceID,
				Amount:              amount,
			})
			remaining -= amount
		}
	}

	if len(dists) == 0 {
		return nil
	}

	data, _ := json.Marshal(map[string]interface{}{
		"distributions": dists,
	})
	return []NPCAction{{ActionType: model.ActionDistributeYield, Data: data}}
}

type slotPosition struct {
	Zone  string `json:"zone"`
	Index int    `json:"index"`
}

// hasFieldSetup returns true if the field has at least one face-up frontend and backend resource.
func hasFieldSetup(field *model.Field) bool {
	hasFront := false
	for _, r := range field.Frontend {
		if r != nil && r.FaceUp {
			hasFront = true
			break
		}
	}
	hasBack := false
	for _, r := range field.Backend {
		if r != nil && r.FaceUp {
			hasBack = true
			break
		}
	}
	return hasFront && hasBack
}

func makeEndPhaseAction() NPCAction {
	return NPCAction{ActionType: model.ActionEndPhase, Data: json.RawMessage(`{}`)}
}

func endPhaseOnly() []NPCAction {
	return []NPCAction{makeEndPhaseAction()}
}

// deployChoiceFor returns the NPC's deploy-time choice for cards with BranchOnChoice.
// Returns "" if the card has no deploy branch.
func deployChoiceFor(cardNo int64) string {
	switch cardNo {
	case 7: // SD RDB - アデリース: 予約契約 — "use" saves budget long-term
		return "use"
	case 11: // SD Cache - メリー: Memcached (instant +400 budget) vs Redis (+200 Yield permanent)
		return "redis"
	case 125: // しゅがーLab Cache - メレンゲもりもりストア: same as SD Cache
		return "redis"
	default:
		return ""
	}
}

package engine

import (
	"encoding/json"
	"fmt"
	"sort"

	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

type AttackRequest struct {
	AttackerInstanceID string `json:"attackerInstanceId"`
	TargetInstanceID   string `json:"targetInstanceId"`
}

func processAttack(state *model.GameState, game *model.Game, playerNum int64, req AttackRequest, cc *cache.CardCache, effects *effect.EffectRegistry) (*ActionResult, error) {
	opponentNum := model.OpponentNum(playerNum)

	// Get fields
	myField, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get my field: %w", err)
	}
	oppField, err := state.GetField(opponentNum)
	if err != nil {
		return nil, fmt.Errorf("get opponent field: %w", err)
	}

	// 1. Find attacker (must be own compute card in frontend)
	attacker, attackerZone, attackerIdx := model.FindResourceByID(myField, req.AttackerInstanceID)
	if attacker == nil {
		return nil, fmt.Errorf("attacker %s not found on field", req.AttackerInstanceID)
	}
	if attackerZone != model.ZoneFrontend {
		return nil, fmt.Errorf("attacker must be in frontend zone")
	}

	attackerCard := cc.Get(attacker.CardID)
	if attackerCard == nil {
		return nil, fmt.Errorf("attacker card definition not found")
	}
	if !model.IsComputeType(attackerCard.CardType) {
		return nil, fmt.Errorf("only compute cards can attack")
	}

	// 1a. Attacker must be face-up
	if !attacker.FaceUp {
		return nil, fmt.Errorf("attacker is still deploying")
	}

	// 2. Check already attacked
	if attacker.HasAttacked {
		return nil, fmt.Errorf("attacker has already attacked this turn")
	}

	// 2a. Check cannot_operate (e.g. from Rate Limiter reactive)
	if model.HasTemporaryEffect(attacker, "cannot_operate") {
		return nil, fmt.Errorf("this resource cannot operate this turn")
	}

	// 3. Find defender (must be opponent's face-up resource)
	defender, defenderZone, defenderIdx := model.FindResourceByID(oppField, req.TargetInstanceID)
	if defender == nil {
		return nil, fmt.Errorf("defender %s not found on opponent's field", req.TargetInstanceID)
	}
	if !defender.FaceUp {
		return nil, fmt.Errorf("cannot attack a deploying resource")
	}

	// 5. Check targeting rules: can only attack backend if no frontend exists
	if defenderZone == model.ZoneBackend && model.HasFrontendResources(oppField) {
		return nil, fmt.Errorf("cannot attack backend while frontend resources exist")
	}

	// 4. Calculate damage (attacker's effective throughput)
	damage := CalculateEffectiveTP(attacker, myField, cc)

	// 8. Check defender's reactive effects before applying damage
	cancelled := false
	var reactiveEvents []model.GameEvent
	if effects != nil {
		cancelled, reactiveEvents = fireReactives(state, game, opponentNum, oppField,
			model.EventAttack, attacker, defender, cc, effects)
	}

	if cancelled {
		// Attack is cancelled by reactive — still deduct cost, but don't apply damage
		if err := state.SetField(playerNum, myField); err != nil {
			return nil, fmt.Errorf("set my field: %w", err)
		}
		if err := state.SetField(opponentNum, oppField); err != nil {
			return nil, fmt.Errorf("set opponent field: %w", err)
		}

		eventData, _ := json.Marshal(map[string]interface{}{
			"attackerId": req.AttackerInstanceID,
			"targetId":   req.TargetInstanceID,
			"cancelled":  true,
		})

		playerID := model.PlayerIDForNum(game, playerNum)
		result := &ActionResult{
			Events: []model.GameEvent{{
				GameID:    game.GameID,
				EventType: model.EventAttack,
				PlayerID:  &playerID,
				EventData: eventData,
				CreatedAt: time.Time{},
			}},
		}
		result.Events = append(result.Events, reactiveEvents...)
		return result, nil
	}

	// 9. Apply damage
	defender.Damage += damage
	effectiveAV := model.CalculateEffectiveAV(defender)

	// 10. Mark attacker as having attacked
	attacker.HasAttacked = true
	myField.Frontend[attackerIdx] = attacker

	// 11. Fire OnAttack trigger (attacker's effect after dealing damage)
	var onAttackEvents []model.GameEvent
	if effects != nil {
		onAttackEvents = fireOnAttack(state, game, playerNum, attacker, defender, cc, effects)
	}

	destroyed := effectiveAV <= 0
	var slaDeducted int64
	var onDestroyEvents []model.GameEvent

	if destroyed {
		// Get SLA penalty
		defenderCard := cc.Get(defender.CardID)
		if defenderCard != nil {
			if model.IsComputeType(defenderCard.CardType) {
				defStats, _ := model.ParseComputeStats(defenderCard.Stats)
				if defStats != nil {
					slaDeducted = defStats.SLAPenalty
				}
			} else if model.IsDataType(defenderCard.CardType) {
				defStats, _ := model.ParseDataStats(defenderCard.Stats)
				if defStats != nil {
					slaDeducted = defStats.SLAPenalty
				}
			}
		}

		// Deduct SLA penalty from defender's owner budget
		oppBudget := state.GetBudget(opponentNum)
		state.SetBudget(opponentNum, oppBudget-slaDeducted)

		// Fire OnDestroy triggers before removing from field
		if effects != nil {
			onDestroyEvents = fireOnDestroy(state, game, opponentNum, defender, myField, oppField, cc, effects)
		}

		// Clear migration link if destroyed resource was a migration source
		clearMigrationOnSourceDestroyed(oppField, defender)

		// Move to trash (host + attachments)
		trash, _ := state.GetTrash(opponentNum)
		trash = append(trash, defender.CardID)
		for _, att := range defender.Attachments {
			trash = append(trash, att.CardID)
		}
		_ = state.SetTrash(opponentNum, trash)

		// Remove from field
		if defenderZone == model.ZoneFrontend {
			oppField.Frontend[defenderIdx] = nil
		} else {
			oppField.Backend[defenderIdx] = nil
		}
	} else {
		// Elastic auto-scaling: Frontend resources scale TP when attacked
		if defenderZone == model.ZoneFrontend {
			defCard := cc.Get(defender.CardID)
			if defCard != nil && defCard.Elastic && defCard.ElasticIncrement > 0 {
				applyElasticBonus(defender, defCard, cc)
			}
		}

		// Update defender on field
		if defenderZone == model.ZoneFrontend {
			oppField.Frontend[defenderIdx] = defender
		} else {
			oppField.Backend[defenderIdx] = defender
		}
	}

	if err := state.SetField(playerNum, myField); err != nil {
		return nil, fmt.Errorf("set my field: %w", err)
	}
	if err := state.SetField(opponentNum, oppField); err != nil {
		return nil, fmt.Errorf("set opponent field: %w", err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"attackerId": req.AttackerInstanceID,
		"targetId":   req.TargetInstanceID,
		"damage":     damage,
		"destroyed":  destroyed,
		"slaPenalty": slaDeducted,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	result := &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventAttack,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}
	result.Events = append(result.Events, reactiveEvents...)
	result.Events = append(result.Events, onAttackEvents...)
	result.Events = append(result.Events, onDestroyEvents...)

	return result, nil
}

// fireOnAttack fires the attacker's OnAttack trigger if registered.
func fireOnAttack(state *model.GameState, game *model.Game, playerNum int64,
	attacker *model.ResourceInstance, target *model.ResourceInstance,
	cc *cache.CardCache, effects *effect.EffectRegistry) []model.GameEvent {

	reg, ok := effects.Get(attacker.CardID, effect.TriggerOnAttack)
	if !ok {
		return nil
	}

	ctx := &effect.EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: playerNum,
		Source:    attacker,
		Target:    target,
		CardCache: cc,
	}

	result, err := reg.Handler(ctx)
	if err != nil {
		return nil
	}
	if result != nil {
		return result.Events
	}
	return nil
}

// fireOnDestroy fires OnDestroy triggers for the destroyed card and allied cards.
// Triggers fire in DeployOrder (earliest deployed first).
func fireOnDestroy(state *model.GameState, game *model.Game, ownerNum int64,
	destroyed *model.ResourceInstance,
	attackerField *model.Field, ownerField *model.Field,
	cc *cache.CardCache, effects *effect.EffectRegistry) []model.GameEvent {

	type triggerEntry struct {
		cardNo      int64
		deployOrder int64
		source      *model.ResourceInstance
	}

	var triggers []triggerEntry

	// 1. The destroyed card's own OnDestroy
	if _, ok := effects.Get(destroyed.CardID, effect.TriggerOnDestroy); ok {
		triggers = append(triggers, triggerEntry{
			cardNo:      destroyed.CardID,
			deployOrder: destroyed.DeployOrder,
			source:      destroyed,
		})
	}

	// 2. Other allied cards with OnDestroy (e.g. #9 versioning, #30 failover_group)
	for _, res := range ownerField.Frontend {
		if res == nil || res.InstanceID == destroyed.InstanceID {
			continue
		}
		if _, ok := effects.Get(res.CardID, effect.TriggerOnDestroy); ok {
			triggers = append(triggers, triggerEntry{
				cardNo:      res.CardID,
				deployOrder: res.DeployOrder,
				source:      res,
			})
		}
	}
	for _, res := range ownerField.Backend {
		if res == nil || res.InstanceID == destroyed.InstanceID {
			continue
		}
		if _, ok := effects.Get(res.CardID, effect.TriggerOnDestroy); ok {
			triggers = append(triggers, triggerEntry{
				cardNo:      res.CardID,
				deployOrder: res.DeployOrder,
				source:      res,
			})
		}
	}

	// Sort by DeployOrder (earliest first)
	sort.Slice(triggers, func(i, j int) bool {
		return triggers[i].deployOrder < triggers[j].deployOrder
	})

	var allEvents []model.GameEvent
	for _, t := range triggers {
		reg, ok := effects.Get(t.cardNo, effect.TriggerOnDestroy)
		if !ok {
			continue
		}
		ctx := &effect.EffectContext{
			State:     state,
			Game:      game,
			PlayerNum: ownerNum,
			Source:    t.source,
			Target:    destroyed,
			CardCache: cc,
		}
		result, err := reg.Handler(ctx)
		if err != nil {
			continue
		}
		if result != nil {
			allEvents = append(allEvents, result.Events...)
		}
	}

	return allEvents
}

// fireReactives checks all support zone cards for reactive triggers.
// Returns true if the action should be cancelled, plus any generated events.
// Reactives fire in DeployOrder (earliest deployed first).
func fireReactives(state *model.GameState, game *model.Game, defenderPlayerNum int64,
	defenderField *model.Field, eventType string,
	attacker *model.ResourceInstance, target *model.ResourceInstance,
	cc *cache.CardCache, effects *effect.EffectRegistry) (bool, []model.GameEvent) {

	type reactiveEntry struct {
		sup         *model.SupportInstance
		deployOrder int64
		slotIdx     int
	}

	var reactives []reactiveEntry
	for i, sup := range defenderField.Support {
		if sup == nil {
			continue
		}
		if _, ok := effects.Get(sup.CardID, effect.TriggerReactive); ok {
			reactives = append(reactives, reactiveEntry{
				sup:         sup,
				deployOrder: sup.DeployOrder,
				slotIdx:     i,
			})
		}
	}

	// Sort by DeployOrder (earliest first)
	sort.Slice(reactives, func(i, j int) bool {
		return reactives[i].deployOrder < reactives[j].deployOrder
	})

	cancelled := false
	var allEvents []model.GameEvent

	for _, r := range reactives {
		reg, ok := effects.Get(r.sup.CardID, effect.TriggerReactive)
		if !ok {
			continue
		}

		ctx := &effect.EffectContext{
			State:     state,
			Game:      game,
			PlayerNum: defenderPlayerNum,
			Target:    target,
			CardCache: cc,
		}

		result, err := reg.Handler(ctx)
		if err != nil {
			continue
		}

		if result != nil {
			allEvents = append(allEvents, result.Events...)
			if result.CancelAction {
				cancelled = true
				// Flip face-down reactive to face-up when triggered
				if r.sup.FaceDown {
					r.sup.FaceDown = false
				}
				break // First cancelling reactive wins
			}
		}
	}

	return cancelled, allEvents
}

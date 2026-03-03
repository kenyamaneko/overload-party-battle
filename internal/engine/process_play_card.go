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

type PlayCardRequest struct {
	CardInstanceID string `json:"cardInstanceId"`
	Position       struct {
		Zone  string `json:"zone"`  // "frontend", "backend", "support"
		Index int    `json:"index"` // 0-2
	} `json:"position"`
	TargetInstanceID *string         `json:"targetInstanceId,omitempty"`
	ChoiceData       json.RawMessage `json:"choiceData,omitempty"` // for deploy-trigger branch choices
}

func processPlayCard(state *model.GameState, game *model.Game, playerNum int64, req PlayCardRequest, cc *cache.CardCache, effects *effect.EffectRegistry) (*ActionResult, error) {
	// 1. Find card in hand
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get hand: %w", err)
	}

	handIdx := -1
	var handCard model.HandCard
	for i, c := range hand {
		if c.InstanceID == req.CardInstanceID {
			handIdx = i
			handCard = c
			break
		}
	}
	if handIdx == -1 {
		return nil, fmt.Errorf("card %s not in hand", req.CardInstanceID)
	}

	cardDef := cc.Get(handCard.CardID)
	if cardDef == nil {
		return nil, fmt.Errorf("card definition %d not found", handCard.CardID)
	}

	// Incident: 1 per turn limit (server-side guard)
	if cardDef.CardType == "Incident" {
		field, _ := state.GetField(playerNum)
		if field != nil && field.IncidentPlayedThisTurn {
			return nil, fmt.Errorf("only 1 incident per turn allowed")
		}
	}

	// Attachment cards follow a separate flow
	if cardDef.CardType == "Attachment" {
		return processAttachCard(state, game, playerNum, hand, handIdx, handCard, cardDef, req, cc, effects)
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	// 4. Validate position and card type
	if err := validatePlayPosition(cardDef, field, req); err != nil {
		return nil, err
	}

	// 5. Remove card from hand
	hand = append(hand[:handIdx], hand[handIdx+1:]...)

	// 6. Assign deploy order
	deployOrder := state.NextDeployOrder()

	// 8. Place card on field
	var sourceInstance *model.ResourceInstance
	switch req.Position.Zone {
	case model.ZoneFrontend:
		instance, err := model.CreateResourceInstance(cardDef, state.NextInstanceID())
		if err != nil {
			return nil, fmt.Errorf("create resource instance: %w", err)
		}
		instance.DeployedOnTurn = state.CurrentTurn
		instance.DeployOrder = deployOrder
		field.Frontend[req.Position.Index] = instance
		if instance.FaceUp {
			field.HasHadActiveResource = true
		}
		sourceInstance = instance

	case model.ZoneBackend:
		instance, err := model.CreateResourceInstance(cardDef, state.NextInstanceID())
		if err != nil {
			return nil, fmt.Errorf("create resource instance: %w", err)
		}
		instance.DeployedOnTurn = state.CurrentTurn
		instance.DeployOrder = deployOrder
		field.Backend[req.Position.Index] = instance
		if instance.FaceUp {
			field.HasHadActiveResource = true
		}
		sourceInstance = instance

	case model.ZoneSupport:
		isReactive := cardDef.CardType == "Reactive"
		sup := &model.SupportInstance{
			InstanceID:  state.NextInstanceID(),
			CardID:      cardDef.CardNo,
			FaceDown:    isReactive || cardDef.DeployTurns > 0,
			DeployOrder: deployOrder,
		}
		if cardDef.DeployTurns > 0 {
			sup.DeployingTurnsLeft = cardDef.DeployTurns
		}
		field.Support[req.Position.Index] = sup
	}

	// Handle immediate cards (Strategy, Incident) — trash after effect
	if model.IsImmediateType(cardDef.CardType) {
		// Clear the support slot (it was placed temporarily)
		field.Support[req.Position.Index] = nil
		// Incident: mark as played this turn (1 per turn limit)
		if cardDef.CardType == "Incident" {
			field.IncidentPlayedThisTurn = true
		}
	}

	if err := state.SetField(playerNum, field); err != nil {
		return nil, fmt.Errorf("set field: %w", err)
	}
	if err := state.SetHand(playerNum, hand); err != nil {
		return nil, fmt.Errorf("set hand: %w", err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"cardId": handCard.CardID,
		"zone":   req.Position.Zone,
		"index":  req.Position.Index,
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	result := &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventPlayCard,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}

	// 9a. Fire activate trigger for immediate cards (Strategy, Incident)
	if effects != nil && model.IsImmediateType(cardDef.CardType) {
		if reg, ok := effects.Get(cardDef.CardNo, effect.TriggerActivate); ok {
			ctx := &effect.EffectContext{
				State:      state,
				Game:       game,
				PlayerNum:  playerNum,
				CardCache:  cc,
				ChoiceData: req.ChoiceData,
			}
			effectResult, err := reg.Handler(ctx)
			if err != nil {
				return nil, fmt.Errorf("immediate effect for card %d: %w", cardDef.CardNo, err)
			}
			if effectResult != nil {
				result.Events = append(result.Events, effectResult.Events...)
			}
		}
		// Move to trash after effect execution
		trash, _ := state.GetTrash(playerNum)
		trash = append(trash, cardDef.CardNo)
		_ = state.SetTrash(playerNum, trash)
	}

	// 9b. Fire on_enemy_deploy reactives on opponent's field
	if effects != nil && sourceInstance != nil {
		cancelled, reactiveEvents := fireDeployReactives(state, game, playerNum, sourceInstance, cc, effects)
		result.Events = append(result.Events, reactiveEvents...)
		if cancelled {
			// Reactive destroyed the deployed card — remove it from field, send to trash
			field2, err2 := state.GetField(playerNum)
			if err2 == nil {
				switch req.Position.Zone {
				case model.ZoneFrontend:
					field2.Frontend[req.Position.Index] = nil
				case model.ZoneBackend:
					field2.Backend[req.Position.Index] = nil
				}
				_ = state.SetField(playerNum, field2)
			}
			trash, _ := state.GetTrash(playerNum)
			trash = append(trash, cardDef.CardNo)
			_ = state.SetTrash(playerNum, trash)
			return result, nil
		}
	}

	// 9c. Fire deploy trigger for resource cards
	if effects != nil && sourceInstance != nil {
		if reg, ok := effects.Get(cardDef.CardNo, effect.TriggerDeploy); ok {
			ctx := &effect.EffectContext{
				State:      state,
				Game:       game,
				PlayerNum:  playerNum,
				Source:     sourceInstance,
				CardCache:  cc,
				ChoiceData: req.ChoiceData,
			}
			effectResult, err := reg.Handler(ctx)
			if err != nil {
				return nil, fmt.Errorf("deploy effect for card %d: %w", cardDef.CardNo, err)
			}
			if effectResult != nil {
				result.Events = append(result.Events, effectResult.Events...)
			}
		}
	}

	return result, nil
}

// fireDeployReactives checks the opponent's support zone for TriggerOnEnemyDeploy handlers
// and fires them against the newly deployed resource. Returns (cancelled, events).
// If cancelled=true, the caller must remove the deployed card from the field.
func fireDeployReactives(state *model.GameState, game *model.Game,
	deployerNum int64, deployed *model.ResourceInstance,
	cc *cache.CardCache, effects *effect.EffectRegistry) (bool, []model.GameEvent) {

	opponentNum := model.OpponentNum(deployerNum)
	oppField, err := state.GetField(opponentNum)
	if err != nil {
		return false, nil
	}

	type reactiveEntry struct {
		sup         *model.SupportInstance
		deployOrder int64
	}

	var reactives []reactiveEntry
	for _, sup := range oppField.Support {
		if sup == nil {
			continue
		}
		if _, ok := effects.Get(sup.CardID, effect.TriggerOnEnemyDeploy); ok {
			reactives = append(reactives, reactiveEntry{sup: sup, deployOrder: sup.DeployOrder})
		}
	}

	sort.Slice(reactives, func(i, j int) bool {
		return reactives[i].deployOrder < reactives[j].deployOrder
	})

	var allEvents []model.GameEvent
	cancelled := false

	for _, r := range reactives {
		reg, ok := effects.Get(r.sup.CardID, effect.TriggerOnEnemyDeploy)
		if !ok {
			continue
		}

		ctx := &effect.EffectContext{
			State:     state,
			Game:      game,
			PlayerNum: opponentNum,
			Target:    deployed,
			SupSource: r.sup,
			CardCache: cc,
		}

		result, err := reg.Handler(ctx)
		if err != nil {
			continue // guard condition not met — reactive doesn't fire
		}

		if result != nil {
			allEvents = append(allEvents, result.Events...)
			if result.CancelAction {
				// Flip reactive face-up when it triggers
				r.sup.FaceDown = false
				cancelled = true
				break
			}
		}
	}

	if err := state.SetField(opponentNum, oppField); err != nil {
		return cancelled, allEvents
	}

	return cancelled, allEvents
}

// processAttachCard handles playing an Attachment card onto a target resource.
// Attachment cards are free (cost 0) and each resource has MaxAttachments slots.
func processAttachCard(
	state *model.GameState, game *model.Game, playerNum int64,
	hand []model.HandCard, handIdx int, handCard model.HandCard,
	cardDef *model.CardDefinition, req PlayCardRequest,
	cc *cache.CardCache, effects *effect.EffectRegistry,
) (*ActionResult, error) {
	if req.TargetInstanceID == nil {
		return nil, fmt.Errorf("attachment requires targetInstanceId")
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	target, _, _ := model.FindResourceByID(field, *req.TargetInstanceID)
	if target == nil {
		return nil, fmt.Errorf("target resource %s not found on field", *req.TargetInstanceID)
	}

	if len(target.Attachments) >= model.MaxAttachments {
		return nil, fmt.Errorf("resource %s already has %d attachments (max %d)", *req.TargetInstanceID, len(target.Attachments), model.MaxAttachments)
	}

	// Add attachment to the target resource
	target.Attachments = append(target.Attachments, model.AttachmentRef{
		InstanceID: state.NextInstanceID(),
		CardID:     cardDef.CardNo,
	})

	// Remove card from hand
	hand = append(hand[:handIdx], hand[handIdx+1:]...)

	if err := state.SetField(playerNum, field); err != nil {
		return nil, fmt.Errorf("set field: %w", err)
	}
	if err := state.SetHand(playerNum, hand); err != nil {
		return nil, fmt.Errorf("set hand: %w", err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"cardId":     handCard.CardID,
		"targetId":   *req.TargetInstanceID,
		"attachType": "attachment",
	})

	playerID := model.PlayerIDForNum(game, playerNum)
	result := &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventAttachCard,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}

	// Fire deploy trigger if the attachment has one
	if effects != nil {
		if reg, ok := effects.Get(cardDef.CardNo, effect.TriggerDeploy); ok {
			ctx := &effect.EffectContext{
				State:      state,
				Game:       game,
				PlayerNum:  playerNum,
				Target:     target,
				CardCache:  cc,
				ChoiceData: req.ChoiceData,
			}
			effectResult, err := reg.Handler(ctx)
			if err != nil {
				return nil, fmt.Errorf("attachment deploy effect for card %d: %w", cardDef.CardNo, err)
			}
			if effectResult != nil {
				result.Events = append(result.Events, effectResult.Events...)
			}
		}
	}

	return result, nil
}

func validatePlayPosition(cardDef *model.CardDefinition, field *model.Field, req PlayCardRequest) error {
	idx := req.Position.Index
	if idx < 0 || idx > 2 {
		return fmt.Errorf("invalid slot index: %d", idx)
	}

	switch req.Position.Zone {
	case model.ZoneFrontend:
		if !model.IsFrontendEligible(cardDef.CardType) {
			return fmt.Errorf("card type %s cannot be placed in frontend", cardDef.CardType)
		}
		if field.Frontend[idx] != nil {
			return fmt.Errorf("frontend slot %d is occupied", idx)
		}

	case model.ZoneBackend:
		if !model.IsBackendEligible(cardDef.CardType) {
			return fmt.Errorf("card type %s cannot be placed in backend", cardDef.CardType)
		}
		if field.Backend[idx] != nil {
			return fmt.Errorf("backend slot %d is occupied", idx)
		}

	case model.ZoneSupport:
		if !model.IsSupportType(cardDef.CardType) && !model.IsImmediateType(cardDef.CardType) {
			return fmt.Errorf("card type %s cannot be placed in support", cardDef.CardType)
		}
		if field.Support[idx] != nil {
			return fmt.Errorf("support slot %d is occupied", idx)
		}

	default:
		return fmt.Errorf("invalid zone: %s", req.Position.Zone)
	}

	return nil
}

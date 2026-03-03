package engine

import (
	"fmt"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

const MaxChainLevel = 3

// PushToChain adds a new entry to the chain stack.
func PushToChain(state *model.GameState, entry model.ChainEntry) error {
	stack, err := state.GetChainStack()
	if err != nil {
		return fmt.Errorf("get chain stack: %w", err)
	}

	if len(stack) >= MaxChainLevel {
		return fmt.Errorf("chain level limit reached (%d)", MaxChainLevel)
	}

	// Reactive cannot chain on reactive
	if entry.ActionType == "reactive" {
		for _, existing := range stack {
			if existing.ActionType == "reactive" && !existing.Resolved {
				return fmt.Errorf("reactive cannot chain on reactive")
			}
		}
	}

	entry.ChainLevel = int64(len(stack)) + 1
	stack = append(stack, entry)

	return state.SetChainStack(stack)
}

// ResolveChain resolves the chain stack in LIFO order.
// Returns all game events generated during resolution.
func ResolveChain(state *model.GameState, game *model.Game, cc *cache.CardCache, effects *effect.EffectRegistry) ([]model.GameEvent, error) {
	stack, err := state.GetChainStack()
	if err != nil {
		return nil, fmt.Errorf("get chain stack: %w", err)
	}

	if len(stack) == 0 {
		return nil, nil
	}

	var allEvents []model.GameEvent

	// Resolve in LIFO order (last added = first resolved)
	for i := len(stack) - 1; i >= 0; i-- {
		entry := &stack[i]
		if entry.Resolved {
			continue
		}

		events, err := resolveChainEntry(state, game, entry, cc, effects)
		if err != nil {
			return nil, fmt.Errorf("resolve chain entry %d: %w", entry.ChainLevel, err)
		}
		allEvents = append(allEvents, events...)
		entry.Resolved = true
	}

	// Clear the chain stack
	if err := state.SetChainStack(nil); err != nil {
		return nil, fmt.Errorf("clear chain stack: %w", err)
	}

	return allEvents, nil
}

func resolveChainEntry(state *model.GameState, game *model.Game, entry *model.ChainEntry, cc *cache.CardCache, effects *effect.EffectRegistry) ([]model.GameEvent, error) {
	if effects == nil {
		return nil, nil
	}

	// Determine trigger type from chain action type
	trigger := chainActionToTrigger(entry.ActionType)

	playerNum := model.PlayerNumForID(game, entry.SourcePlayerID)
	if playerNum == 0 {
		return nil, fmt.Errorf("source player not found: %s", entry.SourcePlayerID)
	}

	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	source, _, _ := model.FindResourceByID(field, entry.SourceInstanceID)
	if source == nil {
		// Also check support zone
		for _, sup := range field.Support {
			if sup != nil && sup.InstanceID == entry.SourceInstanceID {
				reg, ok := effects.Get(sup.CardID, trigger)
				if ok {
					ctx := &effect.EffectContext{
						State:      state,
						Game:       game,
						PlayerNum:  playerNum,
						CardCache:  cc,
						ChoiceData: entry.EffectData,
					}
					result, err := reg.Handler(ctx)
					if err != nil {
						return nil, err
					}
					if result != nil {
						return result.Events, nil
					}
				}
				return nil, nil
			}
		}
		return nil, nil // Source destroyed before resolution
	}

	reg, ok := effects.Get(source.CardID, trigger)
	if !ok {
		return nil, nil
	}

	ctx := &effect.EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  playerNum,
		Source:     source,
		CardCache:  cc,
		ChoiceData: entry.EffectData,
	}

	result, err := reg.Handler(ctx)
	if err != nil {
		return nil, err
	}
	if result != nil {
		return result.Events, nil
	}
	return nil, nil
}

// chainActionToTrigger maps chain entry action types to effect trigger types.
func chainActionToTrigger(actionType string) effect.TriggerType {
	switch actionType {
	case "reactive":
		return effect.TriggerReactive
	case model.ActionAttack:
		return effect.TriggerOnAttack
	default:
		return effect.TriggerActivate
	}
}

// CanChainReactive checks if a reactive effect can be added to the current chain.
func CanChainReactive(state *model.GameState) (bool, error) {
	stack, err := state.GetChainStack()
	if err != nil {
		return false, err
	}

	if len(stack) == 0 {
		return false, nil // Nothing to chain on
	}

	if len(stack) >= MaxChainLevel {
		return false, nil
	}

	// Last entry must not be a reactive
	last := stack[len(stack)-1]
	return last.ActionType != "reactive", nil
}

// IsChainActive returns true if there's an unresolved chain in progress.
func IsChainActive(state *model.GameState) bool {
	stack, _ := state.GetChainStack()
	for _, entry := range stack {
		if !entry.Resolved {
			return true
		}
	}
	return false
}

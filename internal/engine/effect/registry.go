package effect

import (
	"encoding/json"
	"log"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

// TriggerType defines when an effect fires.
type TriggerType string

const (
	TriggerDeploy        TriggerType = "deploy"
	TriggerActivate      TriggerType = "activate"
	TriggerPassive       TriggerType = "passive"
	TriggerOnAttack      TriggerType = "on_attack"
	TriggerOnHit         TriggerType = "on_hit"
	TriggerOnDestroy     TriggerType = "on_destroy"
	TriggerReactive      TriggerType = "reactive"
	TriggerOnEnemyDeploy TriggerType = "on_enemy_deploy" // fires on owner's field when opponent deploys a resource
)

// EffectContext provides all data an effect handler needs.
type EffectContext struct {
	State      *model.GameState
	Game       *model.Game
	PlayerNum  int64
	Source     *model.ResourceInstance
	Target     *model.ResourceInstance
	SupSource  *model.SupportInstance // Set when the triggering card is a support-zone card
	CardCache  *cache.CardCache
	ChoiceData json.RawMessage
}

// EffectResult carries the outcome of an effect handler.
type EffectResult struct {
	Events       []model.GameEvent
	CancelAction bool // If true, the triggering action is cancelled
}

// EffectHandler is the function signature for all effect handlers.
type EffectHandler func(ctx *EffectContext) (*EffectResult, error)

// registryKey uniquely identifies an effect handler by card number and trigger type.
type registryKey struct {
	CardNo  int64
	Trigger TriggerType
}

// EffectRegistration pairs a card with its trigger type and handler.
type EffectRegistration struct {
	CardNo      int64
	TriggerType TriggerType
	Handler     EffectHandler
	Ops         []Op // Stored for NPC classification (may be nil for data-driven effects)
}

// PassiveDef holds a parsed passive effect definition for data-driven stat calculations.
type PassiveDef struct {
	CardNo        int64
	PassiveType   string // tp_bonus, yield_bonus, av_bonus, scale_cost_reduction, incident_reduction
	Scope         string // platform, resource, attachment
	TargetZone    string // frontend, backend, "" (any)
	TargetFaction string // "" (any)
	Value         int64
	Condition     func(octx *OpContext) bool
}

// EffectRegistry maps (card_no, trigger_type) to EffectRegistration.
type EffectRegistry struct {
	handlers map[registryKey]*EffectRegistration
	passives []PassiveDef
}

func NewEffectRegistry() *EffectRegistry {
	return &EffectRegistry{
		handlers: make(map[registryKey]*EffectRegistration),
	}
}

// Register registers a handler by card number and trigger type.
func (r *EffectRegistry) Register(cardNo int64, triggerType TriggerType, handler EffectHandler) {
	key := registryKey{CardNo: cardNo, Trigger: triggerType}
	r.handlers[key] = &EffectRegistration{
		CardNo:      cardNo,
		TriggerType: triggerType,
		Handler:     handler,
	}
}

// RegisterComposed builds a handler from ops via Compose and stores both
// the handler and the ops for NPC classification.
func (r *EffectRegistry) RegisterComposed(cardNo int64, triggerType TriggerType, ops ...Op) {
	key := registryKey{CardNo: cardNo, Trigger: triggerType}
	r.handlers[key] = &EffectRegistration{
		CardNo:      cardNo,
		TriggerType: triggerType,
		Handler:     Compose(ops...),
		Ops:         ops,
	}
}

// Get retrieves a registration by card number and trigger type.
func (r *EffectRegistry) Get(cardNo int64, trigger TriggerType) (*EffectRegistration, bool) {
	key := registryKey{CardNo: cardNo, Trigger: trigger}
	reg, ok := r.handlers[key]
	return reg, ok
}

// GetPassives returns all registered passive effect definitions.
func (r *EffectRegistry) GetPassives() []PassiveDef {
	return r.passives
}

// HasEffect checks if the card has any effect handler registered for the given trigger.
func (r *EffectRegistry) HasEffect(cardNo int64, trigger TriggerType) bool {
	_, ok := r.Get(cardNo, trigger)
	return ok
}

// RegistrationCount returns the total number of registered effect handlers.
func (r *EffectRegistry) RegistrationCount() int {
	return len(r.handlers)
}

// PassiveCount returns the total number of registered passive definitions.
func (r *EffectRegistry) PassiveCount() int {
	return len(r.passives)
}

// GetEffectInfo returns the classification of an effect for NPC decision-making.
// Returns nil if the card has no registered handler or no ops stored.
func (r *EffectRegistry) GetEffectInfo(cardNo int64, trigger TriggerType) *EffectInfo {
	reg, ok := r.Get(cardNo, trigger)
	if !ok || reg.Ops == nil {
		return nil
	}
	return ClassifyOps(reg.Ops)
}

// GetChoiceOptions returns the branch keys if the effect uses BranchOnChoice, nil otherwise.
func (r *EffectRegistry) GetChoiceOptions(cardNo int64, trigger TriggerType) []string {
	reg, ok := r.Get(cardNo, trigger)
	if !ok || reg.Ops == nil {
		return nil
	}
	for _, op := range reg.Ops {
		if bc, ok := op.(BranchOnChoice); ok {
			keys := make([]string, 0, len(bc.Branches))
			for k := range bc.Branches {
				keys = append(keys, k)
			}
			return keys
		}
	}
	return nil
}

// CardNosForTrigger returns all card numbers that have a handler for the given trigger.
func (r *EffectRegistry) CardNosForTrigger(trigger TriggerType) []int64 {
	var nos []int64
	for key := range r.handlers {
		if key.Trigger == trigger {
			nos = append(nos, key.CardNo)
		}
	}
	return nos
}

// BuildRegistryFromCards builds an EffectRegistry from card definitions in the cache.
func BuildRegistryFromCards(cc *cache.CardCache) *EffectRegistry {
	reg := NewEffectRegistry()
	cards := cc.All()

	for _, card := range cards {
		if len(card.Effects) == 0 || string(card.Effects) == "null" {
			continue
		}

		defs, err := ParseEffects(card.Effects)
		if err != nil {
			log.Printf("WARN: parse effects for card %d (%s): %v", card.CardNo, card.CardName, err)
			continue
		}

		for _, def := range defs {
			if def.Trigger == "passive" {
				passive := buildPassiveDef(card, def)
				if passive != nil {
					reg.passives = append(reg.passives, *passive)
				}
				continue
			}

			handler, err := BuildHandler(def)
			if err != nil {
				log.Printf("WARN: build handler for card %d (%s) trigger %s: %v",
					card.CardNo, card.CardName, def.Trigger, err)
				continue
			}

			trigger := TriggerType(def.Trigger)
			reg.Register(card.CardNo, trigger, handler)
		}
	}

	log.Printf("effect registry built: %d handlers, %d passives", len(reg.handlers), len(reg.passives))
	return reg
}

// buildPassiveDef converts a passive EffectDef into a PassiveDef.
func buildPassiveDef(card *model.CardDefinition, def EffectDef) *PassiveDef {
	val := ParseValue(def.Value)
	// Resolve static value — passives should use constant values.
	resolved, err := val.Resolve(nil)
	if err != nil {
		log.Printf("WARN: resolve passive value for card %d: %v", card.CardNo, err)
		return nil
	}

	pd := &PassiveDef{
		CardNo:        card.CardNo,
		PassiveType:   def.PassiveType,
		Scope:         def.Scope,
		TargetZone:    def.TargetZone,
		TargetFaction: def.TargetFaction,
		Value:         resolved,
	}

	if len(def.Condition) > 0 && string(def.Condition) != "null" {
		condFn, err := ParseCondition(def.Condition)
		if err != nil {
			log.Printf("WARN: parse passive condition for card %d: %v", card.CardNo, err)
			return nil
		}
		pd.Condition = condFn
	}

	return pd
}

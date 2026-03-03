package effect

import (
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

// SetCancelAction sets CancelAction=true on the result.
type SetCancelAction struct{}

func (op SetCancelAction) Execute(octx *OpContext) error {
	octx.Result.CancelAction = true
	return nil
}

// RequireFactionCount fails if faction count on own field < Min.
type RequireFactionCount struct {
	Faction string
	Min     int
}

func (op RequireFactionCount) Execute(octx *OpContext) error {
	field, err := octx.GetField(octx.Ctx.PlayerNum)
	if err != nil {
		return err
	}
	if CountFactionCards(field, op.Faction, octx.Ctx.CardCache) < op.Min {
		return fmt.Errorf("need %d+ %s cards on field", op.Min, op.Faction)
	}
	return nil
}

// RequireBudget fails if budget < Min.
type RequireBudget struct {
	Min int64
}

func (op RequireBudget) Execute(octx *OpContext) error {
	budget := octx.Ctx.State.GetBudget(octx.Ctx.PlayerNum)
	if budget < op.Min {
		return fmt.Errorf("insufficient budget: need %d, have %d", op.Min, budget)
	}
	return nil
}

// RequireMaxBudget fails if budget > Max.
type RequireMaxBudget struct {
	Max int64
}

func (op RequireMaxBudget) Execute(octx *OpContext) error {
	budget := octx.Ctx.State.GetBudget(octx.Ctx.PlayerNum)
	if budget > op.Max {
		return fmt.Errorf("budget too high: max %d, have %d", op.Max, budget)
	}
	return nil
}

// RequireOpponentBackend fails if opponent has no backend resources.
type RequireOpponentBackend struct{}

func (op RequireOpponentBackend) Execute(octx *OpContext) error {
	if CountOpponentBackend(octx.Ctx.State, octx.Ctx.PlayerNum) == 0 {
		return fmt.Errorf("opponent has no backend resources")
	}
	return nil
}

// GuardFaction verifies Target is a specific faction (and optionally card type).
type GuardFaction struct {
	Faction  string
	CardType string // "" = any
}

func (op GuardFaction) Execute(octx *OpContext) error {
	if octx.Ctx.Target == nil {
		return fmt.Errorf("no target")
	}
	card := octx.Ctx.CardCache.Get(octx.Ctx.Target.CardID)
	if card == nil {
		return fmt.Errorf("card %d not found", octx.Ctx.Target.CardID)
	}
	if op.Faction != "" && card.Faction != op.Faction {
		return fmt.Errorf("target is not %s faction", op.Faction)
	}
	if op.CardType != "" && !matchCardType(card.CardType, op.CardType) {
		return fmt.Errorf("target is not %s type", op.CardType)
	}
	return nil
}

// GuardNotSelf verifies Target is not the same instance as Source.
type GuardNotSelf struct{}

func (op GuardNotSelf) Execute(octx *OpContext) error {
	if octx.Ctx.Source == nil || octx.Ctx.Target == nil {
		return fmt.Errorf("source or target missing")
	}
	if octx.Ctx.Source.InstanceID == octx.Ctx.Target.InstanceID {
		return fmt.Errorf("target must be different from source")
	}
	return nil
}

// GuardTargetAV verifies target's effective AV <= MaxAV.
type GuardTargetAV struct {
	MaxAV int64
}

func (op GuardTargetAV) Execute(octx *OpContext) error {
	if octx.Ctx.Target == nil {
		return fmt.Errorf("no target")
	}
	av := model.CalculateEffectiveAV(octx.Ctx.Target)
	if av > op.MaxAV {
		return fmt.Errorf("target AV %d exceeds max %d", av, op.MaxAV)
	}
	return nil
}

// SurviveDestruction resets damage so effective AV = SurviveAV and cancels action.
type SurviveDestruction struct {
	SurviveAV int64
}

func (op SurviveDestruction) Execute(octx *OpContext) error {
	if octx.Ctx.Target == nil {
		return fmt.Errorf("no target to protect")
	}
	octx.Ctx.Target.Damage = octx.Ctx.Target.MaxAV - op.SurviveAV
	if octx.Ctx.Target.Damage < 0 {
		octx.Ctx.Target.Damage = 0
	}
	octx.Result.CancelAction = true
	return nil
}

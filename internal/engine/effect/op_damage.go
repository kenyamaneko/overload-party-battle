package effect

import "github.com/kenyamaneko/overload-party-common/model"

// DealDamage applies direct damage to selected resources.
type DealDamage struct {
	Sel   Selector
	Value Amount
}

func (op DealDamage) Execute(octx *OpContext) error {
	amount, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	targets, err := op.Sel.Select(octx)
	if err != nil {
		return err
	}
	for _, t := range targets {
		t.Damage += amount
	}
	return nil
}

// IncidentDamage applies damage with incident reduction calculation.
type IncidentDamage struct {
	Sel           Selector
	Value         Amount
	BudgetPenalty Amount // nil means no penalty
}

func (op IncidentDamage) Execute(octx *OpContext) error {
	baseDamage, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	targets, err := op.Sel.Select(octx)
	if err != nil {
		return err
	}

	// Determine the field for incident damage reduction
	// Incident targets are on the opponent's field
	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(oppNum)
	if err != nil {
		return err
	}

	for _, t := range targets {
		actualDamage := applyIncidentDamageReduction(baseDamage, t, field, octx.Ctx.CardCache)
		t.Damage += actualDamage
	}

	if op.BudgetPenalty != nil {
		penalty, err := op.BudgetPenalty.Resolve(octx)
		if err != nil {
			return err
		}
		if penalty > 0 {
			oppBudget := octx.Ctx.State.GetBudget(oppNum)
			octx.Ctx.State.SetBudget(oppNum, oppBudget-penalty)
		}
	}

	return nil
}

// HealDamage reduces damage on selected resources. Amount 0 means full heal.
type HealDamage struct {
	Sel   Selector
	Value Amount
}

func (op HealDamage) Execute(octx *OpContext) error {
	amount, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	targets, err := op.Sel.Select(octx)
	if err != nil {
		return err
	}
	for _, t := range targets {
		if amount == 0 {
			t.Damage = 0
		} else {
			t.Damage -= amount
			if t.Damage < 0 {
				t.Damage = 0
			}
		}
	}
	return nil
}

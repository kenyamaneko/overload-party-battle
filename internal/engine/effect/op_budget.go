package effect

// GainBudget adds budget to a player.
type GainBudget struct {
	Player PlayerRef
	Value  Amount
}

func (op GainBudget) Execute(octx *OpContext) error {
	amount, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	pNum := op.Player.Resolve(octx.Ctx)
	budget := octx.Ctx.State.GetBudget(pNum)
	octx.Ctx.State.SetBudget(pNum, budget+amount)
	return nil
}

// LoseBudget deducts budget from a player.
type LoseBudget struct {
	Player PlayerRef
	Value  Amount
}

func (op LoseBudget) Execute(octx *OpContext) error {
	amount, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	pNum := op.Player.Resolve(octx.Ctx)
	budget := octx.Ctx.State.GetBudget(pNum)
	octx.Ctx.State.SetBudget(pNum, budget-amount)
	return nil
}

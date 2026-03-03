package effect

import "github.com/kenyamaneko/overload-party-common/model"

// GainInsight adds to the active player's Insight pool.
type GainInsight struct {
	Value Amount
}

func (op GainInsight) Execute(octx *OpContext) error {
	amount, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	insightPool := octx.Ctx.State.GetInsightPool(octx.Ctx.PlayerNum)
	octx.Ctx.State.SetInsightPool(octx.Ctx.PlayerNum, insightPool+amount)
	return nil
}

// AbsorbInsight transfers insight from opponent, capped at their current amount.
type AbsorbInsight struct {
	Value Amount
}

func (op AbsorbInsight) Execute(octx *OpContext) error {
	amount, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	oppInsight := octx.Ctx.State.GetInsightPool(oppNum)
	stolen := amount
	if oppInsight < stolen {
		stolen = oppInsight
	}
	octx.Ctx.State.SetInsightPool(oppNum, oppInsight-stolen)
	myInsight := octx.Ctx.State.GetInsightPool(octx.Ctx.PlayerNum)
	octx.Ctx.State.SetInsightPool(octx.Ctx.PlayerNum, myInsight+stolen)
	return nil
}

package effect

import "github.com/kenyamaneko/overload-party-common/model"

// ApplyBuff adds a TemporaryEffect to selected resources.
type ApplyBuff struct {
	Sel        Selector
	EffectType string
	Value      Amount
	Duration   string
	SourceID   string
}

func (op ApplyBuff) Execute(octx *OpContext) error {
	value, err := op.Value.Resolve(octx)
	if err != nil {
		return err
	}
	targets, err := op.Sel.Select(octx)
	if err != nil {
		return err
	}
	for _, t := range targets {
		t.TemporaryEffects = append(t.TemporaryEffects, model.TemporaryEffect{
			EffectType: op.EffectType,
			Value:      value,
			Duration:   op.Duration,
			SourceID:   op.SourceID,
		})
	}
	return nil
}

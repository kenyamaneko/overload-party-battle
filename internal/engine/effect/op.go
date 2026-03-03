package effect

import (
	"github.com/kenyamaneko/overload-party-common/model"
)

// Op is a single atomic operation in an effect pipeline.
type Op interface {
	Execute(octx *OpContext) error
}

// PlayerRef identifies which player an operation targets.
type PlayerRef int

const (
	Self     PlayerRef = 0
	Opponent PlayerRef = 1
)

// Resolve returns the actual player number for this reference.
func (p PlayerRef) Resolve(ctx *EffectContext) int64 {
	if p == Opponent {
		return model.OpponentNum(ctx.PlayerNum)
	}
	return ctx.PlayerNum
}

// OpContext carries mutable state through a pipeline of ops.
type OpContext struct {
	Ctx    *EffectContext
	Result *EffectResult
	fields map[int64]*model.Field
}

func newOpContext(ctx *EffectContext) *OpContext {
	return &OpContext{
		Ctx:    ctx,
		Result: &EffectResult{},
		fields: make(map[int64]*model.Field),
	}
}

// GetField returns the field for a player, caching the deserialized result.
// All ops in the same pipeline share the same *Field pointer.
func (o *OpContext) GetField(playerNum int64) (*model.Field, error) {
	if f, ok := o.fields[playerNum]; ok {
		return f, nil
	}
	f, err := o.Ctx.State.GetField(playerNum)
	if err != nil {
		return nil, err
	}
	o.fields[playerNum] = f
	return f, nil
}

// FlushFields writes all cached fields back to GameState.
func (o *OpContext) FlushFields() error {
	for pNum, field := range o.fields {
		if err := o.Ctx.State.SetField(pNum, field); err != nil {
			return err
		}
	}
	return nil
}

// Compose takes a sequence of Ops and returns an EffectHandler.
func Compose(ops ...Op) EffectHandler {
	return func(ctx *EffectContext) (*EffectResult, error) {
		octx := newOpContext(ctx)
		for _, op := range ops {
			if err := op.Execute(octx); err != nil {
				return nil, err
			}
		}
		if err := octx.FlushFields(); err != nil {
			return nil, err
		}
		return octx.Result, nil
	}
}

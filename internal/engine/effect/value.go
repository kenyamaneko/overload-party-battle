package effect

import (
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

// Amount resolves to an int64 value, possibly depending on game state.
type Amount interface {
	Resolve(octx *OpContext) (int64, error)
}

// Static is a constant amount.
type Static int64

func (s Static) Resolve(_ *OpContext) (int64, error) {
	return int64(s), nil
}

// Fn wraps an arbitrary function as an Amount.
type Fn func(octx *OpContext) (int64, error)

func (f Fn) Resolve(octx *OpContext) (int64, error) {
	return f(octx)
}

// SourceYield returns the Source's current Yield value.
func SourceYield() Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		if octx.Ctx.Source == nil {
			return 0, fmt.Errorf("no source for SourceYield")
		}
		if octx.Ctx.Source.CurrentYield == nil {
			return 0, nil
		}
		return *octx.Ctx.Source.CurrentYield, nil
	})
}

// TargetTP returns the Target's current TP value.
func TargetTP() Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		if octx.Ctx.Target == nil {
			return 0, fmt.Errorf("no target for TargetTP")
		}
		if octx.Ctx.Target.CurrentTP == nil {
			return 0, nil
		}
		return *octx.Ctx.Target.CurrentTP, nil
	})
}

// SelectedTP resolves the TP of the first resource matched by a selector.
func SelectedTP(sel Selector) Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		targets, err := sel.Select(octx)
		if err != nil || len(targets) == 0 {
			return 0, err
		}
		if targets[0].CurrentTP != nil {
			return *targets[0].CurrentTP, nil
		}
		return 0, nil
	})
}

// HalfMaxAV returns ceil(Target.MaxAV / 2) rounded up to nearest 200.
func HalfMaxAV() Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		if octx.Ctx.Target == nil {
			return 0, fmt.Errorf("no target for HalfMaxAV")
		}
		half := octx.Ctx.Target.MaxAV / 2
		if half%200 != 0 {
			half = ((half / 200) + 1) * 200
		}
		return half, nil
	})
}

// SLAPenaltyAmount returns the SLA penalty of the Target's card definition.
func SLAPenaltyAmount() Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		if octx.Ctx.Target == nil {
			return 0, fmt.Errorf("no target for SLAPenaltyAmount")
		}
		card := octx.Ctx.CardCache.Get(octx.Ctx.Target.CardID)
		if card == nil {
			return 0, nil
		}
		if model.IsComputeType(card.CardType) {
			stats, _ := model.ParseComputeStats(card.Stats)
			if stats != nil {
				return stats.SLAPenalty, nil
			}
		} else if model.IsDataType(card.CardType) {
			stats, _ := model.ParseDataStats(card.Stats)
			if stats != nil {
				return stats.SLAPenalty, nil
			}
		}
		return 0, nil
	})
}

// BackendScaled returns base + perBackend * opponentBackendCount, capped at base + maxBonus.
func BackendScaled(base, perBackend, maxBonus int64) Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		count := int64(CountOpponentBackend(octx.Ctx.State, octx.Ctx.PlayerNum))
		bonus := count * perBackend
		if bonus > maxBonus {
			bonus = maxBonus
		}
		return base + bonus, nil
	})
}

// TargetCardID returns the Target's CardID as an Amount.
func TargetCardID() Amount {
	return Fn(func(octx *OpContext) (int64, error) {
		if octx.Ctx.Target == nil {
			return 0, fmt.Errorf("no target for TargetCardID")
		}
		return octx.Ctx.Target.CardID, nil
	})
}

package effect

import (
	"encoding/json"
	"fmt"
)

// BranchOnChoice reads a ChoiceOption and executes the corresponding op sequence.
type BranchOnChoice struct {
	Branches map[string][]Op
}

func (op BranchOnChoice) Execute(octx *OpContext) error {
	var choice ChoiceOption
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse choice option: %w", err)
	}
	ops, ok := op.Branches[choice.Option]
	if !ok {
		return fmt.Errorf("invalid choice: %s", choice.Option)
	}
	for _, subOp := range ops {
		if err := subOp.Execute(octx); err != nil {
			return err
		}
	}
	return nil
}

// IfCondition runs ops only if condition is true; otherwise skips (not error).
type IfCondition struct {
	Cond func(octx *OpContext) bool
	Then []Op
}

func (op IfCondition) Execute(octx *OpContext) error {
	if !op.Cond(octx) {
		return nil
	}
	for _, subOp := range op.Then {
		if err := subOp.Execute(octx); err != nil {
			return err
		}
	}
	return nil
}

// CustomFn is an escape hatch for effects that truly cannot be decomposed.
type CustomFn struct {
	Fn func(octx *OpContext) error
}

func (op CustomFn) Execute(octx *OpContext) error {
	return op.Fn(octx)
}

// CustomFnTagged is like CustomFn but includes classification metadata
// so that the NPC classifier can understand what the function does.
type CustomFnTagged struct {
	Fn         func(octx *OpContext) error
	Categories []EffectCategory
	Target     TargetType
	Zone       string
}

func (op CustomFnTagged) Execute(octx *OpContext) error {
	return op.Fn(octx)
}

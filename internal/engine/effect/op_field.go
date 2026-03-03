package effect

import (
	"encoding/json"
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

// RevealTrap flips the opponent's first face-down trap.
type RevealTrap struct{}

func (op RevealTrap) Execute(octx *OpContext) error {
	return opRevealTrap(octx)
}

// ScaleToRank changes the source's rank and recalculates stats.
type ScaleToRank struct {
	Rank string
}

func (op ScaleToRank) Execute(octx *OpContext) error {
	if octx.Ctx.Source == nil {
		return fmt.Errorf("no source for scale")
	}

	source := octx.Ctx.Source
	card := octx.Ctx.CardCache.Get(source.CardID)
	if card == nil {
		return fmt.Errorf("card %d not found", source.CardID)
	}

	mult := model.RankMultiplier(op.Rank)
	source.Rank = op.Rank

	if model.IsComputeType(card.CardType) {
		stats, err := model.ParseComputeStats(card.Stats)
		if err != nil {
			return err
		}
		tp := stats.Throughput * mult
		source.CurrentTP = &tp
		if !card.Elastic {
			source.MaxTP = &tp
		}
		source.MaxAV = stats.Availability * mult
		source.CurrentAV = source.MaxAV
	} else if model.IsDataType(card.CardType) {
		stats, err := model.ParseDataStats(card.Stats)
		if err != nil {
			return err
		}
		if stats.Yield > 0 {
			yieldVal := stats.Yield * mult
			source.CurrentYield = &yieldVal
			if !card.Elastic {
				source.MaxYield = &yieldVal
			}
		}
		source.MaxAV = stats.Availability * mult
		source.CurrentAV = source.MaxAV
	}

	return nil
}

// DestroyPlatform destroys an opponent's support card by ChoiceInstanceID.
type DestroyPlatform struct{}

func (op DestroyPlatform) Execute(octx *OpContext) error {
	var choice ChoiceInstanceID
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse platform choice: %w", err)
	}

	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(oppNum)
	if err != nil {
		return err
	}

	for i, sup := range field.Support {
		if sup != nil && sup.InstanceID == choice.InstanceID {
			card := octx.Ctx.CardCache.Get(sup.CardID)
			if card == nil || card.CardType != "Platform" {
				return fmt.Errorf("target is not a Platform")
			}
			if err := addToTrash(octx.Ctx.State, oppNum, sup.CardID); err != nil {
				return err
			}
			field.Support[i] = nil
			return nil
		}
	}

	return fmt.Errorf("platform %s not found", choice.InstanceID)
}

// DestroyCheck checks all resources on a player's field and destroys those with AV <= 0.
type DestroyCheck struct {
	Player PlayerRef
}

func (op DestroyCheck) Execute(octx *OpContext) error {
	pNum := op.Player.Resolve(octx.Ctx)
	field, err := octx.GetField(pNum)
	if err != nil {
		return err
	}
	return DestroyResources(octx.Ctx.State, field, pNum, octx.Ctx.CardCache)
}

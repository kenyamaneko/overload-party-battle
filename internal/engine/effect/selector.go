package effect

import (
	"encoding/json"
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

// Selector resolves which ResourceInstance(s) an op targets.
type Selector interface {
	Select(octx *OpContext) ([]*model.ResourceInstance, error)
}

// SourceSel selects ctx.Source.
type SourceSel struct{}

func (s SourceSel) Select(octx *OpContext) ([]*model.ResourceInstance, error) {
	if octx.Ctx.Source == nil {
		return nil, fmt.Errorf("no source resource")
	}
	return []*model.ResourceInstance{octx.Ctx.Source}, nil
}

// TargetSel selects ctx.Target.
type TargetSel struct{}

func (s TargetSel) Select(octx *OpContext) ([]*model.ResourceInstance, error) {
	if octx.Ctx.Target == nil {
		return nil, fmt.Errorf("no target resource")
	}
	return []*model.ResourceInstance{octx.Ctx.Target}, nil
}

// ByChoiceSel selects a resource by ChoiceInstanceID from the player's field.
type ByChoiceSel struct {
	Zone     string    // "frontend", "backend", "" = any
	Faction  string    // "" = any
	CardType string    // "" = any (uses IsComputeType/IsDataType check pattern)
	Owner    PlayerRef // Self or Opponent
}

func (s ByChoiceSel) Select(octx *OpContext) ([]*model.ResourceInstance, error) {
	var choice ChoiceInstanceID
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return nil, fmt.Errorf("parse choice instance ID: %w", err)
	}

	playerNum := s.Owner.Resolve(octx.Ctx)
	field, err := octx.GetField(playerNum)
	if err != nil {
		return nil, err
	}

	res, zone, _ := model.FindResourceByID(field, choice.InstanceID)
	if res == nil {
		return nil, fmt.Errorf("resource %s not found", choice.InstanceID)
	}

	if s.Zone != "" && zone != s.Zone {
		return nil, fmt.Errorf("resource must be in %s zone", s.Zone)
	}

	if s.Faction != "" || s.CardType != "" {
		card := octx.Ctx.CardCache.Get(res.CardID)
		if card == nil {
			return nil, fmt.Errorf("card %d not found", res.CardID)
		}
		if s.Faction != "" && card.Faction != s.Faction {
			return nil, fmt.Errorf("card must be %s faction", s.Faction)
		}
		if s.CardType != "" {
			if !matchCardType(card.CardType, s.CardType) {
				return nil, fmt.Errorf("card must be %s type", s.CardType)
			}
		}
	}

	return []*model.ResourceInstance{res}, nil
}

// AllOwnSel selects all own resources, optionally filtered by zone and faction.
type AllOwnSel struct {
	Zone    string // "frontend", "backend", "" = both
	Faction string // "" = any
}

func (s AllOwnSel) Select(octx *OpContext) ([]*model.ResourceInstance, error) {
	field, err := octx.GetField(octx.Ctx.PlayerNum)
	if err != nil {
		return nil, err
	}
	return selectFromField(field, s.Zone, s.Faction, octx), nil
}

// AllOpponentSel selects all opponent resources, optionally filtered.
type AllOpponentSel struct {
	Zone    string
	Faction string
}

func (s AllOpponentSel) Select(octx *OpContext) ([]*model.ResourceInstance, error) {
	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(oppNum)
	if err != nil {
		return nil, err
	}
	return selectFromField(field, s.Zone, s.Faction, octx), nil
}

// selectFromField collects resources from a field with optional zone/faction filtering.
func selectFromField(field *model.Field, zone, faction string, octx *OpContext) []*model.ResourceInstance {
	var result []*model.ResourceInstance

	if zone == "" || zone == model.ZoneFrontend {
		for _, res := range field.Frontend {
			if res != nil && matchFaction(res, faction, octx) {
				result = append(result, res)
			}
		}
	}
	if zone == "" || zone == model.ZoneBackend {
		for _, res := range field.Backend {
			if res != nil && matchFaction(res, faction, octx) {
				result = append(result, res)
			}
		}
	}

	return result
}

func matchFaction(res *model.ResourceInstance, faction string, octx *OpContext) bool {
	if faction == "" {
		return true
	}
	card := octx.Ctx.CardCache.Get(res.CardID)
	return card != nil && card.Faction == faction
}

func matchCardType(actual, expected string) bool {
	switch expected {
	case "compute":
		return model.IsComputeType(actual)
	case "data":
		return model.IsDataType(actual)
	default:
		return actual == expected
	}
}

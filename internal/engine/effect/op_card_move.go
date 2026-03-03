package effect

import (
	"encoding/json"
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

// SearchRepo finds a card in the player's repository by ChoiceCardNo and adds it to hand.
type SearchRepo struct {
	Faction string // "" = any
}

func (op SearchRepo) Execute(octx *OpContext) error {
	var choice ChoiceCardNo
	autoSelect := false

	// Try to parse choice data; if empty/invalid, auto-select first matching card
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		autoSelect = true
	}

	// Auto-select: find first matching card in repository
	if autoSelect || choice.CardNo == 0 {
		repo, err := octx.Ctx.State.GetRepository(octx.Ctx.PlayerNum)
		if err != nil {
			return fmt.Errorf("get repository: %w", err)
		}

		found := false
		for _, cardNo := range repo {
			card := octx.Ctx.CardCache.Get(cardNo)
			if card == nil {
				continue
			}
			if !model.IsResourceType(card.CardType) {
				continue
			}
			if op.Faction != "" && card.Faction != op.Faction {
				continue
			}
			// Found a matching card
			choice.CardNo = cardNo
			found = true
			break
		}
		if !found {
			return fmt.Errorf("no matching cards in repository")
		}
	}

	card := octx.Ctx.CardCache.Get(choice.CardNo)
	if card == nil {
		return fmt.Errorf("card %d not found", choice.CardNo)
	}
	if !model.IsResourceType(card.CardType) {
		return fmt.Errorf("card %d is not a component", choice.CardNo)
	}
	if op.Faction != "" && card.Faction != op.Faction {
		return fmt.Errorf("card %d is not a %s component", choice.CardNo, op.Faction)
	}

	found, err := removeFromRepo(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo)
	if err != nil {
		return err
	}
	if !found {
		return fmt.Errorf("card %d not in repository", choice.CardNo)
	}

	return addToHand(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo)
}

// CardFilter is a predicate for matching card definitions.
type CardFilter func(card *model.CardDefinition) bool

// DeployFromRepo finds the first matching card in the repository and deploys it.
type DeployFromRepo struct {
	Filter     CardFilter
	OverrideAV int64 // 0 = use card's native AV
}

func (op DeployFromRepo) Execute(octx *OpContext) error {
	repo, err := octx.Ctx.State.GetRepository(octx.Ctx.PlayerNum)
	if err != nil {
		return fmt.Errorf("get repository: %w", err)
	}

	// Find matching card in repo
	cardNo := int64(-1)
	for _, c := range repo {
		card := octx.Ctx.CardCache.Get(c)
		if card != nil && op.Filter(card) {
			cardNo = c
			break
		}
	}
	if cardNo == -1 {
		// No matching card found - silently succeed (optional deploy)
		return nil
	}

	return deployResourceFromRepo(octx.Ctx.State, octx.Ctx.PlayerNum, cardNo, op.OverrideAV, octx.Ctx.CardCache)
}

// DeployFromHand deploys a card from hand by ChoiceCardNo, validating against a filter.
type DeployFromHand struct {
	Filter CardFilter // nil = any card
}

func (op DeployFromHand) Execute(octx *OpContext) error {
	var choice ChoiceCardNo
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse deploy choice: %w", err)
	}

	if op.Filter != nil {
		card := octx.Ctx.CardCache.Get(choice.CardNo)
		if card == nil {
			return fmt.Errorf("card %d not found", choice.CardNo)
		}
		if !op.Filter(card) {
			return fmt.Errorf("card %d does not match filter", choice.CardNo)
		}
	}

	return deployResourceFromHand(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo, octx.Ctx.CardCache)
}

// AddToHand adds a card to the player's hand by card number.
type AddToHand struct {
	CardNo Amount
}

func (op AddToHand) Execute(octx *OpContext) error {
	cardNo, err := op.CardNo.Resolve(octx)
	if err != nil {
		return err
	}
	return addToHand(octx.Ctx.State, octx.Ctx.PlayerNum, cardNo)
}

// TrashToHand moves a card from trash to hand by ChoiceCardNo.
type TrashToHand struct{}

func (op TrashToHand) Execute(octx *OpContext) error {
	var choice ChoiceCardNo
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse trash choice: %w", err)
	}

	card := octx.Ctx.CardCache.Get(choice.CardNo)
	if card == nil {
		return fmt.Errorf("card %d not found", choice.CardNo)
	}
	if !model.IsResourceType(card.CardType) {
		return fmt.Errorf("card %d is not a resource", choice.CardNo)
	}

	found, err := removeFromTrash(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo)
	if err != nil {
		return err
	}
	if !found {
		return fmt.Errorf("card %d not in trash", choice.CardNo)
	}

	return addToHand(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo)
}

// DrawCards draws N cards from the repository top.
type DrawCards struct {
	Count        int
	TunersTrash bool // if true, Tuners-faction cards go to trash
	KeepOne      bool // if true and 2+ non-Tuners drawn, player discards 1 via ChoiceData
}

func (op DrawCards) Execute(octx *OpContext) error {
	repo, err := octx.Ctx.State.GetRepository(octx.Ctx.PlayerNum)
	if err != nil {
		return fmt.Errorf("get repository: %w", err)
	}

	drawCount := op.Count
	if drawCount > len(repo) {
		drawCount = len(repo)
	}
	if drawCount == 0 {
		return nil
	}

	drawn := repo[:drawCount]
	remaining := repo[drawCount:]
	if err := octx.Ctx.State.SetRepository(octx.Ctx.PlayerNum, remaining); err != nil {
		return err
	}

	var keptCards []int64
	for _, cardNo := range drawn {
		card := octx.Ctx.CardCache.Get(cardNo)
		if op.TunersTrash && card != nil && card.Faction == model.FactionTuners {
			if err := addToTrash(octx.Ctx.State, octx.Ctx.PlayerNum, cardNo); err != nil {
				return err
			}
		} else {
			if err := addToHand(octx.Ctx.State, octx.Ctx.PlayerNum, cardNo); err != nil {
				return err
			}
			keptCards = append(keptCards, cardNo)
		}
	}

	// KeepOne: if 2+ non-Tuners cards were kept, discard 1 via choice
	if op.KeepOne && len(keptCards) >= 2 {
		var choice ChoiceCardNo
		if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
			// No choice provided — discard the last kept card
			discardNo := keptCards[len(keptCards)-1]
			return discardFromHand(octx.Ctx.State, octx.Ctx.PlayerNum, discardNo)
		}
		return discardFromHand(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo)
	}

	return nil
}

// discardFromHand removes a card from hand and adds it to trash.
func discardFromHand(state *model.GameState, playerNum int64, cardNo int64) error {
	hand, err := state.GetHand(playerNum)
	if err != nil {
		return err
	}
	for i, c := range hand {
		if c.CardID == cardNo {
			hand = append(hand[:i], hand[i+1:]...)
			if err := state.SetHand(playerNum, hand); err != nil {
				return err
			}
			return addToTrash(state, playerNum, cardNo)
		}
	}
	return fmt.Errorf("card %d not in hand", cardNo)
}

// --- Filter constructors ---

// FactionFilter matches cards of a specific faction ("" = any).
func FactionFilter(faction string) CardFilter {
	return func(card *model.CardDefinition) bool {
		return faction == "" || card.Faction == faction
	}
}

// FactionAndTypeFilter matches cards by faction and type predicate.
func FactionAndTypeFilter(faction string, isType func(string) bool) CardFilter {
	return func(card *model.CardDefinition) bool {
		factionOK := faction == "" || card.Faction == faction
		typeOK := isType == nil || isType(card.CardType)
		return factionOK && typeOK
	}
}

// CardNoFilter matches a specific card number.
func CardNoFilter(cardNo int64) CardFilter {
	return func(card *model.CardDefinition) bool {
		return card.CardNo == cardNo
	}
}

// SameCardAsTarget returns a filter matching the Target's card number.
// Must be called within the Op pipeline where Target is set.
func SameCardAsTarget(octx *OpContext) CardFilter {
	if octx.Ctx.Target == nil {
		return func(_ *model.CardDefinition) bool { return false }
	}
	targetCardNo := octx.Ctx.Target.CardID
	return func(card *model.CardDefinition) bool {
		return card.CardNo == targetCardNo
	}
}

// DeployFromRepoSameCard is a convenience op that deploys the same card as Target from repo.
type DeployFromRepoSameCard struct {
	OverrideAV int64
}

func (op DeployFromRepoSameCard) Execute(octx *OpContext) error {
	if octx.Ctx.Target == nil {
		return fmt.Errorf("no target for same card deploy")
	}
	inner := DeployFromRepo{
		Filter:     CardNoFilter(octx.Ctx.Target.CardID),
		OverrideAV: op.OverrideAV,
	}
	return inner.Execute(octx)
}

// --- reveal trap helper for Op pipeline ---

func opRevealTrap(octx *OpContext) error {
	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(oppNum)
	if err != nil {
		return err
	}
	for i, sup := range field.Support {
		if sup != nil && sup.FaceDown {
			field.Support[i].FaceDown = false
			eventData, _ := json.Marshal(map[string]interface{}{
				"revealedCard": sup.CardID,
				"slotIndex":    i,
			})
			octx.Result.Events = append(octx.Result.Events, model.GameEvent{
				GameID:    octx.Ctx.Game.GameID,
				EventType: model.EventTrapRevealed,
				EventData: eventData,
			})
			break
		}
	}
	return nil
}

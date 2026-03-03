package engine

import (
	"encoding/json"
	"fmt"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

func computeCardDef() *model.CardDefinition {
	return &model.CardDefinition{
		CardNo:    1,
		CardName:  "Test Compute",
		Faction:   "SD",
		CardType:  "Compute",
		Resizable: true,
		Stats:     json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	}
}

func databaseCardDef() *model.CardDefinition {
	return &model.CardDefinition{
		CardNo:   2,
		CardName: "Test Database",
		Faction:  "SD",
		CardType: "Database",
		Stats:    json.RawMessage(`{"yield": 300, "availability": 800, "maintenance_cost": 100, "deploy_cost": 300, "sla_penalty": 300}`),
	}
}

func platformCardDef() *model.CardDefinition {
	return &model.CardDefinition{
		CardNo:   3,
		CardName: "Test Platform",
		Faction:  "SD",
		CardType: "Platform",
		Stats:    json.RawMessage(`{"deploy_cost": 200}`),
	}
}

func attachmentCardDef() *model.CardDefinition {
	return &model.CardDefinition{
		CardNo:   4,
		CardName: "Test Attachment",
		Faction:  "SD",
		CardType: "Attachment",
		Stats:    json.RawMessage(`{}`),
	}
}

func makeResource(id string, cardID int64, rank string) *model.ResourceInstance {
	res := &model.ResourceInstance{
		InstanceID:       id,
		CardID:           cardID,
		Rank:             rank,
		FaceUp:           true,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}
	if rank == model.RankSmall {
		tp := int64(700)
		res.CurrentTP = &tp
		res.MaxTP = &tp
		res.CurrentAV = 1400
		res.MaxAV = 1400
	}
	return res
}

func findAction(actions []AvailableAction, actionType string) *AvailableAction {
	for i, a := range actions {
		if a.Type == actionType {
			return &actions[i]
		}
	}
	return nil
}

func findActions(actions []AvailableAction, actionType string) []AvailableAction {
	var result []AvailableAction
	for _, a := range actions {
		if a.Type == actionType {
			result = append(result, a)
		}
	}
	return result
}

// --- Play Card Tests ---

func TestAvailableActions_PlayCard_HasBudget(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 5000

	hand := []model.HandCard{{InstanceID: "h1", CardID: 1}}
	_ = state.SetHand(1, hand)
	myField := &model.Field{}
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	playActions := findActions(actions, model.ActionPlayCard)
	if len(playActions) != 1 {
		t.Fatalf("expected 1 play_card action, got %d", len(playActions))
	}
	if playActions[0].HandInstanceID != "h1" {
		t.Errorf("expected hand_instance_id=h1, got %s", playActions[0].HandInstanceID)
	}
	// Compute can go to frontend or backend (6 slots total)
	if len(playActions[0].ValidZones) != 6 {
		t.Errorf("expected 6 valid zones for Compute, got %d: %v", len(playActions[0].ValidZones), playActions[0].ValidZones)
	}
}

func TestAvailableActions_PlayCard_SupportType(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(3, platformCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	hand := []model.HandCard{{InstanceID: "h1", CardID: 3}}
	_ = state.SetHand(1, hand)
	myField := &model.Field{}
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	playActions := findActions(actions, model.ActionPlayCard)
	if len(playActions) != 1 {
		t.Fatalf("expected 1 play_card action for platform, got %d", len(playActions))
	}
	// Platform goes to support zone only
	if len(playActions[0].ValidZones) != 3 {
		t.Errorf("expected 3 support zones, got %d: %v", len(playActions[0].ValidZones), playActions[0].ValidZones)
	}
	if playActions[0].ValidZones[0] != "support_0" {
		t.Errorf("expected support_0, got %s", playActions[0].ValidZones[0])
	}
}

func TestAvailableActions_PlayCard_Attachment(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(4, attachmentCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	hand := []model.HandCard{{InstanceID: "h1", CardID: 4}}
	_ = state.SetHand(1, hand)
	myField := &model.Field{}
	myField.Frontend[0] = makeResource("r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	playActions := findActions(actions, model.ActionPlayCard)
	if len(playActions) != 1 {
		t.Fatalf("expected 1 play_card action for attachment, got %d", len(playActions))
	}
	if len(playActions[0].ValidTargets) != 1 || playActions[0].ValidTargets[0] != "r1" {
		t.Errorf("expected valid_targets=[r1], got %v", playActions[0].ValidTargets)
	}
}

// --- Attack Tests ---

func TestAvailableActions_Attack_ValidTargets(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseBattle
	state.CurrentTurn = 2
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("my-r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)

	oppField := &model.Field{}
	oppField.Frontend[0] = makeResource("opp-r1", 1, model.RankSmall)
	oppField.Frontend[1] = makeResource("opp-r2", 1, model.RankSmall)
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) != 1 {
		t.Fatalf("expected 1 attack action, got %d", len(attackActions))
	}
	if attackActions[0].SourceInstanceID != "my-r1" {
		t.Errorf("expected source_instance_id=my-r1, got %s", attackActions[0].SourceInstanceID)
	}
	if len(attackActions[0].ValidTargets) != 2 {
		t.Errorf("expected 2 targets, got %d", len(attackActions[0].ValidTargets))
	}
}

func TestAvailableActions_Attack_AlreadyAttacked(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 1

	res := makeResource("my-r1", 1, model.RankSmall)
	res.HasAttacked = true
	myField := &model.Field{}
	myField.Frontend[0] = res
	_ = state.SetField(1, myField)

	oppField := &model.Field{}
	oppField.Frontend[0] = makeResource("opp-r1", 1, model.RankSmall)
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) != 0 {
		t.Errorf("expected 0 attack actions when already attacked, got %d", len(attackActions))
	}
}

func TestAvailableActions_Attack_BackendOnlyWhenNoFrontend(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("my-r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)

	oppField := &model.Field{}
	// No frontend, only backend
	oppField.Backend[0] = makeResource("opp-b1", 1, model.RankSmall)
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) != 1 {
		t.Fatalf("expected 1 attack action, got %d", len(attackActions))
	}
	if len(attackActions[0].ValidTargets) != 1 || attackActions[0].ValidTargets[0] != "opp-b1" {
		t.Errorf("expected target opp-b1, got %v", attackActions[0].ValidTargets)
	}
}

// --- Scale Up Tests ---

func TestAvailableActions_ScaleUp_Resizable(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef()) // Resizable: true

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	scaleActions := findActions(actions, model.ActionScaleUp)
	if len(scaleActions) != 1 {
		t.Fatalf("expected 1 scale_up action, got %d", len(scaleActions))
	}
	if scaleActions[0].TargetRank != model.RankMedium {
		t.Errorf("expected target_rank=medium, got %s", scaleActions[0].TargetRank)
	}
	if !scaleActions[0].NeedsFamily {
		t.Error("expected needs_family=true for small→medium")
	}
}

func TestAvailableActions_ScaleUp_AlreadyLarge(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("r1", 1, model.RankLarge)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	scaleActions := findActions(actions, model.ActionScaleUp)
	if len(scaleActions) != 0 {
		t.Errorf("expected 0 scale_up actions at rank large, got %d", len(scaleActions))
	}
}

func TestAvailableActions_ScaleUp_NotResizable(t *testing.T) {
	cc := cache.NewCardCache()
	nonResizable := computeCardDef()
	nonResizable.Resizable = false
	cc.InjectForTest(1, nonResizable)

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	scaleActions := findActions(actions, model.ActionScaleUp)
	if len(scaleActions) != 0 {
		t.Errorf("expected 0 scale_up actions for non-resizable, got %d", len(scaleActions))
	}
}

// --- Distribute Yield Tests ---

func TestAvailableActions_DistributeYield_NotFirstTurn(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 2 // Not first turn
	state.ActivePlayer = 1

	myField := &model.Field{}
	res := makeResource("b1", 1, model.RankSmall)
	myField.Backend[0] = res
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 500, cc, nil)

	yieldActions := findActions(actions, model.ActionDistributeYield)
	if len(yieldActions) != 1 {
		t.Fatalf("expected 1 distribute_yield action, got %d", len(yieldActions))
	}
	if yieldActions[0].SourceInstanceID != "b1" {
		t.Errorf("expected source_instance_id=b1, got %s", yieldActions[0].SourceInstanceID)
	}
	if yieldActions[0].RemainingCapacity != 700 {
		t.Errorf("expected remaining_capacity=700, got %d", yieldActions[0].RemainingCapacity)
	}
}

func TestAvailableActions_DistributeYield_FirstTurn(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 1 // First turn
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Backend[0] = makeResource("b1", 1, model.RankSmall)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 500, cc, nil)

	yieldActions := findActions(actions, model.ActionDistributeYield)
	if len(yieldActions) != 0 {
		t.Errorf("expected 0 distribute_yield on first turn, got %d", len(yieldActions))
	}
}

// --- Activate Effect Tests ---

func TestAvailableActions_ActivateEffect_Available(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	reg := effect.NewEffectRegistry()
	reg.RegisterComposed(1, effect.TriggerActivate,
		effect.GainBudget{Player: effect.Self, Value: effect.Static(100)},
	)

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, reg)

	effectActions := findActions(actions, model.ActionActivateEffect)
	if len(effectActions) != 1 {
		t.Fatalf("expected 1 activate_effect action, got %d", len(effectActions))
	}
	if effectActions[0].SourceInstanceID != "r1" {
		t.Errorf("expected source_instance_id=r1, got %s", effectActions[0].SourceInstanceID)
	}
}

func TestAvailableActions_ActivateEffect_AlreadyUsed(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	reg := effect.NewEffectRegistry()
	reg.RegisterComposed(1, effect.TriggerActivate,
		effect.GainBudget{Player: effect.Self, Value: effect.Static(100)},
	)

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	res := makeResource("r1", 1, model.RankSmall)
	res.EffectUsedThisTurn = true
	myField := &model.Field{}
	myField.Frontend[0] = res
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, reg)

	effectActions := findActions(actions, model.ActionActivateEffect)
	if len(effectActions) != 0 {
		t.Errorf("expected 0 activate_effect when already used, got %d", len(effectActions))
	}
}

// --- ComputeAvailableActions: end_phase / discard_hand are NOT in available_actions ---

func TestAvailableActions_NoEndPhaseInActions(t *testing.T) {
	cc := cache.NewCardCache()

	for _, phase := range []string{model.PhaseMain, model.PhaseBattle} {
		state := newTestState("g1")
		state.CurrentPhase = phase
		state.ActivePlayer = 1

		myField := &model.Field{}
		_ = state.SetField(1, myField)
		oppField := &model.Field{}
		_ = state.SetField(2, oppField)

		game := newTestGame("g1")
		myF, _ := state.GetField(1)
		oppF, _ := state.GetField(2)
		h, _ := state.GetHand(1)

		actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

		endAction := findAction(actions, model.ActionEndPhase)
		if endAction != nil {
			t.Errorf("end_phase should not be in available_actions (%s phase)", phase)
		}
	}
}

func TestAvailableActions_NoDiscardInActions(t *testing.T) {
	cc := cache.NewCardCache()

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1

	hand := make([]model.HandCard, 8) // Over limit
	for i := range hand {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: 1}
	}
	_ = state.SetHand(1, hand)

	myField := &model.Field{}
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	discardActions := findActions(actions, model.ActionDiscardHand)
	if len(discardActions) != 0 {
		t.Errorf("discard_hand should not be in available_actions, got %d", len(discardActions))
	}
}

// --- TurnControls Tests ---

func TestTurnControls_CanEndPhase_MainAndBattle(t *testing.T) {
	for _, phase := range []string{model.PhaseMain, model.PhaseBattle} {
		state := newTestState("g1")
		state.CurrentPhase = phase

		tc := ComputeTurnControls(state, nil)
		if !tc.CanEndPhase {
			t.Errorf("expected CanEndPhase=true in %s phase", phase)
		}
		if tc.DiscardRequired != 0 {
			t.Errorf("expected DiscardRequired=0 in %s phase, got %d", phase, tc.DiscardRequired)
		}
	}
}

func TestTurnControls_CannotEndPhase_EndPhase(t *testing.T) {
	state := newTestState("g1")
	state.CurrentPhase = model.PhaseEnd

	tc := ComputeTurnControls(state, nil)
	if tc.CanEndPhase {
		t.Error("expected CanEndPhase=false in end phase")
	}
}

func TestTurnControls_DiscardRequired_OverLimit(t *testing.T) {
	state := newTestState("g1")
	state.CurrentPhase = model.PhaseEnd

	hand := make([]model.HandCard, 8) // limit is 6, discard 2
	for i := range hand {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: 1}
	}

	tc := ComputeTurnControls(state, hand)
	if tc.CanEndPhase {
		t.Error("expected CanEndPhase=false in end phase")
	}
	if tc.DiscardRequired != 2 {
		t.Errorf("expected DiscardRequired=2, got %d", tc.DiscardRequired)
	}
}

func TestTurnControls_DiscardRequired_AtLimit(t *testing.T) {
	state := newTestState("g1")
	state.CurrentPhase = model.PhaseEnd

	hand := make([]model.HandCard, 6) // Exactly at limit
	for i := range hand {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: 1}
	}

	tc := ComputeTurnControls(state, hand)
	if tc.DiscardRequired != 0 {
		t.Errorf("expected DiscardRequired=0 at hand limit, got %d", tc.DiscardRequired)
	}
}

func TestTurnControls_DrawPhase(t *testing.T) {
	state := newTestState("g1")
	state.CurrentPhase = model.PhaseDraw

	tc := ComputeTurnControls(state, nil)
	if tc.CanEndPhase {
		t.Error("expected CanEndPhase=false in draw phase")
	}
	if tc.DiscardRequired != 0 {
		t.Errorf("expected DiscardRequired=0 in draw phase, got %d", tc.DiscardRequired)
	}
}

// --- Phase isolation ---

func TestAvailableActions_BattlePhase_NoPlayCard(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 1

	hand := []model.HandCard{{InstanceID: "h1", CardID: 1}}
	_ = state.SetHand(1, hand)
	myField := &model.Field{}
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	playActions := findActions(actions, model.ActionPlayCard)
	if len(playActions) != 0 {
		t.Errorf("expected 0 play_card actions in battle phase, got %d", len(playActions))
	}
}

func TestAvailableActions_MainPhase_NoAttack(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	myField := &model.Field{}
	myField.Frontend[0] = makeResource("r1", 1, model.RankSmall)
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	oppField.Frontend[0] = makeResource("opp-r1", 1, model.RankSmall)
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) != 0 {
		t.Errorf("expected 0 attack actions in main phase, got %d", len(attackActions))
	}
}

// --- Incident 1-Per-Turn Limit Tests ---

func incidentCardDef() *model.CardDefinition {
	return &model.CardDefinition{
		CardNo:   100,
		CardName: "Test Incident",
		Faction:  "Neutral",
		CardType: "Incident",
		Stats:    json.RawMessage(`{"deploy_cost": 300}`),
	}
}

func TestAvailableActions_IncidentLimit_FirstPlayAllowed(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(100, incidentCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 2 // Not T1 — incidents are blocked on T1
	state.ActivePlayer = 1

	myField := &model.Field{}
	_ = state.SetField(1, myField)
	_ = state.SetField(2, &model.Field{})

	hand := []model.HandCard{{InstanceID: "h1", CardID: 100}}
	_ = state.SetHand(1, hand)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)
	playActions := findActions(actions, model.ActionPlayCard)
	if len(playActions) != 1 {
		t.Errorf("expected 1 play_card action for incident, got %d", len(playActions))
	}
}

func TestAvailableActions_IncidentBlocked_OnT1(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(100, incidentCardDef())
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 1 // T1 — incidents blocked
	state.ActivePlayer = 1

	myField := &model.Field{}
	_ = state.SetField(1, myField)
	_ = state.SetField(2, &model.Field{})

	hand := []model.HandCard{
		{InstanceID: "h1", CardID: 100}, // Incident — should be blocked on T1
		{InstanceID: "h2", CardID: 1},   // Compute — should be allowed
	}
	_ = state.SetHand(1, hand)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)
	playActions := findActions(actions, model.ActionPlayCard)

	for _, a := range playActions {
		if a.CardID == 100 {
			t.Error("incident card should be blocked on T1 (first player protection)")
		}
	}
	hasCompute := false
	for _, a := range playActions {
		if a.CardID == 1 {
			hasCompute = true
		}
	}
	if !hasCompute {
		t.Error("compute card should still be playable on T1")
	}
}

func TestAvailableActions_IncidentLimit_SecondBlocked(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(100, incidentCardDef())
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 2 // Not T1
	state.ActivePlayer = 1

	myField := &model.Field{IncidentPlayedThisTurn: true}
	_ = state.SetField(1, myField)
	_ = state.SetField(2, &model.Field{})

	hand := []model.HandCard{
		{InstanceID: "h1", CardID: 100}, // Incident — should be blocked
		{InstanceID: "h2", CardID: 1},   // Compute — should be allowed
	}
	_ = state.SetHand(1, hand)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)
	playActions := findActions(actions, model.ActionPlayCard)

	// Only the Compute card should be playable, not the Incident
	for _, a := range playActions {
		if a.CardID == 100 {
			t.Error("incident card should be blocked after one was already played this turn")
		}
	}
	hasCompute := false
	for _, a := range playActions {
		if a.CardID == 1 {
			hasCompute = true
		}
	}
	if !hasCompute {
		t.Error("compute card should still be playable when incident limit is reached")
	}
}

func TestIncidentLimit_ResetOnTurnEnd(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, computeCardDef())

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1

	myField := &model.Field{IncidentPlayedThisTurn: true}
	_ = state.SetField(1, myField)

	game := newTestGame("g1")

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	myF, _ := state.GetField(1)
	if myF.IncidentPlayedThisTurn {
		t.Error("IncidentPlayedThisTurn should be reset after end phase")
	}
}

func TestAvailableActions_PlayCard_ChoiceOptions(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(11, &model.CardDefinition{
		CardNo:   11,
		CardName: "Test Cache",
		Faction:  "SD",
		CardType: "CacheDB",
		Stats:    json.RawMessage(`{"yield": 200, "availability": 600, "deploy_cost": 200, "sla_penalty": 200}`),
	})

	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 2
	state.ActivePlayer = 1

	hand := []model.HandCard{{InstanceID: "h1", CardID: 11}}
	_ = state.SetHand(1, hand)
	myField := &model.Field{}
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, reg)

	playActions := findActions(actions, model.ActionPlayCard)
	if len(playActions) != 1 {
		t.Fatalf("expected 1 play_card action, got %d", len(playActions))
	}
	if len(playActions[0].ChoiceOptions) == 0 {
		t.Error("expected choice_options for Cache DB card with BranchOnChoice deploy effect")
	}
}


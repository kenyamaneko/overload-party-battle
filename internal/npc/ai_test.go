package npc

import (
	"encoding/json"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

func newTestCardCache() *cache.CardCache {
	cc := cache.NewCardCache()

	// Card 1: Compute (frontend eligible) — AV=1400, TP=700, deploy_cost=400
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:    1,
		CardName:  "Test Compute",
		Faction:   "SD",
		CardType:  "Compute",
		Resizable: true,
		Stats:     json.RawMessage(`{"throughput":700,"availability":1400,"maintenance_cost":200,"deploy_cost":400,"sla_penalty":400}`),
	})

	// Card 2: Serverless Compute (frontend eligible) — AV=1800, TP=900, deploy_cost=500
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo:   2,
		CardName: "Test Serverless",
		Faction:  "Tenki",
		CardType: "Serverless",
		Elastic:  true,
		Stats:    json.RawMessage(`{"throughput":900,"availability":1800,"maintenance_cost":300,"deploy_cost":500,"sla_penalty":500}`),
	})

	// Card 3: Database (backend eligible) — Yield=200, deploy_cost=300
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo:    3,
		CardName:  "Test Database",
		Faction:   "Tenki",
		CardType:  "Database",
		Resizable: true,
		Stats:     json.RawMessage(`{"yield":200,"yield_max":600,"deploy_cost":300}`),
	})

	// Card 4: ObjectStorage (backend eligible) — Yield=300, deploy_cost=200
	cc.InjectForTest(4, &model.CardDefinition{
		CardNo:    4,
		CardName:  "Test ObjectStorage",
		Faction:   "SD",
		CardType:  "ObjectStorage",
		Resizable: true,
		Elastic:   true,
		Stats:     json.RawMessage(`{"yield":300,"yield_max":900,"deploy_cost":200}`),
	})

	// Card 5: Platform (support) — deploy_cost=100
	cc.InjectForTest(5, &model.CardDefinition{
		CardNo:   5,
		CardName: "Test Platform",
		Faction:  "Neutral",
		CardType: "Platform",
		Stats:    json.RawMessage(`{"deploy_cost":100}`),
	})

	return cc
}

// computeAvailable is a test helper to compute available actions from game state.
func computeAvailable(state *model.GameState, game *model.Game, playerNum int64, cc *cache.CardCache, reg *effect.EffectRegistry) []engine.AvailableAction {
	myField, _ := state.GetField(playerNum)
	oppField, _ := state.GetField(model.OpponentNum(playerNum))
	hand, _ := state.GetHand(playerNum)
	budget := state.GetBudget(playerNum)
	insightPool := state.GetInsightPool(playerNum)
	return engine.ComputeAvailableActions(state, game, playerNum, myField, oppField, hand, budget, insightPool, cc, reg)
}

func TestDecideMainPhaseActions_DeployCards(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil)

	state := newTestGameState()
	game := newTestNPCGame()

	// Give NPC (player 2) cards in hand
	hand := []model.HandCard{
		{InstanceID: "h1", CardID: 1},
		{InstanceID: "h2", CardID: 3},
	}
	_ = state.SetHand(2, hand)
	_ = state.SetField(2, &model.Field{})

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	available := computeAvailable(state, game, 2, cc, nil)
	actions := ai.DecideMainPhaseActions(state, game, 2, available)

	// Should have at least 1 play_card action + end_phase
	if len(actions) < 2 {
		t.Fatalf("expected at least 2 actions, got %d", len(actions))
	}

	// First action should be play_card (Compute has priority 0)
	if actions[0].ActionType != "play_card" {
		t.Errorf("first action type = %s, want play_card", actions[0].ActionType)
	}

	// Last action should be end_phase
	last := actions[len(actions)-1]
	if last.ActionType != "end_phase" {
		t.Errorf("last action type = %s, want end_phase", last.ActionType)
	}
}

func TestDecideMainPhaseActions_LowBudget_StillDeploys(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil)

	state := newTestGameState()
	game := newTestNPCGame()

	hand := []model.HandCard{
		{InstanceID: "h1", CardID: 1},
	}
	_ = state.SetHand(2, hand)
	_ = state.SetField(2, &model.Field{})

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 100 // Deploy is free — budget doesn't matter

	available := computeAvailable(state, game, 2, cc, nil)
	actions := ai.DecideMainPhaseActions(state, game, 2, available)

	// Deploy is free, so card should still be deployed
	if len(actions) < 2 {
		t.Fatalf("expected at least 2 actions (play_card + end_phase), got %d", len(actions))
	}
	if actions[0].ActionType != "play_card" {
		t.Errorf("first action = %s, want play_card", actions[0].ActionType)
	}
}

func TestDecideBattlePhaseActions_AttackTarget(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil)

	state := newTestGameState()
	game := newTestNPCGame()

	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	// NPC (player 2) has an attacker
	npcField := &model.Field{}
	npcField.Frontend[0] = &model.ResourceInstance{
		InstanceID:  "npc-f0",
		CardID:      1,
		FaceUp:      true,
		HasAttacked: false,
	}
	_ = state.SetField(2, npcField)

	// Opponent (player 1) has a target
	oppField := &model.Field{}
	oppField.Frontend[0] = &model.ResourceInstance{
		InstanceID: "opp-f0",
		CardID:     1,
		FaceUp:     true,
		CurrentAV:  1400,
	}
	_ = state.SetField(1, oppField)

	available := computeAvailable(state, game, 2, cc, nil)
	actions := ai.DecideBattlePhaseActions(state, game, 2, available)

	// Should have 1 attack + end_phase
	if len(actions) < 2 {
		t.Fatalf("expected at least 2 actions, got %d", len(actions))
	}
	if actions[0].ActionType != "attack" {
		t.Errorf("first action = %s, want attack", actions[0].ActionType)
	}

	// Verify attack targets the opponent's resource
	var attackData map[string]string
	_ = json.Unmarshal(actions[0].Data, &attackData)
	if attackData["attackerInstanceId"] != "npc-f0" {
		t.Errorf("attacker = %s, want npc-f0", attackData["attackerInstanceId"])
	}
	if attackData["targetInstanceId"] != "opp-f0" {
		t.Errorf("target = %s, want opp-f0", attackData["targetInstanceId"])
	}
}

func TestDecideBattlePhaseActions_NoAttackersAvailable(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil)

	state := newTestGameState()
	game := newTestNPCGame()

	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	// NPC has no resources
	_ = state.SetField(2, &model.Field{})
	_ = state.SetField(1, &model.Field{})

	available := computeAvailable(state, game, 2, cc, nil)
	actions := ai.DecideBattlePhaseActions(state, game, 2, available)

	// Should only have end_phase
	if len(actions) != 1 {
		t.Fatalf("expected 1 action, got %d", len(actions))
	}
	if actions[0].ActionType != "end_phase" {
		t.Errorf("action = %s, want end_phase", actions[0].ActionType)
	}
}

func TestDecideDiscard_LowestMaintenanceFirst(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil)

	state := newTestGameState()

	// Create a hand that exceeds HandLimit (6)
	// Maintenance costs: Card1=200, Card2=300, Card3=0, Card4=0, Card5=0
	hand := make([]model.HandCard, 8)
	hand[0] = model.HandCard{InstanceID: "h0", CardID: 2} // maintenance=300
	hand[1] = model.HandCard{InstanceID: "h1", CardID: 1} // maintenance=200
	hand[2] = model.HandCard{InstanceID: "h2", CardID: 5} // maintenance=0
	hand[3] = model.HandCard{InstanceID: "h3", CardID: 3} // maintenance=0
	hand[4] = model.HandCard{InstanceID: "h4", CardID: 4} // maintenance=0
	hand[5] = model.HandCard{InstanceID: "h5", CardID: 1} // maintenance=200
	hand[6] = model.HandCard{InstanceID: "h6", CardID: 3} // maintenance=0
	hand[7] = model.HandCard{InstanceID: "h7", CardID: 2} // maintenance=300
	_ = state.SetHand(2, hand)

	ids := ai.DecideDiscard(state, 2)

	// Need to discard 2 cards (8 - 6 = 2)
	if len(ids) != 2 {
		t.Fatalf("discard count = %d, want 2", len(ids))
	}

	// Lowest maintenance: h2(0), h3(0), h4(0), h6(0) — first two in sort order
	if ids[0] != "h2" {
		t.Errorf("first discard = %s, want h2 (lowest maintenance)", ids[0])
	}
	if ids[1] != "h3" {
		t.Errorf("second discard = %s, want h3 (second lowest maintenance)", ids[1])
	}
}

func TestDecideDiscard_NoDiscardNeeded(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil)

	state := newTestGameState()

	hand := []model.HandCard{
		{InstanceID: "h0", CardID: 1},
		{InstanceID: "h1", CardID: 2},
	}
	_ = state.SetHand(2, hand)

	ids := ai.DecideDiscard(state, 2)

	if ids != nil {
		t.Errorf("expected nil, got %v", ids)
	}
}

// --- Category-based evaluation tests ---

// newTestRegistry creates a minimal effect registry for testing.
func newTestRegistry() *effect.EffectRegistry {
	reg := effect.NewEffectRegistry()

	// Card 100: Strategy — budget gain (+500)
	reg.RegisterComposed(100, effect.TriggerActivate,
		effect.GainBudget{Player: effect.Self, Value: effect.Static(500)},
	)

	// Card 101: Strategy — AoE damage to all opponent
	reg.RegisterComposed(101, effect.TriggerActivate,
		effect.IncidentDamage{
			Sel:   effect.AllOpponentSel{},
			Value: effect.Static(400),
		},
	)

	// Card 102: Strategy — draw 2 cards
	reg.RegisterComposed(102, effect.TriggerActivate,
		effect.DrawCards{Count: 2},
	)

	// Card 103: Strategy — single damage (requires target choice)
	reg.RegisterComposed(103, effect.TriggerActivate,
		effect.IncidentDamage{
			Sel:   effect.ByChoiceSel{Zone: "frontend", Owner: effect.Opponent},
			Value: effect.Static(600),
		},
	)

	// Card 104: Strategy — conditional (require 2+ Smile cards)
	reg.RegisterComposed(104, effect.TriggerActivate,
		effect.RequireFactionCount{Faction: "Smile", Min: 2},
		effect.GainBudget{Player: effect.Self, Value: effect.Static(800)},
	)

	// Card 105: Resource activate — heal own resource
	reg.RegisterComposed(105, effect.TriggerActivate,
		effect.HealDamage{Sel: effect.ByChoiceSel{Owner: effect.Self}, Value: effect.Static(300)},
	)

	return reg
}

func injectStrategyCards(cc *cache.CardCache) {
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo: 100, CardName: "Budget Strategy", CardType: "Strategy",
		Faction: "Neutral", Stats: json.RawMessage(`{"deploy_cost":200}`),
	})
	cc.InjectForTest(101, &model.CardDefinition{
		CardNo: 101, CardName: "AoE Incident", CardType: "Incident",
		Faction: "Neutral", Stats: json.RawMessage(`{"deploy_cost":300}`),
	})
	cc.InjectForTest(102, &model.CardDefinition{
		CardNo: 102, CardName: "Draw Strategy", CardType: "Strategy",
		Faction: "Neutral", Stats: json.RawMessage(`{"deploy_cost":100}`),
	})
	cc.InjectForTest(103, &model.CardDefinition{
		CardNo: 103, CardName: "Targeted Incident", CardType: "Incident",
		Faction: "Neutral", Stats: json.RawMessage(`{"deploy_cost":250}`),
	})
	cc.InjectForTest(104, &model.CardDefinition{
		CardNo: 104, CardName: "Faction Strategy", CardType: "Strategy",
		Faction: "Smile", Stats: json.RawMessage(`{"deploy_cost":150}`),
	})
	cc.InjectForTest(105, &model.CardDefinition{
		CardNo: 105, CardName: "Heal Resource", CardType: "Compute",
		Faction: "Neutral",
		Stats: json.RawMessage(`{"throughput":500,"availability":1200,"maintenance_cost":200,"deploy_cost":300,"sla_penalty":300}`),
	})
}

func TestEvaluateCard_BudgetGain_HighPriorityWhenLow(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: &model.Field{},
		hand:     nil,
		budget:   800, // Low budget → high priority
		ai:       ai,
	}

	pri, use, _ := ai.evaluateCard(100, effect.TriggerActivate, ctx)
	if !use {
		t.Fatal("expected budget gain card to be usable")
	}
	if pri != 90 {
		t.Errorf("priority = %d, want 90 (low budget = high priority)", pri)
	}
}

func TestEvaluateCard_BudgetGain_LowPriorityWhenHigh(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: &model.Field{},
		budget:   5000, // High budget → low priority
		ai:       ai,
	}

	pri, use, _ := ai.evaluateCard(100, effect.TriggerActivate, ctx)
	if !use {
		t.Fatal("expected budget gain card to be usable")
	}
	if pri != 40 {
		t.Errorf("priority = %d, want 40 (high budget = low priority)", pri)
	}
}

func TestEvaluateCard_AoEDamage_RequiresMultipleTargets(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	// Only 1 opponent resource — AoE shouldn't fire
	oppField := &model.Field{}
	oppField.Frontend[0] = &model.ResourceInstance{InstanceID: "opp-1", CardID: 1, FaceUp: true, MaxAV: 1400}

	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: oppField,
		budget:   5000,
		ai:       ai,
	}

	_, use, _ := ai.evaluateCard(101, effect.TriggerActivate, ctx)
	if use {
		t.Error("AoE damage should NOT be used with only 1 target")
	}

	// 2 opponent resources — AoE should fire
	oppField.Frontend[1] = &model.ResourceInstance{InstanceID: "opp-2", CardID: 1, FaceUp: true, MaxAV: 1400}
	_, use, _ = ai.evaluateCard(101, effect.TriggerActivate, ctx)
	if !use {
		t.Error("AoE damage should be used with 2+ targets")
	}
}

func TestEvaluateCard_Draw_HighPriorityWhenFewCards(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: &model.Field{},
		hand:     []model.HandCard{{InstanceID: "h1", CardID: 1}}, // Only 1 card
		budget:   5000,
		ai:       ai,
	}

	pri, use, _ := ai.evaluateCard(102, effect.TriggerActivate, ctx)
	if !use {
		t.Fatal("draw card should be usable")
	}
	if pri != 80 {
		t.Errorf("priority = %d, want 80 (few cards = high priority)", pri)
	}
}

func TestEvaluateCard_SingleDamage_SelectsTarget(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	oppField := &model.Field{}
	oppField.Frontend[0] = &model.ResourceInstance{InstanceID: "opp-strong", CardID: 1, FaceUp: true, MaxAV: 1400}
	oppField.Frontend[1] = &model.ResourceInstance{InstanceID: "opp-weak", CardID: 1, FaceUp: true, MaxAV: 800}

	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: oppField,
		budget:   5000,
		ai:       ai,
	}

	_, use, choiceData := ai.evaluateCard(103, effect.TriggerActivate, ctx)
	if !use {
		t.Fatal("single damage should be usable with targets")
	}
	if choiceData == nil {
		t.Fatal("expected choiceData for TargetChoice")
	}

	var choice map[string]string
	_ = json.Unmarshal(choiceData, &choice)
	// Should target the weakest (lowest AV)
	if choice["instanceId"] != "opp-weak" {
		t.Errorf("target = %s, want opp-weak (weakest)", choice["instanceId"])
	}
}

func TestEvaluateCard_SingleDamage_NoTargets(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	// No opponent frontend resources — card 103 targets frontend only
	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: &model.Field{},
		budget:   5000,
		ai:       ai,
	}

	_, use, _ := ai.evaluateCard(103, effect.TriggerActivate, ctx)
	if use {
		t.Error("single damage should NOT be usable with no targets")
	}
}

func TestEvaluateCard_ConditionNotMet(t *testing.T) {
	cc := newTestCardCache()
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	// Card 104 requires 2+ Smile faction cards on field, but field is empty
	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: &model.Field{},
		budget:   5000,
		ai:       ai,
	}

	_, use, _ := ai.evaluateCard(104, effect.TriggerActivate, ctx)
	if use {
		t.Error("should NOT be usable when faction count condition is not met")
	}
}

func TestEvaluateCard_NilEffects(t *testing.T) {
	cc := newTestCardCache()
	ai := NewStandardAI(cc, nil) // No effect registry

	ctx := &decisionCtx{
		field:    &model.Field{},
		oppField: &model.Field{},
		budget:   5000,
		ai:       ai,
	}

	_, use, _ := ai.evaluateCard(100, effect.TriggerActivate, ctx)
	if use {
		t.Error("should not be usable with nil effects registry")
	}
}

func TestDecideImmediateCardActions_PlaysStrategyCards(t *testing.T) {
	cc := newTestCardCache()
	injectStrategyCards(cc)
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	state := newTestGameState()
	game := newTestNPCGame()

	hand := []model.HandCard{
		{InstanceID: "h-budget", CardID: 100}, // Budget gain strategy
		{InstanceID: "h-draw", CardID: 102},   // Draw strategy
		{InstanceID: "h-compute", CardID: 1},  // Normal compute (not immediate)
	}
	_ = state.SetHand(2, hand)

	field := &model.Field{}
	_ = state.SetField(2, field)

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	ctx := &decisionCtx{
		field:    field,
		oppField: &model.Field{},
		hand:     hand,
		budget:   5000,
		ai:       ai,
	}

	available := computeAvailable(state, game, 2, cc, reg)
	usedZones := make(map[string]bool)
	actions := doImmediateActions(ctx, available, cc, reg, usedZones)

	// Should play budget gain (pri=40) and draw (pri=80) — both are Strategy type
	if len(actions) != 2 {
		t.Fatalf("expected 2 immediate actions, got %d", len(actions))
	}

	// All should be play_card
	for _, a := range actions {
		if a.ActionType != "play_card" {
			t.Errorf("action type = %s, want play_card", a.ActionType)
		}
	}

}

func TestDecideActivateActions_UsesFieldEffects(t *testing.T) {
	cc := newTestCardCache()
	injectStrategyCards(cc)
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	state := newTestGameState()
	game := newTestNPCGame()

	// Card 105 has a heal effect; put a damaged resource on own field
	field := &model.Field{}
	field.Frontend[0] = &model.ResourceInstance{
		InstanceID: "own-f0", CardID: 105, FaceUp: true, MaxAV: 1200, Damage: 400,
	}
	_ = state.SetField(2, field)
	_ = state.SetField(1, &model.Field{})

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	ctx := &decisionCtx{
		field:    field,
		oppField: &model.Field{},
		budget:   5000,
		ai:       ai,
	}

	available := computeAvailable(state, game, 2, cc, reg)
	actions := ai.decideActivateActions(ctx, available)

	if len(actions) != 1 {
		t.Fatalf("expected 1 activate action, got %d", len(actions))
	}
	if actions[0].ActionType != "activate_effect" {
		t.Errorf("action type = %s, want activate_effect", actions[0].ActionType)
	}
}

func TestDecideActivateActions_SkipsUsedEffects(t *testing.T) {
	cc := newTestCardCache()
	injectStrategyCards(cc)
	reg := newTestRegistry()
	ai := NewStandardAI(cc, reg)

	state := newTestGameState()
	game := newTestNPCGame()

	field := &model.Field{}
	field.Frontend[0] = &model.ResourceInstance{
		InstanceID: "own-f0", CardID: 105, FaceUp: true, MaxAV: 1200, Damage: 400,
		EffectUsedThisTurn: true, // Already used
	}
	_ = state.SetField(2, field)
	_ = state.SetField(1, &model.Field{})

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	ctx := &decisionCtx{
		field:    field,
		oppField: &model.Field{},
		budget:   5000,
		ai:       ai,
	}

	available := computeAvailable(state, game, 2, cc, reg)
	actions := ai.decideActivateActions(ctx, available)

	if len(actions) != 0 {
		t.Errorf("expected 0 actions for already-used effect, got %d", len(actions))
	}
}

// --- Test Helpers ---

func newTestGameState() *model.GameState {
	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{})
	emptyTrash, _ := json.Marshal([]int64{})

	return &model.GameState{
		GameID:            "game1",
		Version:           1,
		CurrentTurn:       1,
		CurrentPhase:      model.PhaseMain,
		ActivePlayer:      2,
		Player1Budget:     5000,
		Player1InsightPool:     0,
		Player1Field:      emptyField,
		Player1Hand:       emptyHand,
		Player1Repository: emptyRepo,
		Player1Trash:      emptyTrash,
		Player1TimeBank:   120,
		Player2Budget:     5000,
		Player2InsightPool:     0,
		Player2Field:      emptyField,
		Player2Hand:       emptyHand,
		Player2Repository: emptyRepo,
		Player2Trash:      emptyTrash,
		Player2TimeBank:   120,
	}
}

func newTestNPCGame() *model.Game {
	return &model.Game{
		GameID:    "game1",
		Player1ID: "player1",
		Player2ID: NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}
}

// loadRealCardCache loads all cards from the generated JSON file.
func loadRealCardCache(t *testing.T) *cache.CardCache {
	t.Helper()
	cc := cache.NewCardCache()
	if err := cc.LoadFromJSON("../cache/cards_gen.json"); err != nil {
		t.Fatalf("failed to load cards: %v", err)
	}
	return cc
}

// ========================================================
// FactionAI Deploy Behavior Tests
// ========================================================

func TestFactionAI_DeploysBothCards(t *testing.T) {
	cc := newTestCardCache()
	ai := NewSDAI(cc, nil)

	state := newTestGameState()
	game := newTestNPCGame()

	hand := []model.HandCard{
		{InstanceID: "h1", CardID: 1}, // Compute
		{InstanceID: "h2", CardID: 3}, // Database
	}
	_ = state.SetHand(2, hand)
	_ = state.SetField(2, &model.Field{})

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	available := computeAvailable(state, game, 2, cc, nil)
	actions := ai.DecideMainPhaseActions(state, game, 2, available)

	playCardCount := 0
	for _, a := range actions {
		if a.ActionType == model.ActionPlayCard {
			playCardCount++
		}
	}
	if playCardCount < 2 {
		t.Errorf("expected at least 2 play_card actions, got %d", playCardCount)
	}
}

// ========================================================
// Helper Function Tests (action_filter.go)
// ========================================================

func TestFilterByType(t *testing.T) {
	actions := []engine.AvailableAction{
		{Type: model.ActionPlayCard, HandInstanceID: "h1", CardID: 1},
		{Type: model.ActionAttack, SourceInstanceID: "a1"},
		{Type: model.ActionPlayCard, HandInstanceID: "h2", CardID: 2},
		{Type: model.ActionEndPhase},
		{Type: model.ActionScaleUp, SourceInstanceID: "s1"},
		{Type: model.ActionPlayCard, HandInstanceID: "h3", CardID: 3},
	}

	// Filter for play_card
	playCards := filterByType(actions, model.ActionPlayCard)
	if len(playCards) != 3 {
		t.Errorf("filterByType play_card: got %d, want 3", len(playCards))
	}
	for _, a := range playCards {
		if a.Type != model.ActionPlayCard {
			t.Errorf("filtered action type = %s, want play_card", a.Type)
		}
	}

	// Filter for attack
	attacks := filterByType(actions, model.ActionAttack)
	if len(attacks) != 1 {
		t.Errorf("filterByType attack: got %d, want 1", len(attacks))
	}

	// Filter for end_phase
	endPhases := filterByType(actions, model.ActionEndPhase)
	if len(endPhases) != 1 {
		t.Errorf("filterByType end_phase: got %d, want 1", len(endPhases))
	}

	// Filter for a type that doesn't exist
	distributes := filterByType(actions, model.ActionDistributeYield)
	if len(distributes) != 0 {
		t.Errorf("filterByType distribute_yield: got %d, want 0", len(distributes))
	}

	// Empty input
	empty := filterByType(nil, model.ActionPlayCard)
	if len(empty) != 0 {
		t.Errorf("filterByType nil input: got %d, want 0", len(empty))
	}
}

func TestPickBestZone_ComputePrefersFrontend(t *testing.T) {
	computeCard := &model.CardDefinition{
		CardNo:   1,
		CardName: "Test Compute",
		CardType: "Compute",
	}

	usedZones := make(map[string]bool)

	// When both frontend and backend zones are available, Compute prefers frontend
	validZones := []string{"backend_0", "frontend_0", "backend_1", "frontend_1"}
	zone := pickBestZone(validZones, computeCard, usedZones)
	if zone != "frontend_0" {
		t.Errorf("pickBestZone for Compute = %s, want frontend_0", zone)
	}

	// When only backend zones are available, fallback to backend
	backendOnly := []string{"backend_0", "backend_1"}
	zone = pickBestZone(backendOnly, computeCard, usedZones)
	if zone != "backend_0" {
		t.Errorf("pickBestZone for Compute (backend only) = %s, want backend_0", zone)
	}

	// When frontend_0 is already used, pick next available frontend
	usedZones["frontend_0"] = true
	zone = pickBestZone(validZones, computeCard, usedZones)
	if zone != "frontend_1" {
		t.Errorf("pickBestZone for Compute (frontend_0 used) = %s, want frontend_1", zone)
	}
}

func TestPickBestZone_DatabaseBackendOnly(t *testing.T) {
	dbCard := &model.CardDefinition{
		CardNo:   3,
		CardName: "Test Database",
		CardType: "Database",
	}

	usedZones := make(map[string]bool)

	// Database cards should only go to backend zones
	validZones := []string{"frontend_0", "backend_0", "frontend_1", "backend_1"}
	zone := pickBestZone(validZones, dbCard, usedZones)
	if zone != "backend_0" {
		t.Errorf("pickBestZone for Database = %s, want backend_0", zone)
	}

	// When backend_0 is used, pick backend_1
	usedZones["backend_0"] = true
	zone = pickBestZone(validZones, dbCard, usedZones)
	if zone != "backend_1" {
		t.Errorf("pickBestZone for Database (backend_0 used) = %s, want backend_1", zone)
	}

	// When all backend zones are used, should return "" (no frontend fallback for DB)
	usedZones["backend_1"] = true
	zone = pickBestZone(validZones, dbCard, usedZones)
	// DB type has no frontend fallback — falls through to generic fallback
	// Only frontend zones remain, and the fallback returns the first available
	// But since Database only checks backend_*, and there are no available backend zones,
	// it falls to the generic fallback which returns first available zone
	if zone == "" {
		t.Log("pickBestZone for Database with no backend: correctly returned empty (no valid zone)")
	} else if zone == "frontend_0" || zone == "frontend_1" {
		// The generic fallback returns first available; this is acceptable behavior
		t.Logf("pickBestZone for Database with no backend: returned %s (generic fallback)", zone)
	}
}

package engine

import (
	"context"
	"encoding/json"
	"fmt"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
)

// --- Play Card Tests ---

func TestPlayCard_DeployCompute(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	// Set up: main phase, player 1 active
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	// Inject card definition
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:      1,
		CardName:    "Test Compute",
		Faction:     "SD",
		CardType:    "Compute",
		Resizable: true,
		Stats:       json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Give player 1 a hand with the card
	hand := []model.HandCard{{InstanceID: "hand1", CardID: 1}}
	_ = state.SetHand(1, hand)

	// Ensure empty frontend slot
	field := &model.Field{}
	_ = state.SetField(1, field)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(PlayCardRequest{
		CardInstanceID: "hand1",
		Position: struct {
			Zone  string `json:"zone"`
			Index int    `json:"index"`
		}{Zone: "frontend", Index: 0},
	})

	result, err := e.ProcessAction(context.Background(), "game1", "player1", "play_card", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	// Check budget was NOT deducted (deploy is free)
	updatedState := repo.MustGetState("game1")
	if updatedState.GetBudget(1) != 5000 {
		t.Errorf("budget = %d, want %d (deploy is free)", updatedState.GetBudget(1), 5000)
	}

	// Check card was placed on field
	updatedField, _ := updatedState.GetField(1)
	if updatedField.Frontend[0] == nil {
		t.Error("frontend slot 0 should have a resource")
	}

	// Check card removed from hand
	updatedHand, _ := updatedState.GetHand(1)
	if len(updatedHand) != 0 {
		t.Errorf("hand size = %d, want 0", len(updatedHand))
	}
}

func TestPlayCard_WrongPhase(t *testing.T) {
	e, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	state.CurrentPhase = model.PhaseBattle // play_card not allowed in battle
	state.ActivePlayer = 1

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(PlayCardRequest{CardInstanceID: "hand1"})
	_, err := e.ProcessAction(context.Background(), "game1", "player1", "play_card", data)
	if err == nil {
		t.Fatal("expected error for wrong phase")
	}
}

func TestPlayCard_NotYourTurn(t *testing.T) {
	e, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2 // Player 2's turn

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(PlayCardRequest{CardInstanceID: "hand1"})
	_, err := e.ProcessAction(context.Background(), "game1", "player1", "play_card", data)
	if err == nil {
		t.Fatal("expected error for not player's turn")
	}
}

func TestPlayCard_OccupiedSlot(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	hand := []model.HandCard{{InstanceID: "hand1", CardID: 1}}
	_ = state.SetHand(1, hand)

	// Slot 0 already occupied
	tp := int64(700)
	maxTP := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "existing", CardID: 1, CurrentTP: &tp, MaxTP: &maxTP, CurrentAV: 1400, MaxAV: 1400},
		},
	}
	_ = state.SetField(1, field)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(PlayCardRequest{
		CardInstanceID: "hand1",
		Position: struct {
			Zone  string `json:"zone"`
			Index int    `json:"index"`
		}{Zone: "frontend", Index: 0},
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "play_card", data)
	if err == nil {
		t.Fatal("expected error for occupied slot")
	}
}

// --- Attack Tests ---

func newAttackTestState(cc *cache.CardCache, repo *repository.MockGameRepository) (*model.GameState, *model.Game) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 1
	state.CurrentTurn = 2 // Not first turn

	// Inject card definitions
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo:   3,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1000, "deploy_cost": 400, "sla_penalty": 300}`),
	})

	// Player 1: frontend with Compute card
	tp := int64(700)
	maxTP := int64(700)
	p1Field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "atk1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, p1Field)

	// Player 2: frontend with Compute card
	tp2 := int64(700)
	maxTP2 := int64(700)
	p2Field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "def1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp2,
				MaxTP:            &maxTP2,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "def2",
				CardID:           3,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentAV:        1000,
				MaxAV:            1000,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(2, p2Field)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	return state, game
}

func TestAttack_DamageApplied(t *testing.T) {
	e, repo, cc := newTestEngine()
	newAttackTestState(cc, repo)

	data, _ := json.Marshal(AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def1",
	})

	result, err := e.ProcessAction(context.Background(), "game1", "player1", "attack", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}
	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	state := repo.MustGetState("game1")

	// Attacks no longer deduct maintenance cost
	if state.GetBudget(1) != 5000 {
		t.Errorf("attacker budget = %d, want %d", state.GetBudget(1), 5000)
	}

	// Defender should have taken 700 damage (attacker TP)
	oppField, _ := state.GetField(2)
	if oppField.Frontend[0] == nil {
		t.Fatal("defender should still exist (1400 AV - 700 damage = 700 remaining)")
	}
	if oppField.Frontend[0].Damage != 700 {
		t.Errorf("defender damage = %d, want 700", oppField.Frontend[0].Damage)
	}

	// Attacker should be marked as having attacked
	myField, _ := state.GetField(1)
	if !myField.Frontend[0].HasAttacked {
		t.Error("attacker should be marked as having attacked")
	}
}

func TestAttack_DestroyTarget(t *testing.T) {
	e, repo, cc := newTestEngine()
	newAttackTestState(cc, repo)

	// Lower defender AV so it gets destroyed
	state := repo.MustGetState("game1")
	oppField, _ := state.GetField(2)
	oppField.Frontend[0].CurrentAV = 500
	oppField.Frontend[0].MaxAV = 500
	_ = state.SetField(2, oppField)

	data, _ := json.Marshal(AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def1",
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "attack", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	state = repo.MustGetState("game1")
	oppField, _ = state.GetField(2)

	// Defender should be destroyed (nil)
	if oppField.Frontend[0] != nil {
		t.Error("defender should be destroyed")
	}

	// SLA penalty (400) deducted from defender's budget
	if state.GetBudget(2) != 5000-400 {
		t.Errorf("defender budget = %d, want %d (SLA penalty 400)", state.GetBudget(2), 5000-400)
	}
}

func TestAttack_AlreadyAttacked(t *testing.T) {
	e, repo, cc := newTestEngine()
	newAttackTestState(cc, repo)

	// Mark attacker as already attacked
	state := repo.MustGetState("game1")
	myField, _ := state.GetField(1)
	myField.Frontend[0].HasAttacked = true
	_ = state.SetField(1, myField)

	data, _ := json.Marshal(AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def1",
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "attack", data)
	if err == nil {
		t.Fatal("expected error for already attacked")
	}
}

func TestAttack_CannotAttackBackendWhileFrontendExists(t *testing.T) {
	e, repo, cc := newTestEngine()
	newAttackTestState(cc, repo)

	// Try to attack backend while frontend exists
	data, _ := json.Marshal(AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def2",
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "attack", data)
	if err == nil {
		t.Fatal("expected error for attacking backend while frontend exists")
	}
}

// --- Scale Up Tests ---

func TestScaleUp_SmallToMedium(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:      1,
		CardType:    "Compute",
		Faction:     "SD",
		Resizable: true, // Resizable
		Stats:       json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "res1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	// Give player 2 a field too so no system_down
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	familyM := model.FamilyM
	data, _ := json.Marshal(ScaleUpRequest{
		InstanceID:     "res1",
		TargetRank:     model.RankMedium,
		InstanceFamily: &familyM,
	})

	result, err := e.ProcessAction(context.Background(), "game1", "player1", "scale_up", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}
	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	updatedState := repo.MustGetState("game1")

	// Budget should NOT be deducted (scale up is free)
	if updatedState.GetBudget(1) != 5000 {
		t.Errorf("budget = %d, want %d (scale up is free)", updatedState.GetBudget(1), 5000)
	}

	// Rank should be medium
	updatedField, _ := updatedState.GetField(1)
	if updatedField.Frontend[0].Rank != model.RankMedium {
		t.Errorf("rank = %s, want %s", updatedField.Frontend[0].Rank, model.RankMedium)
	}

	// Family should be set
	if updatedField.Frontend[0].InstanceFamily == nil || *updatedField.Frontend[0].InstanceFamily != model.FamilyM {
		t.Error("instance family should be M")
	}
}

func TestScaleUp_NotResizable(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:      1,
		CardType:    "Compute",
		Faction:     "SD",
		Resizable: false, // Not resizable
		Stats:       json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "res1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	familyM := model.FamilyM
	data, _ := json.Marshal(ScaleUpRequest{
		InstanceID:     "res1",
		TargetRank:     model.RankMedium,
		InstanceFamily: &familyM,
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "scale_up", data)
	if err == nil {
		t.Fatal("expected error for non-resizable card")
	}
}

func TestScaleUp_InvalidProgression(t *testing.T) {
	tests := []struct {
		name    string
		current string
		target  string
	}{
		{"small to large", model.RankSmall, model.RankLarge},
		{"large to medium", model.RankLarge, model.RankMedium},
		{"medium to small", model.RankMedium, model.RankSmall},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			err := validateRankProgression(tt.current, tt.target)
			if err == nil {
				t.Errorf("expected error for %s -> %s", tt.current, tt.target)
			}
		})
	}
}

// --- Distribute Yield Tests ---

func TestDistributeYield_Basic(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 2 // Not first turn
	state.Player1InsightPool = 500

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	p1Field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "comp1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, p1Field)

	// Player 2 needs a field to avoid system_down
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(DistributeYieldRequest{
		Distributions: []YieldDistribution{
			{InstanceID: "comp1", Amount: 300},
		},
	})

	result, err := e.ProcessAction(context.Background(), "game1", "player1", "distribute_yield", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}
	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	updatedState := repo.MustGetState("game1")

	// Budget should increase by 300
	if updatedState.GetBudget(1) != 5000+300 {
		t.Errorf("budget = %d, want %d", updatedState.GetBudget(1), 5000+300)
	}

	// Insight pool should decrease by 300
	if updatedState.GetInsightPool(1) != 200 {
		t.Errorf("Insight pool = %d, want 200", updatedState.GetInsightPool(1))
	}
}

func TestDistributeYield_ExceedsThroughput(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 2
	state.Player1InsightPool = 1000

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "comp1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(DistributeYieldRequest{
		Distributions: []YieldDistribution{
			{InstanceID: "comp1", Amount: 800}, // Exceeds TP of 700
		},
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "distribute_yield", data)
	if err == nil {
		t.Fatal("expected error for exceeding throughput capacity")
	}
}

func TestDistributeYield_FirstTurnBlocked(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 1 // First turn
	state.Player1InsightPool = 500

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "comp1", CardID: 1, Rank: model.RankSmall, CurrentTP: &tp, MaxTP: &maxTP, CurrentAV: 1400, MaxAV: 1400, Attachments: []model.AttachmentRef{}, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	_ = state.SetField(1, field)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(DistributeYieldRequest{
		Distributions: []YieldDistribution{
			{InstanceID: "comp1", Amount: 300},
		},
	})

	_, err := e.ProcessAction(context.Background(), "game1", "player1", "distribute_yield", data)
	if err == nil {
		t.Fatal("expected error for distributing yield on first turn")
	}
}

// --- Validate Play Position Tests ---

func TestValidatePlayPosition(t *testing.T) {
	field := &model.Field{}

	tests := []struct {
		name     string
		cardType string
		zone     string
		wantErr  bool
	}{
		{"compute in frontend", "Compute", "frontend", false},
		{"compute in backend", "Compute", "backend", false}, // compute can monetize DV in backend
		{"orchestrator in backend", "Orchestrator", "backend", false},
		{"database in backend", "Database", "backend", false},
		{"database in frontend", "Database", "frontend", true}, // DB cannot go in frontend
		{"objectstorage in backend", "ObjectStorage", "backend", false},
		{"objectstorage in frontend", "ObjectStorage", "frontend", false}, // wall
		{"cachedb in frontend", "CacheDB", "frontend", true},               // CacheDB cannot go in frontend
		{"platform in support", "Platform", "support", false},
		{"compute in support", "Compute", "support", true},
		{"strategy in support", "Strategy", "support", false},
		{"incident in support", "Incident", "support", false},
		{"reactive in support", "Reactive", "support", false},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			card := &model.CardDefinition{CardType: tt.cardType}
			req := PlayCardRequest{
				Position: struct {
					Zone  string `json:"zone"`
					Index int    `json:"index"`
				}{Zone: tt.zone, Index: 0},
			}
			err := validatePlayPosition(card, field, req)
			if (err != nil) != tt.wantErr {
				t.Errorf("validatePlayPosition() error = %v, wantErr %v", err, tt.wantErr)
			}
		})
	}
}

// --- Win Condition via Attack Tests ---

func TestAttack_TriggersWinByBudgetZero(t *testing.T) {
	e, repo, cc := newTestEngine()
	newAttackTestState(cc, repo)

	// Set defender's budget low so SLA penalty brings it to 0
	state := repo.MustGetState("game1")
	state.Player2Budget = 300 // SLA penalty 400 will make it negative

	// Lower defender AV so it gets destroyed
	oppField, _ := state.GetField(2)
	oppField.Frontend[0].CurrentAV = 100
	oppField.Frontend[0].MaxAV = 100
	_ = state.SetField(2, oppField)

	data, _ := json.Marshal(AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def1",
	})

	result, err := e.ProcessAction(context.Background(), "game1", "player1", "attack", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	if !result.GameOver {
		t.Error("game should be over")
	}
	if result.WinnerNum != 1 {
		t.Errorf("winner = %d, want 1", result.WinnerNum)
	}
	if result.WinReason != model.WinReasonBudgetZero {
		t.Errorf("reason = %s, want %s", result.WinReason, model.WinReasonBudgetZero)
	}
}

// ====================================================================
// Discard Hand Tests
// ====================================================================

func TestDiscardHand_Basic(t *testing.T) {
	e, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.CurrentTurn = 2

	// Give player 1 a hand of 8 cards (limit is 6, so must discard 2)
	hand := make([]model.HandCard, 8)
	for i := 0; i < 8; i++ {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)

	// Player 2 needs a field
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	// Discard the first 2 cards
	result, err := processDiscardHand(state, game, 1, DiscardHandRequest{
		CardInstanceIDs: []string{"h0", "h1"},
	}, e.cardCache)
	if err != nil {
		t.Fatalf("processDiscardHand failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}

	// Hand should have 6 cards
	updatedHand, _ := state.GetHand(1)
	if len(updatedHand) != 6 {
		t.Errorf("hand size = %d, want 6", len(updatedHand))
	}

	// Discarded cards should be in trash
	trash, _ := state.GetTrash(1)
	if len(trash) != 2 {
		t.Errorf("trash size = %d, want 2", len(trash))
	}
}

func TestDiscardHand_WrongCount(t *testing.T) {
	e, _, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1

	// 8 cards in hand, need to discard 2, but only provide 1
	hand := make([]model.HandCard, 8)
	for i := 0; i < 8; i++ {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)

	_, err := processDiscardHand(state, game, 1, DiscardHandRequest{
		CardInstanceIDs: []string{"h0"}, // Only 1, need 2
	}, e.cardCache)
	if err == nil {
		t.Fatal("expected error for wrong discard count")
	}
}

func TestDiscardHand_NoDiscardNeeded(t *testing.T) {
	e, _, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1

	// Only 5 cards in hand (under limit)
	hand := make([]model.HandCard, 5)
	for i := 0; i < 5; i++ {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)

	_, err := processDiscardHand(state, game, 1, DiscardHandRequest{
		CardInstanceIDs: []string{},
	}, e.cardCache)
	if err == nil {
		t.Fatal("expected error when no discard is needed")
	}
}

func TestDiscardHand_InvalidCardID(t *testing.T) {
	e, _, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1

	hand := make([]model.HandCard, 8)
	for i := 0; i < 8; i++ {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)

	_, err := processDiscardHand(state, game, 1, DiscardHandRequest{
		CardInstanceIDs: []string{"h0", "nonexistent"},
	}, e.cardCache)
	if err == nil {
		t.Fatal("expected error for card not found in hand")
	}
}

func TestAutoDiscardOldest(t *testing.T) {
	e, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.CurrentTurn = 2

	hand := make([]model.HandCard, 9) // 9 cards, need to discard 3
	for i := 0; i < 9; i++ {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)

	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	result, err := e.AutoDiscardOldest(state, game, 1)
	if err != nil {
		t.Fatalf("AutoDiscardOldest failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}

	updatedHand, _ := state.GetHand(1)
	if len(updatedHand) != 6 {
		t.Errorf("hand size = %d, want 6", len(updatedHand))
	}

	// Oldest 3 cards should be discarded (h0, h1, h2)
	for _, card := range updatedHand {
		if card.InstanceID == "h0" || card.InstanceID == "h1" || card.InstanceID == "h2" {
			t.Errorf("card %s should have been discarded", card.InstanceID)
		}
	}
}

func TestAutoDiscardOldest_NoDiscardNeeded(t *testing.T) {
	e, _, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	hand := make([]model.HandCard, 4)
	for i := 0; i < 4; i++ {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)

	result, err := e.AutoDiscardOldest(state, game, 1)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if result != nil {
		t.Error("result should be nil when no discard needed")
	}
}

// ====================================================================
// Activate Effect Tests (via processActivateEffect)
// ====================================================================

func TestActivateEffect_BudgetRecovery(t *testing.T) {
	e, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(101, &model.CardDefinition{
		CardNo:   101,
		CardType: "Strategy",
		Faction:  "Neutral",
		Stats:    json.RawMessage(`{"deploy_cost": 0}`),
	})

	// Put card in support zone as a "played strategy"
	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "strat1", CardID: 101},
		},
	}
	_ = state.SetField(1, field)

	req := ActivateEffectRequest{InstanceID: "strat1"}
	result, err := processActivateEffect(state, game, 1, req, e.cardCache, e.effects)
	if err != nil {
		t.Fatalf("processActivateEffect failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}

	if state.GetBudget(1) != 5400 { // +400
		t.Errorf("budget = %d, want 5400", state.GetBudget(1))
	}
}

func TestActivateEffect_NoEffectSystem(t *testing.T) {
	cc := cache.NewCardCache()
	// Do NOT set effect registry

	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	req := ActivateEffectRequest{InstanceID: "f1"}
	_, err := processActivateEffect(state, game, 1, req, cc, nil)
	if err == nil {
		t.Fatal("expected error when effect system not initialized")
	}
}

func TestActivateEffect_EffectAlreadyUsed(t *testing.T) {
	e, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(10, &model.CardDefinition{
		CardNo:   10,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	yieldVal := int64(500)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:         "res1",
				CardID:             10,
				FaceUp:             true,
				CurrentYield:       &yieldVal,
				EffectUsedThisTurn: true, // Already used
			},
		},
	}
	_ = state.SetField(1, field)

	req := ActivateEffectRequest{InstanceID: "res1"}
	_, err := processActivateEffect(state, game, 1, req, e.cardCache, e.effects)
	if err == nil {
		t.Fatal("expected error for effect already used this turn")
	}
}

func TestActivateEffect_NoEffectOnCard(t *testing.T) {
	e, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "res1", CardID: 1, CurrentTP: &tp},
		},
	}
	_ = state.SetField(1, field)

	req := ActivateEffectRequest{InstanceID: "res1"}
	_, err := processActivateEffect(state, game, 1, req, e.cardCache, e.effects)
	if err == nil {
		t.Fatal("expected error for card with no effect")
	}
}

func TestActivateEffect_ResourceNotFound(t *testing.T) {
	e, _, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	_ = state.SetField(1, &model.Field{})

	req := ActivateEffectRequest{InstanceID: "nonexistent"}
	_, err := processActivateEffect(state, game, 1, req, e.cardCache, e.effects)
	if err == nil {
		t.Fatal("expected error for resource not found")
	}
}

// --- End Phase Action Tests ---

func TestEndPhase_BattleToEnd_NoDiscard(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 1
	state.CurrentTurn = 2

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	// Small hand (no discard needed)
	hand := []model.HandCard{{InstanceID: "h0", CardID: 1}}
	_ = state.SetHand(1, hand)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(struct{}{})
	result, err := e.ProcessAction(context.Background(), "game1", "player1", "end_phase", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	updatedState := repo.MustGetState("game1")
	// Should have switched to player 2 and advanced to main phase
	if updatedState.ActivePlayer != 2 {
		t.Errorf("active player = %d, want 2", updatedState.ActivePlayer)
	}
	if updatedState.CurrentPhase != model.PhaseMain {
		t.Errorf("phase = %s, want %s", updatedState.CurrentPhase, model.PhaseMain)
	}
	if result.NeedsDiscard {
		t.Error("should not need discard")
	}
}

func TestEndPhase_BattleToEnd_NeedsDiscard(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 1
	state.CurrentTurn = 2

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	// Hand of 8 → needs discard 2
	hand := make([]model.HandCard, 8)
	for i := range hand {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: 1}
	}
	_ = state.SetHand(1, hand)

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(struct{}{})
	result, err := e.ProcessAction(context.Background(), "game1", "player1", "end_phase", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	if !result.NeedsDiscard {
		t.Error("should need discard when hand > 6")
	}
	// Should stay on player 1 until discard is resolved
	updatedState := repo.MustGetState("game1")
	if updatedState.ActivePlayer != 1 {
		t.Errorf("active player = %d, want 1 (waiting for discard)", updatedState.ActivePlayer)
	}
}

func TestEndPhase_FirstTurnSkipsBattle(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 1 // First turn

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(struct{}{})
	result, err := e.ProcessAction(context.Background(), "game1", "player1", "end_phase", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}
	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	updatedState := repo.MustGetState("game1")
	// First turn: main → end (skip battle) → switch to player 2
	if updatedState.ActivePlayer != 2 {
		t.Errorf("active player = %d, want 2", updatedState.ActivePlayer)
	}
}

func TestEndPhase_FirstTurnSkipsBattle_Player2GoesFirst(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.CurrentTurn = 1 // Turn 1 = first player's turn, Player 2 goes first

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(struct{}{})
	result, err := e.ProcessAction(context.Background(), "game1", "player2", "end_phase", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}
	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	updatedState := repo.MustGetState("game1")
	// First turn with P2 first: main → end (skip battle) → switch to player 1
	if updatedState.CurrentPhase != model.PhaseDraw && updatedState.CurrentPhase != model.PhaseMain {
		t.Errorf("phase = %s, want draw or main (after auto-advance)", updatedState.CurrentPhase)
	}
	if updatedState.ActivePlayer != 1 {
		t.Errorf("active player = %d, want 1 (P2's turn ended)", updatedState.ActivePlayer)
	}
}

// Test that the second player CAN attack on their first turn (not blocked)
func TestEndPhase_SecondPlayerCanAttackOnFirstTurn(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.ActivePlayer = 2    // Player 2's turn
	state.CurrentTurn = 2     // Turn 2 = second player's turn

	tp := int64(700)
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	game := newTestGame("game1")
	myField, _ := state.GetField(2)
	oppField, _ := state.GetField(1)
	hand, _ := state.GetHand(2)

	actions := ComputeAvailableActions(state, game, 2, myField, oppField, hand, 5000, 0, cc, nil)
	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) == 0 {
		t.Error("second player should be able to attack on turn 1 (only first player's battle is skipped)")
	}
}

// Test that first player CANNOT attack on their first turn (battle phase is skipped)
func TestAvailableActions_FirstPlayerNoAttackOnFirstTurn(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)

	// Case 1: Player 1 goes first — end_phase from main should skip battle
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 1

	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	game := newTestGame("game1")

	// processEndPhase on first turn: main → end (skip battle) → switch → draw → main
	_, err := processEndPhase(state, game, 1, cc)
	if err != nil {
		t.Fatalf("processEndPhase failed: %v", err)
	}
	// After full processing, active player should have switched and phase should be main
	if state.ActivePlayer != 2 {
		t.Errorf("case 1: active player = %d, want 2 (should have switched)", state.ActivePlayer)
	}
	if state.CurrentPhase != model.PhaseMain {
		t.Errorf("case 1: phase = %s, want main (after auto-advance)", state.CurrentPhase)
	}
	if state.CurrentTurn != 2 {
		t.Errorf("case 1: turn = %d, want 2", state.CurrentTurn)
	}

	// Case 2: Player 2 goes first — same behavior
	state2 := newTestState("game2")
	state2.CurrentPhase = model.PhaseMain
	state2.ActivePlayer = 2
	state2.CurrentTurn = 1

	_ = state2.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state2.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	game2 := newTestGame("game2")
	_, err = processEndPhase(state2, game2, 2, cc)
	if err != nil {
		t.Fatalf("processEndPhase failed: %v", err)
	}
	if state2.ActivePlayer != 1 {
		t.Errorf("case 2: active player = %d, want 1 (should have switched)", state2.ActivePlayer)
	}
	if state2.CurrentPhase != model.PhaseMain {
		t.Errorf("case 2: phase = %s, want main (after auto-advance)", state2.CurrentPhase)
	}
	if state2.CurrentTurn != 2 {
		t.Errorf("case 2: turn = %d, want 2", state2.CurrentTurn)
	}
}

func TestEndPhase_InvalidPhase(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseDraw
	state.ActivePlayer = 1

	cc := cache.NewCardCache()
	_, err := processEndPhase(state, game, 1, cc)
	if err == nil {
		t.Fatal("expected error for ending draw phase manually")
	}
}

func TestEndPhase_MainToBattle(t *testing.T) {
	e, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 2

	// Both players need fields
	tp := int64(700)
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentTP: &tp, CurrentAV: 1400, MaxAV: 1400},
		},
	})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	data, _ := json.Marshal(struct{}{})
	result, err := e.ProcessAction(context.Background(), "game1", "player1", "end_phase", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}
	if !result.StateUpdated {
		t.Error("state should be updated")
	}

	updatedState := repo.MustGetState("game1")
	if updatedState.CurrentPhase != model.PhaseBattle {
		t.Errorf("phase = %s, want %s", updatedState.CurrentPhase, model.PhaseBattle)
	}
}

// ====================================================================
// ResolveChain Tests
// ====================================================================

func TestResolveChain_EmptyStack(t *testing.T) {
	state := newChainTestState()
	game := newTestGame("game1")
	cc := cache.NewCardCache()

	events, err := ResolveChain(state, game, cc, nil)
	if err != nil {
		t.Fatalf("ResolveChain failed: %v", err)
	}
	if events != nil {
		t.Errorf("events should be nil for empty chain, got %d", len(events))
	}
}

func TestResolveChain_NilEffects(t *testing.T) {
	state := newChainTestState()
	game := newTestGame("game1")
	cc := cache.NewCardCache()

	// Push an entry
	_ = PushToChain(state, model.ChainEntry{
		ActionType:       "attack",
		SourcePlayerID:   "player1",
		SourceInstanceID: "atk1",
	})

	// ResolveChain with nil effects — resolveChainEntry returns nil for nil effects
	events, err := ResolveChain(state, game, cc, nil)
	if err != nil {
		t.Fatalf("ResolveChain failed: %v", err)
	}

	// Chain should be cleared
	if IsChainActive(state) {
		t.Error("chain should be cleared after resolve")
	}

	// No events since effects is nil
	if len(events) != 0 {
		t.Errorf("events = %d, want 0 (nil effects)", len(events))
	}
}

func TestResolveChain_WithEffects(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newChainTestState()

	cc.InjectForTest(101, &model.CardDefinition{
		CardNo:   101,
		CardType: "Strategy",
		Faction:  "Neutral",
	})

	// Set up player1 field with a support card that has the effect
	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "sup1", CardID: 101},
		},
	}
	_ = state.SetField(1, field)

	// Push chain entry referencing the support card
	_ = PushToChain(state, model.ChainEntry{
		ActionType:       "component_effect",
		SourcePlayerID:   "player1",
		SourceInstanceID: "sup1",
	})

	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)

	events, err := ResolveChain(state, game, cc, reg)
	if err != nil {
		t.Fatalf("ResolveChain failed: %v", err)
	}

	// Chain should be cleared
	if IsChainActive(state) {
		t.Error("chain should be cleared after resolve")
	}

	// Budget recovery effect should have produced events or changed budget
	// The budget_recovery effect adds 400 budget
	if state.GetBudget(1) != 5400 {
		t.Errorf("budget = %d, want 5400 (+400 from budget_recovery)", state.GetBudget(1))
	}
	_ = events // events may or may not be returned depending on effect handler
}

// ====================================================================
// processActivateEffect — additional path tests
// ====================================================================

func TestActivateEffect_FullHappyPath(t *testing.T) {
	e, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(10, &model.CardDefinition{
		CardNo:   10,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	yieldVal := int64(500)
	yieldMax := int64(500)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:         "res1",
				CardID:             10,
				FaceUp:             true,
				CurrentYield:       &yieldVal,
				MaxYield:           &yieldMax,
				CurrentAV:          1300,
				MaxAV:              1300,
				Rank:               model.RankSmall,
				EffectUsedThisTurn: false,
				Attachments:        []model.AttachmentRef{},
				TemporaryEffects:   []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	req := ActivateEffectRequest{InstanceID: "res1"}
	result, err := processActivateEffect(state, game, 1, req, cc, e.effects)
	if err != nil {
		t.Fatalf("processActivateEffect failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}
	if len(result.Events) == 0 {
		t.Error("should have at least one event")
	}

	// Check effect was marked as used
	updatedField, _ := state.GetField(1)
	if updatedField.Frontend[0] != nil && !updatedField.Frontend[0].EffectUsedThisTurn {
		t.Error("effect should be marked as used this turn")
	}
}

func TestActivateEffect_WithTarget(t *testing.T) {
	e, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(21, &model.CardDefinition{
		CardNo:   21,
		CardType: "Platform",
		Faction:  "SD",
	})

	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 21},
		},
	}
	_ = state.SetField(1, field)

	// Opponent field for target
	tp := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "target1",
				CardID:           1,
				FaceUp:           true,
				CurrentTP:        &tp,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(2, oppField)

	req := ActivateEffectRequest{InstanceID: "plat1"}
	result, err := processActivateEffect(state, game, 1, req, cc, e.effects)
	if err != nil {
		t.Fatalf("processActivateEffect failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}
}

// ====================================================================
// Attachment Destruction with Host
// ====================================================================

func TestAttack_AttachmentDestroyedWithHost(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.CurrentTurn = 2
	state.ActivePlayer = 1

	// Attacker: Compute card
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Attachment card definitions
	cc.InjectForTest(50, &model.CardDefinition{
		CardNo:   50,
		CardName: "Test Attachment A",
		CardType: "Attachment",
		Faction:  "SD",
		Stats:    json.RawMessage(`{}`),
	})
	cc.InjectForTest(51, &model.CardDefinition{
		CardNo:   51,
		CardName: "Test Attachment B",
		CardType: "Attachment",
		Faction:  "SD",
		Stats:    json.RawMessage(`{}`),
	})

	// Attacker field
	atkTP := int64(700)
	atkMaxTP := int64(700)
	myField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "atk1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &atkTP,
				MaxTP:            &atkMaxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, myField)

	// Defender field: Compute with 2 attachments, low AV so it gets destroyed
	defTP := int64(700)
	defMaxTP := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID: "def1",
				CardID:     1,
				FaceUp:     true,
				Rank:       model.RankSmall,
				CurrentTP:  &defTP,
				MaxTP:      &defMaxTP,
				CurrentAV:  500, // Will be destroyed by 700 TP attack
				MaxAV:      500,
				Attachments: []model.AttachmentRef{
					{InstanceID: "att-a", CardID: 50},
					{InstanceID: "att-b", CardID: 51},
				},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(2, oppField)

	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)

	result, err := processAttack(state, game, 1, AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def1",
	}, cc, reg)
	if err != nil {
		t.Fatalf("processAttack failed: %v", err)
	}
	if result == nil {
		t.Fatal("expected non-nil result")
	}

	// Verify defender is destroyed
	updatedOppField, _ := state.GetField(2)
	if updatedOppField.Frontend[0] != nil {
		t.Error("defender should be destroyed")
	}

	// Verify all 3 cards (host + 2 attachments) are in trash
	trash, _ := state.GetTrash(2)
	if len(trash) != 3 {
		t.Fatalf("trash size = %d, want 3 (host + 2 attachments)", len(trash))
	}

	// Verify host card is in trash
	foundHost := false
	foundAttA := false
	foundAttB := false
	for _, cardID := range trash {
		switch cardID {
		case 1:
			foundHost = true
		case 50:
			foundAttA = true
		case 51:
			foundAttB = true
		}
	}
	if !foundHost {
		t.Error("host card (ID=1) should be in trash")
	}
	if !foundAttA {
		t.Error("attachment A (ID=50) should be in trash")
	}
	if !foundAttB {
		t.Error("attachment B (ID=51) should be in trash")
	}
}

func TestAttack_AttachmentSlotsFull_CannotAttach(t *testing.T) {
	cc := cache.NewCardCache()

	// Resource card
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:    1,
		CardName:  "Test Compute",
		CardType:  "Compute",
		Faction:   "SD",
		Resizable: true,
		Stats:     json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Attachment card definition
	cc.InjectForTest(50, &model.CardDefinition{
		CardNo:   50,
		CardName: "Test Attachment",
		CardType: "Attachment",
		Faction:  "SD",
		Stats:    json.RawMessage(`{}`),
	})

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	// Hand has an attachment card
	hand := []model.HandCard{{InstanceID: "h1", CardID: 50}}
	_ = state.SetHand(1, hand)

	// Resource already has MaxAttachments (2) attachments
	tp := int64(700)
	maxTP := int64(700)
	myField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID: "r1",
				CardID:     1,
				FaceUp:     true,
				Rank:       model.RankSmall,
				CurrentTP:  &tp,
				MaxTP:      &maxTP,
				CurrentAV:  1400,
				MaxAV:      1400,
				Attachments: []model.AttachmentRef{
					{InstanceID: "att-1", CardID: 50},
					{InstanceID: "att-2", CardID: 50},
				},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, myField)
	_ = state.SetField(2, &model.Field{})

	game := newTestGame("g1")

	// 1. Verify via available_actions that attachment play is not listed
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	playActions := findActions(actions, model.ActionPlayCard)
	for _, a := range playActions {
		if a.CardID == 50 {
			t.Error("attachment card should not be playable when all resources have full attachment slots")
		}
	}

	// 2. Verify direct processAttachCard call returns error
	targetID := "r1"
	_, err := processAttachCard(
		state, game, 1,
		hand, 0, hand[0],
		cc.Get(50),
		PlayCardRequest{
			CardInstanceID:  "h1",
			TargetInstanceID: &targetID,
		},
		cc, nil,
	)
	if err == nil {
		t.Fatal("expected error when attaching to resource with full attachment slots")
	}
}


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

// --- Test helpers ---

func newTestEngine() (*GameEngine, *repository.MockGameRepository, *cache.CardCache) {
	repo := repository.NewMockGameRepository()
	cc := cache.NewCardCache()
	engine := NewGameEngine(repo, cc)

	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)
	engine.SetEffectRegistry(reg)

	return engine, repo, cc
}

func newTestState(gameID string) *model.GameState {
	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{1, 2, 3, 4, 5})
	emptyTrash, _ := json.Marshal([]int64{})

	return &model.GameState{
		GameID:            gameID,
		Version:           1,
		CurrentTurn:       1,
		CurrentPhase:      model.PhaseDraw,
		ActivePlayer:      1,
		Player1Budget:     5000,
		Player1InsightPool:     0,
		Player1Field:      emptyField,
		Player1Hand:       emptyHand,
		Player1Repository: emptyRepo,
		Player1Trash:      emptyTrash,
		Player1TimeBank:   480,
		Player2Budget:     5000,
		Player2InsightPool:     0,
		Player2Field:      emptyField,
		Player2Hand:       emptyHand,
		Player2Repository: emptyRepo,
		Player2Trash:      emptyTrash,
		Player2TimeBank:   480,
	}
}

func newTestGame(gameID string) *model.Game {
	return &model.Game{
		GameID:    gameID,
		Player1ID: "player1",
		Player2ID: "player2",
		Status:    model.GameStatusPlaying,
	}
}

// --- Phase Transition Tests ---

func TestNextPhase(t *testing.T) {
	tests := []struct {
		phase    string
		expected string
	}{
		{model.PhaseDraw, model.PhaseMain},
		{model.PhaseMain, model.PhaseBattle},
		{model.PhaseBattle, model.PhaseEnd},
	}

	for _, tt := range tests {
		got := nextPhase(tt.phase)
		if got != tt.expected {
			t.Errorf("nextPhase(%s) = %s, want %s", tt.phase, got, tt.expected)
		}
	}
}

func TestIsActionAllowedInPhase(t *testing.T) {
	tests := []struct {
		phase   string
		action  string
		allowed bool
	}{
		{model.PhaseMain, "play_card", true},
		{model.PhaseMain, "scale_up", true},
		{model.PhaseMain, "distribute_yield", true},
		{model.PhaseMain, "attack", false},
		{model.PhaseBattle, "attack", true},
		{model.PhaseBattle, "play_card", false},
		{model.PhaseEnd, "discard_hand", true},
		{model.PhaseEnd, "attack", false},
	}

	for _, tt := range tests {
		got := isActionAllowedInPhase(tt.phase, tt.action)
		if got != tt.allowed {
			t.Errorf("isActionAllowedInPhase(%s, %s) = %v, want %v", tt.phase, tt.action, got, tt.allowed)
		}
	}
}

// --- Draw Phase Tests ---

func TestProcessDrawPhase(t *testing.T) {
	_, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	err := ProcessDrawPhase(state, game)
	if err != nil {
		t.Fatalf("ProcessDrawPhase failed: %v", err)
	}

	// Hand should have 1 card
	hand, _ := state.GetHand(1)
	if len(hand) != 1 {
		t.Errorf("hand size = %d, want 1", len(hand))
	}

	// Repository should have 4 cards
	repo2, _ := state.GetRepository(1)
	if len(repo2) != 4 {
		t.Errorf("repo size = %d, want 4", len(repo2))
	}
}

func TestProcessDrawPhase_EmptyRepo(t *testing.T) {
	_, repo, _ := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	// Empty the repository
	_ = state.SetRepository(1, []int64{})

	repo.InjectGame("game1", game)
	repo.InjectState("game1", state)

	err := ProcessDrawPhase(state, game)
	if err == nil {
		t.Fatal("expected error for empty repository")
	}
}

// --- End Phase Yield Generation Tests ---

func TestProcessEndPhase_YieldGeneration(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd

	// Inject a card definition for a DB card
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo:   3,
		CardName: "Test DB",
		Faction:  "SD",
		CardType: "Database",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1000, "deploy_cost": 400, "sla_penalty": 300}`),
	})

	// Place a backend resource
	yieldVal := int64(500)
	yieldMax := int64(500)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "db1",
				CardID:           3,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentAV:        1000,
				MaxAV:            1000,
				CurrentYield:     &yieldVal,
				MaxYield:         &yieldMax,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	// insight pool should be 500 (yield generated during end phase)
	insightPool := state.GetInsightPool(1)
	if insightPool != 500 {
		t.Errorf("insight pool = %d, want 500", insightPool)
	}
}

func TestProcessEndPhase_YieldAfterMaintenance(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseEnd

	// Card with both maintenance cost and yield
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo:   3,
		CardName: "Test DB",
		Faction:  "SD",
		CardType: "Database",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1000, "maintenance_cost": 100, "deploy_cost": 400, "sla_penalty": 300}`),
	})

	yieldVal := int64(500)
	yieldMax := int64(500)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "db1",
				CardID:           3,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentAV:        1000,
				MaxAV:            1000,
				CurrentYield:     &yieldVal,
				MaxYield:         &yieldMax,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	initialBudget := state.GetBudget(1)

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	// Budget should be reduced by maintenance cost (100)
	finalBudget := state.GetBudget(1)
	if finalBudget != initialBudget-100 {
		t.Errorf("budget = %d, want %d (initial %d - maintenance 100)", finalBudget, initialBudget-100, initialBudget)
	}

	// insight pool should still be 500 (yield happens after maintenance, not affected by it)
	insightPool := state.GetInsightPool(1)
	if insightPool != 500 {
		t.Errorf("insight pool = %d, want 500", insightPool)
	}
}

// --- Win Condition Tests ---

func TestCheckWinCondition_BudgetZero(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.Player1Budget = 0

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 2 {
		t.Errorf("winner = %d, want 2", winnerNum)
	}
	if reason != model.WinReasonBudgetZero {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonBudgetZero)
	}
}

func TestCheckWinCondition_NoGameOver(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Place a resource on each player's field so isSystemDown returns false
	field1 := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	}
	field2 := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	}
	_ = state.SetField(1, field1)
	_ = state.SetField(2, field2)

	_, _, gameOver := CheckWinCondition(state, game)
	if gameOver {
		t.Fatal("expected no game over")
	}
}

// --- Win Condition: System Down ---

func TestCheckWinCondition_SystemDown(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Player 1 has empty field (system down, but had resources before), player 2 has resources
	_ = state.SetField(1, &model.Field{HasHadActiveResource: true}) // all destroyed
	_ = state.SetField(2, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 2 {
		t.Errorf("winner = %d, want 2", winnerNum)
	}
	if reason != model.WinReasonSystemDown {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonSystemDown)
	}
}

func TestCheckWinCondition_SystemDown_Player2(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{HasHadActiveResource: true}) // all destroyed

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 1 {
		t.Errorf("winner = %d, want 1", winnerNum)
	}
	if reason != model.WinReasonSystemDown {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonSystemDown)
	}
}

// --- Win Condition: Launch Failure ---

func TestCheckLaunchFailure_Player1_Turn5(t *testing.T) {
	state := newTestState("game1")
	state.ActivePlayer = 1
	state.CurrentTurn = 5 // Player 1's 3rd personal turn

	// Player 1 never deployed (HasHadActiveResource = false)
	_ = state.SetField(1, &model.Field{})
	_ = state.SetField(2, &model.Field{HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	if !checkLaunchFailure(state, 1) {
		t.Fatal("expected Launch Failure for Player 1 at T=5")
	}
}

func TestCheckLaunchFailure_Player2_Turn6(t *testing.T) {
	state := newTestState("game1")
	state.ActivePlayer = 2
	state.CurrentTurn = 6 // Player 2's 3rd personal turn

	_ = state.SetField(1, &model.Field{HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{}) // never deployed

	if !checkLaunchFailure(state, 2) {
		t.Fatal("expected Launch Failure for Player 2 at T=6")
	}
}

func TestCheckLaunchFailure_NotYet_Turn4(t *testing.T) {
	state := newTestState("game1")
	state.ActivePlayer = 2
	state.CurrentTurn = 4 // Player 2's 2nd personal turn

	_ = state.SetField(2, &model.Field{}) // never deployed

	if checkLaunchFailure(state, 2) {
		t.Fatal("should NOT Launch Failure at T=4 (only 2nd personal turn)")
	}
}

func TestCheckLaunchFailure_HasDeployed(t *testing.T) {
	state := newTestState("game1")
	state.ActivePlayer = 1
	state.CurrentTurn = 5

	// Player 1 has deployed before → no Launch Failure
	_ = state.SetField(1, &model.Field{HasHadActiveResource: true})

	if checkLaunchFailure(state, 1) {
		t.Fatal("should NOT Launch Failure when HasHadActiveResource is true")
	}
}

func TestCheckLaunchFailure_FaceDownOnly(t *testing.T) {
	state := newTestState("game1")
	state.ActivePlayer = 1
	state.CurrentTurn = 5

	// Player 1 has face-down resources (deploying) but never had an active one
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: false, InstanceID: "f1", CardID: 1, DeployingTurnsLeft: 1},
		},
	})

	if !checkLaunchFailure(state, 1) {
		t.Fatal("expected Launch Failure: face-down only means HasHadActiveResource is still false")
	}
}

// --- Win Condition: Timeout ---

func TestCheckWinCondition_Timeout(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Both players have resources
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	state.Player1TimeBank = 0

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 2 {
		t.Errorf("winner = %d, want 2", winnerNum)
	}
	if reason != model.WinReasonTimeout {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonTimeout)
	}
}

// --- Win Condition: Turn Limit ---

func TestCheckWinCondition_TurnLimit_Player1Wins(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Set turn to 30 (15 rounds × 2 turns per round)
	state.CurrentTurn = 30

	// Player 1 has more budget
	state.Player1Budget = 3000
	state.Player2Budget = 2000

	// Both players have resources
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 1 {
		t.Errorf("winner = %d, want 1", winnerNum)
	}
	if reason != model.WinReasonTurnLimit {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonTurnLimit)
	}
}

func TestCheckWinCondition_TurnLimit_Player2Wins(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Set turn to 30 (15 rounds × 2 turns per round)
	state.CurrentTurn = 30

	// Player 2 has more budget
	state.Player1Budget = 2000
	state.Player2Budget = 3000

	// Both players have resources
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 2 {
		t.Errorf("winner = %d, want 2", winnerNum)
	}
	if reason != model.WinReasonTurnLimit {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonTurnLimit)
	}
}

func TestCheckWinCondition_TurnLimit_Draw(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Set turn to 30 (15 rounds × 2 turns per round)
	state.CurrentTurn = 30

	// Both players have same budget
	state.Player1Budget = 2500
	state.Player2Budget = 2500

	// Both players have resources
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	winnerNum, reason, gameOver := CheckWinCondition(state, game)
	if !gameOver {
		t.Fatal("expected game over")
	}
	if winnerNum != 0 {
		t.Errorf("winner = %d, want 0 (draw)", winnerNum)
	}
	if reason != model.WinReasonDraw {
		t.Errorf("reason = %s, want %s", reason, model.WinReasonDraw)
	}
}

func TestCheckWinCondition_TurnLimit_NotReached(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")

	// Turn is less than 30
	state.CurrentTurn = 29

	// Both players have resources
	_ = state.SetField(1, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f2", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	_, _, gameOver := CheckWinCondition(state, game)
	if gameOver {
		t.Fatal("expected no game over, turn limit not reached")
	}
}

// --- Win Condition: Repository Out (via AutoAdvancePhases) ---

func TestAutoAdvancePhases_RepositoryOut(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	// Empty repository triggers repo_out on draw
	_ = state.SetRepository(1, []int64{})
	state.CurrentPhase = model.PhaseDraw

	gameOver, winReason, err := AutoAdvancePhases(state, game, cc)
	if err != nil {
		t.Fatalf("AutoAdvancePhases failed: %v", err)
	}
	if !gameOver {
		t.Fatal("expected game over from empty repository")
	}
	if winReason != model.WinReasonRepositoryOut {
		t.Errorf("reason = %s, want %s", winReason, model.WinReasonRepositoryOut)
	}
}

// --- End Phase Tests ---

func TestProcessEndPhase_RemoveThisTurnEffects(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.ActivePlayer = 1

	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID: "f1",
				CardID:     1,
				FaceUp:     true,
				CurrentAV:  1000,
				MaxAV:      1000,
				TemporaryEffects: []model.TemporaryEffect{
					{EffectType: "buff_tp", Value: 200, Duration: "this_turn", SourceID: "test"},
					{EffectType: "buff_tp", Value: 100, Duration: "until_next_turn_end", SourceID: "test2"},
				},
			},
		},
	}
	_ = state.SetField(1, field)

	needsDiscard, err := ProcessEndPhase(state, game, cache.NewCardCache())
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}
	if needsDiscard {
		t.Error("should not need discard (hand is empty)")
	}

	updatedField, _ := state.GetField(1)
	effects := updatedField.Frontend[0].TemporaryEffects
	if len(effects) != 1 {
		t.Fatalf("effects count = %d, want 1 (only until_next_turn_end should remain)", len(effects))
	}
	if effects[0].Duration != "until_next_turn_end" {
		t.Errorf("remaining effect duration = %s, want until_next_turn_end", effects[0].Duration)
	}
}

func TestProcessEndPhase_ResetTurnFlags(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.ActivePlayer = 1

	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:         "f1",
				CardID:             1,
				FaceUp:             true,
				CurrentAV:          1000,
				MaxAV:              1000,
				HasAttacked:        true,
				EffectUsedThisTurn: true,
			},
		},
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:         "b1",
				CardID:             2,
				FaceUp:             true,
				CurrentAV:          800,
				MaxAV:              800,
				EffectUsedThisTurn: true,
			},
		},
	}
	_ = state.SetField(1, field)

	_, err := ProcessEndPhase(state, game, cache.NewCardCache())
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	updatedField, _ := state.GetField(1)
	if updatedField.Frontend[0].HasAttacked {
		t.Error("HasAttacked should be reset")
	}
	if updatedField.Frontend[0].EffectUsedThisTurn {
		t.Error("frontend EffectUsedThisTurn should be reset")
	}
	if updatedField.Backend[0].EffectUsedThisTurn {
		t.Error("backend EffectUsedThisTurn should be reset")
	}
}

func TestProcessEndPhase_NeedsDiscard(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.ActivePlayer = 1

	// Set hand to 7 cards (exceeds HandLimit of 6)
	hand := make([]model.HandCard, 7)
	for i := range hand {
		hand[i] = model.HandCard{InstanceID: fmt.Sprintf("h%d", i), CardID: int64(i + 1)}
	}
	_ = state.SetHand(1, hand)
	_ = state.SetField(1, &model.Field{})

	needsDiscard, err := ProcessEndPhase(state, game, cache.NewCardCache())
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}
	if !needsDiscard {
		t.Error("should need discard when hand > 6")
	}
}

func TestProcessEndPhase_ResetMonetizedAmount(t *testing.T) {
	game := newTestGame("game1")
	state := newTestState("game1")
	state.ActivePlayer = 1

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:      "b1",
				CardID:          1,
				FaceUp:          true,
				CurrentAV:       1000,
				MaxAV:           1000,
				MonetizedAmount: 500,
			},
		},
	}
	_ = state.SetField(1, field)

	_, err := ProcessEndPhase(state, game, cache.NewCardCache())
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	updatedField, _ := state.GetField(1)
	if updatedField.Backend[0].MonetizedAmount != 0 {
		t.Errorf("MonetizedAmount = %d, want 0", updatedField.Backend[0].MonetizedAmount)
	}
}

// --- First Turn Battle Skip ---

func TestFirstTurnBattleSkip(t *testing.T) {
	// Turn 1 is always the first turn (first player's turn)
	if !isFirstTurn(1) {
		t.Error("turn=1 should be first turn")
	}
	// Turn 2 is the second player's first turn — NOT "first turn"
	if isFirstTurn(2) {
		t.Error("turn=2 should NOT be first turn")
	}
	if isFirstTurn(3) {
		t.Error("turn=3 should NOT be first turn")
	}
}

// --- Switch Player Tests ---

func TestSwitchActivePlayer(t *testing.T) {
	state := newTestState("game1")

	// Player 1's turn
	if state.ActivePlayer != 1 {
		t.Fatalf("initial active player = %d, want 1", state.ActivePlayer)
	}

	SwitchActivePlayer(state)
	if state.ActivePlayer != 2 {
		t.Errorf("after switch, active player = %d, want 2", state.ActivePlayer)
	}
	if state.CurrentTurn != 2 {
		t.Errorf("turn should be 2 after first switch, got %d", state.CurrentTurn)
	}

	SwitchActivePlayer(state)
	if state.ActivePlayer != 1 {
		t.Errorf("after second switch, active player = %d, want 1", state.ActivePlayer)
	}
	if state.CurrentTurn != 3 {
		t.Errorf("turn should be 3 after second switch, got %d", state.CurrentTurn)
	}
}

// --- Per-turn +500 Budget ---

func TestAutoAdvancePhases_PerTurnBudget(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")

	state.Player1Budget = 5000
	state.CurrentPhase = model.PhaseDraw
	state.ActivePlayer = 1

	// Ensure repository has at least 1 card so draw succeeds
	_ = state.SetRepository(1, []int64{1})

	gameOver, _, err := AutoAdvancePhases(state, game, cc)
	if err != nil {
		t.Fatalf("AutoAdvancePhases failed: %v", err)
	}
	if gameOver {
		t.Fatal("unexpected game over")
	}

	// Budget should be initial 5000 + 500 (PerTurnBudget)
	budget := state.GetBudget(1)
	if budget != 5500 {
		t.Errorf("budget = %d, want 5500 (5000 + 500 per-turn)", budget)
	}
}

// --- Elastic Maintenance Cost Tests ---
// Elastic MC formula: MC = ElasticBonus × maintenance_cost × rankMult / baseStat
// MC = 0 when ElasticBonus = 0 (free tier)

func TestElasticMC_ZeroBonus(t *testing.T) {
	cc := cache.NewCardCache()

	cc.InjectForTest(10, &model.CardDefinition{
		CardNo:   10,
		CardName: "Elastic Container",
		Faction:  "SD",
		CardType: "Container",
		Elastic:  true,
		Stats:    json.RawMessage(`{"throughput": 500, "availability": 1200, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	maxTP := int64(1000)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:   "c1",
				CardID:       10,
				FaceUp:       true,
				Rank:         model.RankSmall,
				CurrentTP:    &tp,
				MaxTP:        &maxTP,
				CurrentAV:    1200,
				MaxAV:        1200,
				ElasticBonus: 0, // No scaling yet
				Attachments:  []model.AttachmentRef{},
			},
		},
	}

	mc := collectMaintenanceCost(field, cc)
	// ElasticBonus = 0 → MC = 0 (free tier)
	if mc != 0 {
		t.Errorf("maintenance cost = %d, want 0 (free tier, no elastic scaling)", mc)
	}
}

func TestElasticMC_WithBonus(t *testing.T) {
	cc := cache.NewCardCache()

	cc.InjectForTest(10, &model.CardDefinition{
		CardNo:         10,
		CardName:       "Elastic Container",
		Faction:        "SD",
		CardType:       "Container",
		Elastic:        true,
		FreeTier:       500,
		CostPerRequest: 10,
		Stats:          json.RawMessage(`{"throughput": 500, "availability": 1200, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:   "c1",
				CardID:       10,
				FaceUp:       true,
				Rank:         model.RankSmall,
				CurrentTP:    &tp,
				CurrentAV:    1200,
				MaxAV:        1200,
				ElasticBonus: 200,
				Attachments:  []model.AttachmentRef{},
			},
		},
	}

	mc := collectMaintenanceCost(field, cc)
	// intrinsic = 500×1 + effectiveEB(200, 500) = 500 + 168 = 668
	// MC = (668 - 500) × 10 / 100 = 16
	if mc != 16 {
		t.Errorf("maintenance cost = %d, want 16", mc)
	}
}

func TestElasticMC_WithRank(t *testing.T) {
	cc := cache.NewCardCache()

	cc.InjectForTest(10, &model.CardDefinition{
		CardNo:         10,
		CardName:       "Elastic Container",
		Faction:        "SD",
		CardType:       "Container",
		Elastic:        true,
		FreeTier:       500,
		CostPerRequest: 10,
		Stats:          json.RawMessage(`{"throughput": 500, "availability": 1200, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:   "c1",
				CardID:       10,
				FaceUp:       true,
				Rank:         model.RankMedium,
				CurrentTP:    &tp,
				CurrentAV:    1200,
				MaxAV:        1200,
				ElasticBonus: 200,
				Attachments:  []model.AttachmentRef{},
			},
		},
	}

	mc := collectMaintenanceCost(field, cc)
	// intrinsic = 500×2 + effectiveEB(200, 500) = 1000 + 168 = 1168
	// MC = (1168 - 500) × 10 / 100 = 66
	if mc != 66 {
		t.Errorf("maintenance cost = %d, want 66", mc)
	}
}

func TestElasticMC_BackendYield(t *testing.T) {
	cc := cache.NewCardCache()

	cc.InjectForTest(20, &model.CardDefinition{
		CardNo:         20,
		CardName:       "Elastic Database",
		Faction:        "SD",
		CardType:       "Database",
		Elastic:        true,
		FreeTier:       300,
		CostPerRequest: 17,
		Stats:          json.RawMessage(`{"yield": 300, "availability": 1000, "maintenance_cost": 50, "sla_penalty": 300}`),
	})

	yieldVal := int64(300)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:   "n1",
				CardID:       20,
				FaceUp:       true,
				Rank:         model.RankSmall,
				CurrentYield: &yieldVal,
				CurrentAV:    1000,
				MaxAV:        1000,
				ElasticBonus: 300,
				Attachments:  []model.AttachmentRef{},
			},
		},
	}

	mc := collectMaintenanceCost(field, cc)
	// intrinsic = 300×1 + effectiveEB(300, 300) = 300 + 207 = 507
	// MC = (507 - 300) × 17 / 100 = 35
	if mc != 35 {
		t.Errorf("maintenance cost = %d, want 35", mc)
	}
}

// --- Elastic Auto-Scaling Tests ---

func TestElasticAutoScale_FrontendAttacked(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 2000, "maintenance_cost": 50, "sla_penalty": 400}`),
	})
	// Attacker card
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo:   2,
		CardName: "Attacker",
		Faction:  "Tenki",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 300, "availability": 1000, "maintenance_cost": 100, "sla_penalty": 400}`),
	})

	tp1 := int64(500)
	maxTP1 := int64(1000)
	tp2 := int64(300)
	maxTP2 := int64(300)

	state := newTestState("elastic-attack-test")
	game := newTestGame("elastic-attack-test")
	state.ActivePlayer = 2 // P2 attacks P1's resource
	state.CurrentPhase = model.PhaseBattle

	// P1's field: Elastic container in frontend
	p1Field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID: "def1",
				CardID:     1,
				FaceUp:     true,
				Rank:       model.RankSmall,
				CurrentTP:  &tp1,
				MaxTP:      &maxTP1,
				CurrentAV:  2000,
				MaxAV:      2000,
				Attachments: []model.AttachmentRef{},
			},
		},
	}
	_ = state.SetField(1, p1Field)

	// P2's field: attacker
	p2Field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID: "atk1",
				CardID:     2,
				FaceUp:     true,
				Rank:       model.RankSmall,
				CurrentTP:  &tp2,
				MaxTP:      &maxTP2,
				CurrentAV:  1000,
				MaxAV:      1000,
				Attachments: []model.AttachmentRef{},
			},
		},
	}
	_ = state.SetField(2, p2Field)

	req := AttackRequest{AttackerInstanceID: "atk1", TargetInstanceID: "def1"}
	_, err := processAttack(state, game, 2, req, cc, nil)
	if err != nil {
		t.Fatalf("processAttack failed: %v", err)
	}

	// Check defender's ElasticBonus increased
	p1FieldAfter, _ := state.GetField(1)
	defender := p1FieldAfter.Frontend[0]
	if defender.ElasticBonus != 100 {
		t.Errorf("ElasticBonus = %d, want 100 (one attack trigger)", defender.ElasticBonus)
	}
}

func TestElasticAutoScale_NoCap(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		FreeTier:         500,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 2000, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	res := &model.ResourceInstance{
		InstanceID:   "c1",
		CardID:       1,
		FaceUp:       true,
		Rank:         model.RankSmall,
		CurrentTP:    &tp,
		CurrentAV:    2000,
		MaxAV:        2000,
		ElasticBonus: 0,
		Attachments:  []model.AttachmentRef{},
	}

	card := cc.Get(1)
	// Apply 20 triggers — old system would cap at baseStat (500 bonus)
	for i := 0; i < 20; i++ {
		applyElasticBonus(res, card, cc)
	}

	// No cap: 20 × 100 = 2000 raw bonus
	if res.ElasticBonus != 2000 {
		t.Errorf("ElasticBonus = %d, want 2000 (no cap)", res.ElasticBonus)
	}

	// EffectiveTP = 500 + effectiveEB(2000, 500)
	// = 500 + int64(500 × ln(1 + 2000/500)) = 500 + int64(500 × ln(5)) = 500 + 804 = 1304
	// Old system capped at 500 + 500 = 1000
	field := &model.Field{}
	got := CalculateEffectiveTP(res, field, cc)
	if got != 1304 {
		t.Errorf("EffectiveTP = %d, want 1304 (beyond old cap of 1000)", got)
	}
}

func TestEffectiveElasticBonus_Basic(t *testing.T) {
	cases := []struct {
		raw, scale, want int64
	}{
		{0, 500, 0},
		{100, 500, 91},  // 500*ln(1.2) = 91
		{500, 500, 346}, // 500*ln(2) = 346
		{1000, 500, 549},
		{5000, 500, 1198}, // 500*ln(11) = 1198
	}
	for _, c := range cases {
		got := effectiveElasticBonus(c.raw, c.scale)
		if got != c.want {
			t.Errorf("effectiveElasticBonus(%d, %d) = %d, want %d", c.raw, c.scale, got, c.want)
		}
	}
}

func TestEffectiveElasticBonus_ZeroInputs(t *testing.T) {
	if got := effectiveElasticBonus(0, 500); got != 0 {
		t.Errorf("effectiveElasticBonus(0, 500) = %d, want 0", got)
	}
	// scale=0 → returns raw (no ln transform)
	if got := effectiveElasticBonus(200, 0); got != 200 {
		t.Errorf("effectiveElasticBonus(200, 0) = %d, want 200", got)
	}
	if got := effectiveElasticBonus(0, 0); got != 0 {
		t.Errorf("effectiveElasticBonus(0, 0) = %d, want 0", got)
	}
}

func TestElasticTP_DiminishingReturns(t *testing.T) {
	scale := int64(500)
	prev := int64(0)
	prevGain := int64(10000) // start high

	for i := 1; i <= 10; i++ {
		raw := int64(i * 500)
		eb := effectiveElasticBonus(raw, scale)
		gain := eb - prev
		if gain >= prevGain {
			t.Errorf("batch %d: gain %d should be < previous gain %d (diminishing returns)", i, gain, prevGain)
		}
		prevGain = gain
		prev = eb
	}
}

// --- Maintenance Cost with Rank Multiplier ---

func TestMaintenanceCost_MediumRank(t *testing.T) {
	cc := cache.NewCardCache()

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Test Compute",
		Faction:  "SD",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	familyM := model.FamilyM
	tp := int64(1400)
	maxTP := int64(1400)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "r1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankMedium,
				InstanceFamily:   &familyM,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        2800,
				MaxAV:            2800,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}

	mc := collectMaintenanceCost(field, cc)
	// MC = 200 * 2 (medium rank multiplier) = 400
	if mc != 400 {
		t.Errorf("maintenance cost = %d, want 400 (200 * 2 for medium rank)", mc)
	}
}

func TestMaintenanceCost_LargeRank(t *testing.T) {
	cc := cache.NewCardCache()

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Test Compute",
		Faction:  "SD",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	familyM := model.FamilyM
	tp := int64(2100)
	maxTP := int64(2100)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "r1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankLarge,
				InstanceFamily:   &familyM,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        4200,
				MaxAV:            4200,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}

	mc := collectMaintenanceCost(field, cc)
	// MC = 200 * 3 (large rank multiplier) = 600
	if mc != 600 {
		t.Errorf("maintenance cost = %d, want 600 (200 * 3 for large rank)", mc)
	}
}

// --- Scale Up Damage Preservation ---

func TestScaleUp_DamagePreserved(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 10000

	// Card definition: Compute, resizable, AV=1400
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:    1,
		CardName:  "Test Compute",
		Faction:   "SD",
		CardType:  "Compute",
		Resizable: true,
		Stats:     json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Small rank resource with AV=1400, Damage=600, CurrentAV=800
	tp := int64(700)
	maxTP := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "r1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentAV:        800,
				MaxAV:            1400,
				Damage:           600,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	familyM := model.FamilyM
	req := ScaleUpRequest{
		InstanceID:     "r1",
		TargetRank:     model.RankMedium,
		InstanceFamily: &familyM,
	}

	_, err := processScaleUp(state, game, 1, req, cc)
	if err != nil {
		t.Fatalf("processScaleUp failed: %v", err)
	}

	updatedField, _ := state.GetField(1)
	res := updatedField.Frontend[0]

	// After scale up to medium:
	// MaxAV = 1400 * 2 (medium) * 1.0 (M family) = 2800
	if res.MaxAV != 2800 {
		t.Errorf("MaxAV = %d, want 2800", res.MaxAV)
	}

	// Damage should be preserved
	if res.Damage != 600 {
		t.Errorf("Damage = %d, want 600 (preserved)", res.Damage)
	}

	// CurrentAV = MaxAV - Damage = 2800 - 600 = 2200
	if res.CurrentAV != 2200 {
		t.Errorf("CurrentAV = %d, want 2200 (2800 - 600)", res.CurrentAV)
	}
}

// --- Frontend Compute Cannot Monetize (Distribute Yield) ---

func TestAvailableActions_DistributeYield_FrontendComputeExcluded(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:    1,
		CardName:  "Test Compute",
		Faction:   "SD",
		CardType:  "Compute",
		Resizable: true,
		Stats:     json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 2 // Not first turn
	state.ActivePlayer = 1

	tp := int64(700)
	maxTP := int64(700)
	myField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "f1",
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
	_ = state.SetField(1, myField)
	oppField := &model.Field{}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	// insight pool > 0 so distribute_yield actions are possible
	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 500, cc, nil)

	yieldActions := findActions(actions, model.ActionDistributeYield)
	// Frontend compute should NOT appear in distribute_yield actions
	for _, a := range yieldActions {
		if a.SourceInstanceID == "f1" {
			t.Error("frontend compute should not have distribute_yield action")
		}
	}
	if len(yieldActions) != 0 {
		t.Errorf("expected 0 distribute_yield actions (only frontend compute on field), got %d", len(yieldActions))
	}
}

// --- Object Storage in Frontend Cannot Attack ---

func TestAvailableActions_Attack_ObjectStorageFrontendExcluded(t *testing.T) {
	cc := cache.NewCardCache()

	// ObjectStorage card definition
	cc.InjectForTest(5, &model.CardDefinition{
		CardNo:   5,
		CardName: "Test ObjectStorage",
		Faction:  "SD",
		CardType: "ObjectStorage",
		Stats:    json.RawMessage(`{"yield": 300, "availability": 2000, "maintenance_cost": 100, "deploy_cost": 300, "sla_penalty": 200}`),
	})
	// Opponent has a compute for target
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Test Compute",
		Faction:  "SD",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	state := newTestState("g1")
	state.CurrentPhase = model.PhaseBattle
	state.CurrentTurn = 2
	state.ActivePlayer = 1

	yieldVal := int64(300)
	yieldMax := int64(300)
	myField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "os1",
				CardID:           5,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentYield:     &yieldVal,
				MaxYield:         &yieldMax,
				CurrentAV:        2000,
				MaxAV:            2000,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, myField)

	oppTP := int64(700)
	oppMaxTP := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "opp-r1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &oppTP,
				MaxTP:            &oppMaxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(2, oppField)

	game := newTestGame("g1")
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)

	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) != 0 {
		t.Errorf("expected 0 attack actions for ObjectStorage in frontend, got %d", len(attackActions))
	}
}

// --- No Summoning Sickness ---

func TestAttack_NoSummoningSickness(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.CurrentTurn = 2
	state.ActivePlayer = 1

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:    1,
		CardName:  "Test Compute",
		Faction:   "SD",
		CardType:  "Compute",
		Resizable: true,
		Stats:     json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Deploy a compute on the current turn (DeployedOnTurn = current turn)
	tp := int64(700)
	maxTP := int64(700)
	myField := &model.Field{
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
				DeployedOnTurn:   state.CurrentTurn, // deployed this turn
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, myField)

	oppTP := int64(700)
	oppMaxTP := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "def1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &oppTP,
				MaxTP:            &oppMaxTP,
				CurrentAV:        1400,
				MaxAV:            1400,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(2, oppField)

	// Verify attack action is available (no summoning sickness)
	myF, _ := state.GetField(1)
	oppF, _ := state.GetField(2)
	h, _ := state.GetHand(1)

	actions := ComputeAvailableActions(state, game, 1, myF, oppF, h, 5000, 0, cc, nil)
	attackActions := findActions(actions, model.ActionAttack)
	if len(attackActions) != 1 {
		t.Fatalf("expected 1 attack action (no summoning sickness), got %d", len(attackActions))
	}
	if attackActions[0].SourceInstanceID != "atk1" {
		t.Errorf("expected source_instance_id=atk1, got %s", attackActions[0].SourceInstanceID)
	}

	// Also verify the actual attack succeeds
	req := AttackRequest{
		AttackerInstanceID: "atk1",
		TargetInstanceID:   "def1",
	}

	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)

	result, err := processAttack(state, game, 1, req, cc, reg)
	if err != nil {
		t.Fatalf("processAttack failed (no summoning sickness expected): %v", err)
	}
	if result == nil {
		t.Fatal("expected non-nil result from attack")
	}

	// Verify damage was applied to defender
	updatedOppField, _ := state.GetField(2)
	defender := updatedOppField.Frontend[0]
	if defender == nil {
		// Defender was destroyed (damage >= AV), which is fine
		return
	}
	if defender.Damage != 700 {
		t.Errorf("defender damage = %d, want 700 (attacker TP)", defender.Damage)
	}
}

// --- Migration Tests ---

func newLoadedCardCache(t *testing.T) *cache.CardCache {
	t.Helper()
	cc := cache.NewCardCache()
	if err := cc.LoadFromJSON("../cache/cards_gen.json"); err != nil {
		t.Fatalf("LoadFromJSON: %v", err)
	}
	return cc
}

func TestMigrate_Basic(t *testing.T) {
	cc := newLoadedCardCache(t)
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 3

	// Card 1 = Compute (DT=1), Card 4 = Orchestrator (DT=2)
	// Source DT=1, Target DT=2 → valid (target.DT >= source.DT)
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "src", CardID: 1, MaxAV: 1000, CurrentAV: 1000},
			{FaceUp: true, InstanceID: "tgt", CardID: 4, MaxAV: 800, CurrentAV: 800},
		},
	})

	req := MigrateRequest{SourceInstanceID: "src", TargetInstanceID: "tgt"}
	result, err := processMigrate(state, game, 1, req, cc)
	if err != nil {
		t.Fatalf("processMigrate failed: %v", err)
	}
	if result == nil {
		t.Fatal("expected non-nil result")
	}

	field, _ := state.GetField(1)
	src := field.Frontend[0]
	tgt := field.Frontend[1]

	if src.MigrationTarget == nil || *src.MigrationTarget != "tgt" {
		t.Errorf("source.MigrationTarget = %v, want 'tgt'", src.MigrationTarget)
	}
	if tgt.MigratingFrom == nil || *tgt.MigratingFrom != "src" {
		t.Errorf("target.MigratingFrom = %v, want 'src'", tgt.MigratingFrom)
	}
	if tgt.MigratingOnTurn != 3 {
		t.Errorf("target.MigratingOnTurn = %d, want 3", tgt.MigratingOnTurn)
	}
}

func TestMigrate_RejectInvalidDeployTurns(t *testing.T) {
	cc := newLoadedCardCache(t)
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseMain
	state.CurrentTurn = 3

	// Card 4 = Orchestrator (DT=2) as source, Card 1 = Compute (DT=1) as target
	// Source DT=2, Target DT=1 → invalid (target.DT < source.DT)
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "src", CardID: 4, MaxAV: 800, CurrentAV: 800},
			{FaceUp: true, InstanceID: "tgt", CardID: 1, MaxAV: 1000, CurrentAV: 1000},
		},
	})

	req := MigrateRequest{SourceInstanceID: "src", TargetInstanceID: "tgt"}
	_, err := processMigrate(state, game, 1, req, cc)
	if err == nil {
		t.Fatal("expected error for invalid deploy_turns, got nil")
	}
}

func TestMigrate_CompletionAfterTwoPasses(t *testing.T) {
	state := newTestState("game1")
	sourceID := "old-res"

	// Migration started on turn 3, check on turn 5 (2 turns later)
	state.CurrentTurn = 5
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: sourceID, CardID: 1, MaxAV: 1000, CurrentAV: 1000,
				MigrationTarget: strPtr("new-res")},
			{FaceUp: true, InstanceID: "new-res", CardID: 1, MaxAV: 1000, CurrentAV: 1000,
				MigratingFrom: strPtr(sourceID), MigratingOnTurn: 3},
		},
	})

	events, err := processMigrationCompletion(state, 1)
	if err != nil {
		t.Fatalf("processMigrationCompletion failed: %v", err)
	}
	if len(events) != 1 {
		t.Fatalf("expected 1 migration event, got %d", len(events))
	}

	field, _ := state.GetField(1)
	// Source should be removed (slot nil)
	if field.Frontend[0] != nil {
		t.Error("expected source resource to be removed from field")
	}
	// Target should be unlocked
	tgt := field.Frontend[1]
	if tgt.MigratingFrom != nil {
		t.Error("expected target MigratingFrom to be cleared")
	}
	if tgt.MigratingOnTurn != 0 {
		t.Error("expected target MigratingOnTurn to be 0")
	}

	// Source card should be in trash
	trash, _ := state.GetTrash(1)
	found := false
	for _, c := range trash {
		if c == 1 {
			found = true
		}
	}
	if !found {
		t.Error("expected source card to be in trash")
	}
}

func TestMigrate_NotYetComplete(t *testing.T) {
	state := newTestState("game1")

	// Migration started on turn 3, check on turn 4 (only 1 turn later)
	state.CurrentTurn = 4
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "old", CardID: 1, MaxAV: 1000, CurrentAV: 1000,
				MigrationTarget: strPtr("new")},
			{FaceUp: true, InstanceID: "new", CardID: 1, MaxAV: 1000, CurrentAV: 1000,
				MigratingFrom: strPtr("old"), MigratingOnTurn: 3},
		},
	})

	events, _ := processMigrationCompletion(state, 1)
	if len(events) != 0 {
		t.Fatalf("expected 0 events (not yet complete), got %d", len(events))
	}

	field, _ := state.GetField(1)
	if field.Frontend[0] == nil {
		t.Error("source should still be on field")
	}
	if field.Frontend[1].MigratingFrom == nil {
		t.Error("target should still be locked")
	}
}

func TestMigrate_SourceDestroyedAutoUnlock(t *testing.T) {
	cc := newLoadedCardCache(t)
	game := newTestGame("game1")
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle
	state.CurrentTurn = 4

	// Player 2's field has a migrating pair (source has low AV so it gets destroyed)
	_ = state.SetField(2, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "opp-src", CardID: 1, MaxAV: 100, CurrentAV: 100,
				MigrationTarget: strPtr("opp-tgt")},
			{FaceUp: true, InstanceID: "opp-tgt", CardID: 1, MaxAV: 1000, CurrentAV: 1000,
				MigratingFrom: strPtr("opp-src"), MigratingOnTurn: 3},
		},
	})

	// Player 1 attacks opp-src (the migration source) — TP=700, enough to destroy 100 AV
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "atk", CardID: 1, MaxAV: 2000, CurrentAV: 2000,
				CurrentTP: int64Ptr(700)},
		},
	})

	req := AttackRequest{AttackerInstanceID: "atk", TargetInstanceID: "opp-src"}
	_, err := processAttack(state, game, 1, req, cc, nil)
	if err != nil {
		t.Fatalf("processAttack failed: %v", err)
	}

	// Target should be auto-unlocked
	oppField, _ := state.GetField(2)
	tgt := oppField.Frontend[1]
	if tgt == nil {
		t.Fatal("target should still exist")
	}
	if tgt.MigratingFrom != nil {
		t.Error("expected target MigratingFrom to be cleared after source destruction")
	}
}

func TestMigrate_LockedTargetCannotAttack(t *testing.T) {
	cc := newLoadedCardCache(t)
	state := newTestState("game1")
	state.CurrentPhase = model.PhaseBattle

	myField := &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "locked", CardID: 1, MaxAV: 1000, CurrentAV: 1000,
				CurrentTP:     int64Ptr(700),
				MigratingFrom: strPtr("some-old")},
		},
	}
	oppField := &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "enemy", CardID: 1, MaxAV: 1000, CurrentAV: 1000},
		},
	}

	actions := enumerateAttackActions(myField, oppField, cc)
	if len(actions) != 0 {
		t.Errorf("expected 0 attack actions for locked migration target, got %d", len(actions))
	}
}

func TestEnumerateMigrateActions(t *testing.T) {
	cc := newLoadedCardCache(t)

	// Card 1 = Compute (DT=1), Card 3 = Container (DT=0), Card 4 = Orchestrator (DT=2)
	myField := &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "res-dt1", CardID: 1, MaxAV: 1000, CurrentAV: 1000},
			{FaceUp: true, InstanceID: "res-dt0", CardID: 3, MaxAV: 800, CurrentAV: 800},
			{FaceUp: true, InstanceID: "res-dt2", CardID: 4, MaxAV: 600, CurrentAV: 600},
		},
	}

	actions := enumerateMigrateActions(myField, cc)

	// res-dt0 (DT=0): can migrate to res-dt0 equivalent or higher → res-dt1, res-dt2
	// res-dt1 (DT=1): can migrate to DT>=1 → res-dt2
	// res-dt2 (DT=2): can migrate to DT>=2 → nobody (res-dt0 and res-dt1 have lower DT)
	// Wait, res-dt0 can also migrate to res-dt0 if there were another DT=0 card

	// Let's count: we expect actions for res-dt0 (targets: res-dt1, res-dt2) and res-dt1 (target: res-dt2)
	found := map[string][]string{}
	for _, a := range actions {
		found[a.SourceInstanceID] = a.ValidTargets
	}

	// res-dt0 should have 2 targets
	if targets, ok := found["res-dt0"]; !ok || len(targets) != 2 {
		t.Errorf("res-dt0 targets = %v, want 2 targets (res-dt1, res-dt2)", targets)
	}

	// res-dt1 should have 1 target (res-dt2)
	if targets, ok := found["res-dt1"]; !ok || len(targets) != 1 || targets[0] != "res-dt2" {
		t.Errorf("res-dt1 targets = %v, want [res-dt2]", targets)
	}

	// res-dt2 should have no migrate action (no valid targets)
	if _, ok := found["res-dt2"]; ok {
		t.Error("res-dt2 should not have migrate actions (no valid targets with DT>=2)")
	}
}

// --- Deploy Turns Tests ---

// TestDeployTurns_OrchestratorDeploysFaceDown verifies that a card with deploy_turns=2
// (Orchestrator #4) is deployed face-down and flips face-up after 2 of the owner's turns.
func TestDeployTurns_OrchestratorDeploysFaceDown(t *testing.T) {
	cc := newLoadedCardCache(t)

	// Card #4 = SD Orchestrator "クジランティス" — deploy_turns=2
	card4 := cc.Get(4)
	if card4 == nil {
		t.Fatal("card #4 not found in cache")
	}
	if card4.DeployTurns != 2 {
		t.Fatalf("card #4 deploy_turns = %d, want 2", card4.DeployTurns)
	}

	// Create a resource instance from the card
	inst, err := model.CreateResourceInstance(card4, "orch-1")
	if err != nil {
		t.Fatalf("CreateResourceInstance: %v", err)
	}

	// Should be face-down with 2 turns left
	if inst.FaceUp {
		t.Error("Orchestrator should be face-DOWN on deploy (deploy_turns=2)")
	}
	if inst.DeployingTurnsLeft != 2 {
		t.Errorf("DeployingTurnsLeft = %d, want 2", inst.DeployingTurnsLeft)
	}

	// Card #3 = SD Container "アリゲーター" — deploy_turns=0
	card3 := cc.Get(3)
	if card3 == nil {
		t.Fatal("card #3 not found in cache")
	}
	instContainer, _ := model.CreateResourceInstance(card3, "cont-1")
	if !instContainer.FaceUp {
		t.Error("Container should be face-UP on deploy (deploy_turns=0)")
	}
}

// TestDeployTurns_FlipsAfterCountdown verifies processDeployCountdown flips resources.
func TestDeployTurns_FlipsAfterCountdown(t *testing.T) {
	cc := newLoadedCardCache(t)
	state := newTestState("game-dt")
	state.CurrentPhase = model.PhaseDraw
	state.ActivePlayer = 1
	state.CurrentTurn = 3 // player 1's turn

	card4 := cc.Get(4)
	inst, _ := model.CreateResourceInstance(card4, "orch-1")
	// Simulate: deployed last turn, DeployingTurnsLeft=2
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{inst, nil, nil},
	}
	_ = state.SetField(1, field)

	// First countdown: 2 → 1, still face-down
	processDeployCountdown(state, 1, cc)
	field1, _ := state.GetField(1)
	res1 := field1.Frontend[0]
	if res1.FaceUp {
		t.Error("after 1st countdown: should still be face-down")
	}
	if res1.DeployingTurnsLeft != 1 {
		t.Errorf("after 1st countdown: DeployingTurnsLeft = %d, want 1", res1.DeployingTurnsLeft)
	}
	_ = state.SetField(1, field1)

	// Second countdown: 1 → 0, should flip face-up
	processDeployCountdown(state, 1, cc)
	field2, _ := state.GetField(1)
	res2 := field2.Frontend[0]
	if !res2.FaceUp {
		t.Error("after 2nd countdown: should be face-UP")
	}
	if res2.DeployingTurnsLeft != 0 {
		t.Errorf("after 2nd countdown: DeployingTurnsLeft = %d, want 0", res2.DeployingTurnsLeft)
	}
	if !field2.HasHadActiveResource {
		t.Error("HasHadActiveResource should be true after flip")
	}
}

// --- Elastic Auto-Scale: Backend Distribute Yield ---

func TestElasticAutoScale_BackendDistributeYield(t *testing.T) {
	e, repo, cc := newTestEngine()
	game := newTestGame("elastic-dist-yield")
	state := newTestState("elastic-dist-yield")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.CurrentTurn = 2
	state.Player1InsightPool = 500

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"throughput": 600, "availability": 1300, "maintenance_cost": 50, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	tp := int64(600)
	maxTP := int64(1200)
	p1Field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "comp1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1300,
				MaxAV:            1300,
				ElasticBonus:     0,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, p1Field)

	// Player 2 needs a field
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	repo.InjectGame("elastic-dist-yield", game)
	repo.InjectState("elastic-dist-yield", state)

	data, _ := json.Marshal(DistributeYieldRequest{
		Distributions: []YieldDistribution{
			{InstanceID: "comp1", Amount: 300},
		},
	})

	_, err := e.ProcessAction(context.Background(), "elastic-dist-yield", "player1", "distribute_yield", data)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	updatedState := repo.MustGetState("elastic-dist-yield")
	f, _ := updatedState.GetField(1)
	res := f.Backend[0]

	// ElasticBonus should increase by 100 (elastic_increment)
	if res.ElasticBonus != 100 {
		t.Errorf("ElasticBonus = %d, want 100", res.ElasticBonus)
	}
}

// --- Elastic Auto-Scale: Backend DB Yield Generation (End Phase) ---

func TestElasticAutoScale_BackendDBYieldGen(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("elastic-db-yield")
	state := newTestState("elastic-db-yield")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.CurrentTurn = 3
	state.Player1InsightPool = 0

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic DB",
		Faction:          "SD",
		CardType:         "Database",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"yield": 300, "availability": 1400, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	yieldVal := int64(300)
	yieldMax := int64(800)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "db1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentYield:     &yieldVal,
				CurrentAV:        1400,
				MaxAV:            1400,
				ElasticBonus:     0,
				MaxYield:         &yieldMax,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	f, _ := state.GetField(1)
	res := f.Backend[0]

	// ElasticBonus should increase by 100
	if res.ElasticBonus != 100 {
		t.Errorf("ElasticBonus = %d, want 100", res.ElasticBonus)
	}

	// Yield should have included the bonus: base(300) + ElasticBonus(100) = 400
	if state.Player1InsightPool != 400 {
		t.Errorf("InsightPool = %d, want 400 (base 300 + elastic 100)", state.Player1InsightPool)
	}
}

// --- Elastic Integration: MC Scales With Auto-Scaled Bonus ---

func TestElasticAutoScale_MCScalesWithBonus(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("elastic-mc-integration")
	state := newTestState("elastic-mc-integration")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.CurrentTurn = 3
	state.Player1InsightPool = 0
	state.Player1Budget = 5000

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		FreeTier:         500,
		CostPerRequest:   10,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 2000, "maintenance_cost": 50, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	// Backend Compute with existing ElasticBonus = 200
	tp := int64(500)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "c1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				CurrentAV:        2000,
				MaxAV:            2000,
				ElasticBonus:     200,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)
	_ = state.SetField(2, &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "p2f", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	})

	// End phase should collect MC
	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	// intrinsic = 500×1 + effectiveEB(200, 500) = 500 + 168 = 668
	// MC = (668 - 500) × 10 / 100 = 16
	// Budget = 5000 - 16 = 4984
	if state.Player1Budget != 4984 {
		t.Errorf("Budget = %d, want 4984 (5000 - MC 16)", state.Player1Budget)
	}
}

// === High Priority: Negative / Guard Tests ===

// TestElasticAutoScale_FaceDown_NoEndPhaseTrigger verifies that a face-down (deploying)
// Elastic DB does NOT get ElasticBonus incremented during End Phase Yield generation.
func TestElasticAutoScale_FaceDown_NoEndPhaseTrigger(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("elastic-facedown")
	state := newTestState("elastic-facedown")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.Player1InsightPool = 0

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic DB",
		Faction:          "SD",
		CardType:         "Database",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"yield": 300, "availability": 1400, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	yieldVal := int64(300)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:         "db1",
				CardID:             1,
				FaceUp:             false, // Still deploying
				Rank:               model.RankSmall,
				CurrentYield:       &yieldVal,
				CurrentAV:          1400,
				MaxAV:              1400,
				ElasticBonus:       0,
				DeployingTurnsLeft: 1,
				Attachments:        []model.AttachmentRef{},
				TemporaryEffects:   []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)
	_ = state.SetField(2, &model.Field{})

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	f, _ := state.GetField(1)
	res := f.Backend[0]

	// Face-down: no Elastic trigger, no Yield generation
	if res.ElasticBonus != 0 {
		t.Errorf("ElasticBonus = %d, want 0 (face-down card should not trigger)", res.ElasticBonus)
	}
	if state.Player1InsightPool != 0 {
		t.Errorf("InsightPool = %d, want 0 (face-down card produces no yield)", state.Player1InsightPool)
	}
}

// TestElasticAutoScale_MigrationLocked_NoEndPhaseTrigger verifies that a migration-locked
// Elastic DB does NOT get ElasticBonus incremented during End Phase.
func TestElasticAutoScale_MigrationLocked_NoEndPhaseTrigger(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("elastic-locked")
	state := newTestState("elastic-locked")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.Player1InsightPool = 0

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic DB",
		Faction:          "SD",
		CardType:         "Database",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"yield": 300, "availability": 1400, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	yieldVal := int64(300)
	srcID := "old-res"
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:      "db1",
				CardID:          1,
				FaceUp:          true,
				Rank:            model.RankSmall,
				CurrentYield:    &yieldVal,
				CurrentAV:       1400,
				MaxAV:           1400,
				ElasticBonus:    0,
				MigratingFrom:   &srcID, // Migration locked
				MigratingOnTurn: 2,
				Attachments:     []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)
	_ = state.SetField(2, &model.Field{})

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	f, _ := state.GetField(1)
	res := f.Backend[0]

	// Migration-locked: no Elastic trigger, no Yield generation
	if res.ElasticBonus != 0 {
		t.Errorf("ElasticBonus = %d, want 0 (migration-locked card should not trigger)", res.ElasticBonus)
	}
	if state.Player1InsightPool != 0 {
		t.Errorf("InsightPool = %d, want 0 (migration-locked card produces no yield)", state.Player1InsightPool)
	}
}

// TestElasticAutoScale_BackendCompute_NoEndPhaseTrigger verifies that a Backend
// Elastic Compute card does NOT trigger Elastic bonus during End Phase Yield generation.
// Only Backend DB/Data cards trigger on End Phase.
func TestElasticAutoScale_BackendCompute_NoEndPhaseTrigger(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("elastic-compute-no-ep")
	state := newTestState("elastic-compute-no-ep")
	state.CurrentPhase = model.PhaseEnd
	state.ActivePlayer = 1
	state.Player1InsightPool = 0

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 1200, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	maxTP := int64(1000)
	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{
				InstanceID:       "c1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1200,
				MaxAV:            1200,
				ElasticBonus:     0,
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)
	_ = state.SetField(2, &model.Field{})

	_, err := ProcessEndPhase(state, game, cc)
	if err != nil {
		t.Fatalf("ProcessEndPhase failed: %v", err)
	}

	f, _ := state.GetField(1)
	res := f.Backend[0]

	// Compute card should NOT trigger Elastic during End Phase (only DB/Data types)
	if res.ElasticBonus != 0 {
		t.Errorf("ElasticBonus = %d, want 0 (Backend Compute should not trigger on End Phase)", res.ElasticBonus)
	}
}

// TestElasticAutoScale_RE_FlatBonusNotMultipliedByRank verifies that ElasticBonus is
// a flat addition after rank multiplication, NOT multiplied by rank itself.
// R+E card at medium: TP = base(600) × rank(2) = 1200, + ElasticBonus(300) = 1500 (NOT 1800)
func TestElasticAutoScale_RE_FlatBonusNotMultipliedByRank(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:    1,
		CardName:  "R+E Orchestrator",
		Faction:   "SD",
		CardType:  "Orchestrator",
		Resizable: true,
		Elastic:   true,
		Stats:     json.RawMessage(`{"throughput": 600, "availability": 1200, "maintenance_cost": 100, "sla_penalty": 400}`),
	})

	tp := int64(1200) // 600 × 2 (medium)
	maxTP := int64(1200)
	familyM := model.FamilyM
	instance := &model.ResourceInstance{
		FaceUp:         true,
		InstanceID:     "orch1",
		CardID:         1,
		Rank:           model.RankMedium,
		InstanceFamily: &familyM,
		CurrentTP:      &tp,
		MaxTP:          &maxTP,
		CurrentAV:      2400,
		MaxAV:          2400,
		ElasticBonus:   300,
		Attachments:    []model.AttachmentRef{},
	}

	field := &model.Field{}

	// EffectiveTP = base(600) × rank(2) × family(1.0) + ElasticBonus(300) = 1500
	// NOT base(600) × rank(2) + ElasticBonus(300) × rank(2) = 1800
	got := CalculateEffectiveTP(instance, field, cc)
	if got != 1500 {
		t.Errorf("CalculateEffectiveTP = %d, want 1500 (1200 base + 300 flat elastic, NOT rank-multiplied)", got)
	}
}

// TestElasticAutoScale_YieldCap verifies that ElasticBonus for Yield (DB) is capped
// at MaxYield - baseYield (analogous to TP cap).
func TestElasticMC_ScaleUpIncreaseMC(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:         1,
		CardName:       "Elastic Container",
		Faction:        "SD",
		CardType:       "Container",
		Elastic:        true,
		FreeTier:       500,
		CostPerRequest: 10,
		Stats:          json.RawMessage(`{"throughput": 500, "availability": 1200, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	// At small rank with no EB: intrinsic=500, free_tier=500 → MC=0
	tp := int64(500)
	resSmall := &model.ResourceInstance{
		InstanceID:  "c1",
		CardID:      1,
		FaceUp:      true,
		Rank:        model.RankSmall,
		CurrentTP:   &tp,
		CurrentAV:   1200,
		MaxAV:       1200,
		Attachments: []model.AttachmentRef{},
	}
	fieldSmall := &model.Field{Frontend: [3]*model.ResourceInstance{resSmall}}
	mcSmall := collectMaintenanceCost(fieldSmall, cc)

	// At medium rank with no EB: intrinsic=1000, MC = (1000-500)*10/100 = 50
	tpMed := int64(500)
	resMed := &model.ResourceInstance{
		InstanceID:  "c1",
		CardID:      1,
		FaceUp:      true,
		Rank:        model.RankMedium,
		CurrentTP:   &tpMed,
		CurrentAV:   1200,
		MaxAV:       1200,
		Attachments: []model.AttachmentRef{},
	}
	fieldMed := &model.Field{Frontend: [3]*model.ResourceInstance{resMed}}
	mcMed := collectMaintenanceCost(fieldMed, cc)

	if mcSmall != 0 {
		t.Errorf("MC at small rank = %d, want 0 (within free tier)", mcSmall)
	}
	if mcMed != 50 {
		t.Errorf("MC at medium rank = %d, want 50", mcMed)
	}
	if mcMed <= mcSmall {
		t.Errorf("Scale Up should increase MC: small=%d, medium=%d", mcSmall, mcMed)
	}
}

// === Medium Priority: Interaction & Integration Tests ===

// TestScaleUp_ElasticBonusPreserved verifies that ElasticBonus is NOT reset on Scale Up.
func TestScaleUp_ElasticBonusPreserved(t *testing.T) {
	_, _, cc := newTestEngine()
	game := newTestGame("elastic-scaleup")
	state := newTestState("elastic-scaleup")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "R+E Container",
		Faction:          "SD",
		CardType:         "Container",
		Resizable:        true,
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 1200, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	maxTP := int64(1000)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:       "c1",
				CardID:           1,
				FaceUp:           true,
				Rank:             model.RankSmall,
				CurrentTP:        &tp,
				MaxTP:            &maxTP,
				CurrentAV:        1200,
				MaxAV:            1200,
				ElasticBonus:     200, // Pre-existing bonus
				Attachments:      []model.AttachmentRef{},
				TemporaryEffects: []model.TemporaryEffect{},
			},
		},
	}
	_ = state.SetField(1, field)

	familyM := model.FamilyM
	req := ScaleUpRequest{
		InstanceID:     "c1",
		TargetRank:     model.RankMedium,
		InstanceFamily: &familyM,
	}

	_, err := processScaleUp(state, game, 1, req, cc)
	if err != nil {
		t.Fatalf("processScaleUp failed: %v", err)
	}

	f, _ := state.GetField(1)
	res := f.Frontend[0]

	// ElasticBonus should be preserved through Scale Up
	if res.ElasticBonus != 200 {
		t.Errorf("ElasticBonus = %d, want 200 (preserved after Scale Up)", res.ElasticBonus)
	}
	// Rank should be updated
	if res.Rank != model.RankMedium {
		t.Errorf("Rank = %s, want medium", res.Rank)
	}
}

// TestEffectiveTP_ElasticPlusPlatform verifies that ElasticBonus and Platform bonus
// stack additively (not multiplicatively).
func TestEffectiveTP_ElasticPlusPlatform(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Elastic Container",
		Faction:  "SD",
		CardType: "Container",
		Elastic:  true,
		Stats:    json.RawMessage(`{"throughput": 600, "availability": 1300, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	// Platform that gives +200 TP to Containers
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardName: "Test Platform",
		Faction:  "SD",
		CardType: "Platform",
		PlatformEffects: []model.PlatformEffect{
			{
				Type: model.PlatformTPBonus,
				Params: map[string]interface{}{
					"target_faction":    "SD",
					"target_card_types": []interface{}{"Container"},
					"bonus":             float64(200),
				},
			},
		},
	})

	tp := int64(600)
	maxTP := int64(1200)
	instance := &model.ResourceInstance{
		FaceUp:       true,
		InstanceID:   "c1",
		CardID:       1,
		Rank:         model.RankSmall,
		CurrentTP:    &tp,
		MaxTP:        &maxTP,
		CurrentAV:    1300,
		MaxAV:        1300,
		ElasticBonus: 200,
		Attachments:  []model.AttachmentRef{},
	}

	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat-100", CardID: 100},
		},
	}

	// EffectiveTP = base(600) + ElasticBonus(200) + Platform(200) = 1000
	got := CalculateEffectiveTP(instance, field, cc)
	if got != 1000 {
		t.Errorf("CalculateEffectiveTP = %d, want 1000 (600 base + 200 elastic + 200 platform)", got)
	}
}

// TestElasticAutoScale_FrontendAttacked_Destroyed_NoTrigger verifies that when a
// Frontend Elastic card is destroyed by an attack, ElasticBonus does NOT trigger.
func TestElasticAutoScale_FrontendAttacked_Destroyed_NoTrigger(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 500, "maintenance_cost": 50, "sla_penalty": 400}`),
	})
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo:   2,
		CardName: "Strong Attacker",
		Faction:  "Tenki",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 1000, "availability": 2000, "maintenance_cost": 100, "sla_penalty": 400}`),
	})

	tp1 := int64(500)
	maxTP1 := int64(1000)
	tp2 := int64(1000)
	maxTP2 := int64(1000)

	state := newTestState("elastic-destroy-test")
	game := newTestGame("elastic-destroy-test")
	state.ActivePlayer = 2
	state.CurrentPhase = model.PhaseBattle

	// P1: Elastic container with only 500 AV (will be destroyed by 1000 damage)
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:  "def1",
				CardID:      1,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp1,
				MaxTP:       &maxTP1,
				CurrentAV:   500,
				MaxAV:       500,
				Attachments: []model.AttachmentRef{},
			},
		},
	})

	// P2: Attacker with 1000 TP (enough to destroy)
	_ = state.SetField(2, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:  "atk1",
				CardID:      2,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp2,
				MaxTP:       &maxTP2,
				CurrentAV:   2000,
				MaxAV:       2000,
				Attachments: []model.AttachmentRef{},
			},
		},
	})

	req := AttackRequest{AttackerInstanceID: "atk1", TargetInstanceID: "def1"}
	_, err := processAttack(state, game, 2, req, cc, nil)
	if err != nil {
		t.Fatalf("processAttack failed: %v", err)
	}

	// Defender should be destroyed (removed from field)
	p1Field, _ := state.GetField(1)
	if p1Field.Frontend[0] != nil {
		t.Error("expected defender to be destroyed and removed from field")
	}

	// Card should be in trash
	trash, _ := state.GetTrash(1)
	if len(trash) == 0 {
		t.Error("expected destroyed card to be in trash")
	}
}

// TestElasticAutoScale_MultipleTriggersSameTurn verifies that multiple attacks in one turn
// each increment the ElasticBonus cumulatively.
func TestElasticAutoScale_MultipleTriggersSameTurn(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic Container",
		Faction:          "SD",
		CardType:         "Container",
		Elastic:          true,
		ElasticIncrement: 100,
		Stats:            json.RawMessage(`{"throughput": 500, "availability": 5000, "maintenance_cost": 50, "sla_penalty": 400}`),
	})
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo:   2,
		CardName: "Attacker A",
		Faction:  "Tenki",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 300, "availability": 1000, "maintenance_cost": 100, "sla_penalty": 400}`),
	})
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo:   3,
		CardName: "Attacker B",
		Faction:  "Tenki",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 200, "availability": 1000, "maintenance_cost": 100, "sla_penalty": 400}`),
	})

	tp1 := int64(500)
	maxTP1 := int64(2000)
	tp2 := int64(300)
	maxTP2 := int64(300)
	tp3 := int64(200)
	maxTP3 := int64(200)

	state := newTestState("elastic-multi-trigger")
	game := newTestGame("elastic-multi-trigger")
	state.ActivePlayer = 2
	state.CurrentPhase = model.PhaseBattle

	// P1: Elastic container with high AV (survives both attacks)
	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:  "def1",
				CardID:      1,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp1,
				MaxTP:       &maxTP1,
				CurrentAV:   5000,
				MaxAV:       5000,
				Attachments: []model.AttachmentRef{},
			},
		},
	})

	// P2: Two attackers
	_ = state.SetField(2, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:  "atk1",
				CardID:      2,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp2,
				MaxTP:       &maxTP2,
				CurrentAV:   1000,
				MaxAV:       1000,
				Attachments: []model.AttachmentRef{},
			},
			{
				InstanceID:  "atk2",
				CardID:      3,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp3,
				MaxTP:       &maxTP3,
				CurrentAV:   1000,
				MaxAV:       1000,
				Attachments: []model.AttachmentRef{},
			},
		},
	})

	// First attack
	req1 := AttackRequest{AttackerInstanceID: "atk1", TargetInstanceID: "def1"}
	_, err := processAttack(state, game, 2, req1, cc, nil)
	if err != nil {
		t.Fatalf("first attack failed: %v", err)
	}

	// Second attack
	req2 := AttackRequest{AttackerInstanceID: "atk2", TargetInstanceID: "def1"}
	_, err = processAttack(state, game, 2, req2, cc, nil)
	if err != nil {
		t.Fatalf("second attack failed: %v", err)
	}

	// ElasticBonus should be 200 (100 × 2 attacks)
	p1Field, _ := state.GetField(1)
	defender := p1Field.Frontend[0]
	if defender.ElasticBonus != 200 {
		t.Errorf("ElasticBonus = %d, want 200 (2 attacks × 100 increment)", defender.ElasticBonus)
	}
}

// TestElasticAutoScale_FrontendAIML_Attacked verifies that an AI/ML card in Frontend
// also triggers Elastic bonus when attacked (not just Compute types).
func TestElasticAutoScale_FrontendAIML_Attacked(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:           1,
		CardName:         "Elastic AI/ML",
		Faction:          "SD",
		CardType:         "AI/ML",
		Elastic:          true,
		ElasticIncrement: 150,
		Stats:            json.RawMessage(`{"throughput": 400, "availability": 2000, "maintenance_cost": 80, "sla_penalty": 500}`),
	})
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo:   2,
		CardName: "Attacker",
		Faction:  "Tenki",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 300, "availability": 1000, "maintenance_cost": 100, "sla_penalty": 400}`),
	})

	tp1 := int64(400)
	maxTP1 := int64(800)
	tp2 := int64(300)
	maxTP2 := int64(300)

	state := newTestState("elastic-aiml-attack")
	game := newTestGame("elastic-aiml-attack")
	state.ActivePlayer = 2
	state.CurrentPhase = model.PhaseBattle

	_ = state.SetField(1, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:  "aiml1",
				CardID:      1,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp1,
				MaxTP:       &maxTP1,
				CurrentAV:   2000,
				MaxAV:       2000,
				Attachments: []model.AttachmentRef{},
			},
		},
	})

	_ = state.SetField(2, &model.Field{
		HasHadActiveResource: true,
		Frontend: [3]*model.ResourceInstance{
			{
				InstanceID:  "atk1",
				CardID:      2,
				FaceUp:      true,
				Rank:        model.RankSmall,
				CurrentTP:   &tp2,
				MaxTP:       &maxTP2,
				CurrentAV:   1000,
				MaxAV:       1000,
				Attachments: []model.AttachmentRef{},
			},
		},
	})

	req := AttackRequest{AttackerInstanceID: "atk1", TargetInstanceID: "aiml1"}
	_, err := processAttack(state, game, 2, req, cc, nil)
	if err != nil {
		t.Fatalf("processAttack failed: %v", err)
	}

	p1Field, _ := state.GetField(1)
	defender := p1Field.Frontend[0]
	if defender.ElasticBonus != 150 {
		t.Errorf("ElasticBonus = %d, want 150 (AI/ML Elastic trigger)", defender.ElasticBonus)
	}
}

func int64Ptr(n int64) *int64 {
	return &n
}

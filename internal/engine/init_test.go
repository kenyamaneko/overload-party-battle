package engine

import (
	"encoding/json"
	"testing"

	"github.com/kenyamaneko/overload-party-common/model"
)

func TestCreateResourceInstance_Compute(t *testing.T) {
	card := &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	}

	instance, err := model.CreateResourceInstance(card, "test-id")
	if err != nil {
		t.Fatalf("createResourceInstance failed: %v", err)
	}

	if instance.InstanceID != "test-id" {
		t.Errorf("instanceID = %s, want test-id", instance.InstanceID)
	}
	if instance.CardID != 1 {
		t.Errorf("cardID = %d, want 1", instance.CardID)
	}
	if instance.Rank != model.RankSmall {
		t.Errorf("rank = %s, want %s", instance.Rank, model.RankSmall)
	}
	if instance.CurrentAV != 1400 {
		t.Errorf("currentAV = %d, want 1400", instance.CurrentAV)
	}
	if instance.MaxAV != 1400 {
		t.Errorf("maxAV = %d, want 1400", instance.MaxAV)
	}
	if instance.CurrentTP == nil || *instance.CurrentTP != 700 {
		t.Error("currentTP should be 700")
	}
	if instance.MaxTP == nil || *instance.MaxTP != 700 {
		t.Error("maxTP should be 700")
	}
	if instance.CurrentYield != nil {
		t.Error("compute card should not have yield")
	}
}

func TestCreateResourceInstance_Database(t *testing.T) {
	card := &model.CardDefinition{
		CardNo:   3,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1000, "deploy_cost": 400, "sla_penalty": 300}`),
	}

	instance, err := model.CreateResourceInstance(card, "db-id")
	if err != nil {
		t.Fatalf("createResourceInstance failed: %v", err)
	}

	if instance.CurrentAV != 1000 {
		t.Errorf("currentAV = %d, want 1000", instance.CurrentAV)
	}
	if instance.CurrentYield == nil || *instance.CurrentYield != 500 {
		t.Error("currentYield should be 500")
	}
	if instance.MaxYield == nil || *instance.MaxYield != 500 {
		t.Error("maxYield should be 500")
	}
	if instance.CurrentTP != nil {
		t.Error("database card should not have TP")
	}
}

func TestCreateNewGame(t *testing.T) {
	e, repo, _ := newTestEngine()

	// Build 30-card deck snapshots
	cards := make([]int64, 30)
	for i := range cards {
		if i%2 == 0 {
			cards[i] = 1
		} else {
			cards[i] = 3
		}
	}
	deck1 := model.DeckSnapshot{DeckID: "deck1", Cards: cards}
	deck2 := model.DeckSnapshot{DeckID: "deck2", Cards: cards}

	gameID, err := e.CreateNewGame(t.Context(), "player1", "player2", deck1, deck2, 1)
	if err != nil {
		t.Fatalf("CreateNewGame failed: %v", err)
	}

	// Retrieve created state from mock repo
	state := repo.MustGetState(gameID)

	// Both players should have 5-card hands
	hand1, _ := state.GetHand(1)
	if len(hand1) != model.InitialHand {
		t.Errorf("player 1 hand size = %d, want %d", len(hand1), model.InitialHand)
	}
	hand2, _ := state.GetHand(2)
	if len(hand2) != model.InitialHand {
		t.Errorf("player 2 hand size = %d, want %d", len(hand2), model.InitialHand)
	}

	// Repository should have 30 - 5 (hand) = 25 cards
	repo1, _ := state.GetRepository(1)
	if len(repo1) != 25 {
		t.Errorf("player 1 repo size = %d, want 25", len(repo1))
	}
	repo2, _ := state.GetRepository(2)
	if len(repo2) != 25 {
		t.Errorf("player 2 repo size = %d, want 25", len(repo2))
	}

	// Fields should be empty (no resources deployed)
	field1, _ := state.GetField(1)
	for i, r := range field1.Frontend {
		if r != nil {
			t.Errorf("player 1 frontend[%d] should be nil", i)
		}
	}
	for i, r := range field1.Backend {
		if r != nil {
			t.Errorf("player 1 backend[%d] should be nil", i)
		}
	}
	field2, _ := state.GetField(2)
	for i, r := range field2.Frontend {
		if r != nil {
			t.Errorf("player 2 frontend[%d] should be nil", i)
		}
	}
	for i, r := range field2.Backend {
		if r != nil {
			t.Errorf("player 2 backend[%d] should be nil", i)
		}
	}

	// Phase should be draw, Turn should be 1
	if state.CurrentPhase != model.PhaseDraw {
		t.Errorf("phase = %s, want %s", state.CurrentPhase, model.PhaseDraw)
	}
	if state.CurrentTurn != 1 {
		t.Errorf("turn = %d, want 1", state.CurrentTurn)
	}
}

package engine

import (
	"encoding/json"
	"testing"

	"github.com/kenyamaneko/overload-party-common/model"
)

func newChainTestState() *model.GameState {
	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{})
	emptyTrash, _ := json.Marshal([]int64{})
	emptyChain, _ := json.Marshal([]model.ChainEntry{})

	return &model.GameState{
		GameID:            "chain-test",
		Version:           1,
		CurrentTurn:       1,
		CurrentPhase:      model.PhaseBattle,
		ActivePlayer:      1,
		Player1Budget:     5000,
		Player1Field:      emptyField,
		Player1Hand:       emptyHand,
		Player1Repository: emptyRepo,
		Player1Trash:      emptyTrash,
		Player1TimeBank:   480,
		Player2Budget:     5000,
		Player2Field:      emptyField,
		Player2Hand:       emptyHand,
		Player2Repository: emptyRepo,
		Player2Trash:      emptyTrash,
		Player2TimeBank:   480,
		ChainStack:        emptyChain,
	}
}

func TestPushToChain_Basic(t *testing.T) {
	state := newChainTestState()

	entry := model.ChainEntry{
		ActionType:       "attack",
		SourcePlayerID:   "player1",
		SourceInstanceID: "atk1",
		TargetInstanceID: "def1",
	}

	if err := PushToChain(state, entry); err != nil {
		t.Fatalf("PushToChain failed: %v", err)
	}

	stack, err := state.GetChainStack()
	if err != nil {
		t.Fatalf("GetChainStack failed: %v", err)
	}

	if len(stack) != 1 {
		t.Fatalf("chain length = %d, want 1", len(stack))
	}
	if stack[0].ChainLevel != 1 {
		t.Errorf("chain level = %d, want 1", stack[0].ChainLevel)
	}
}

func TestPushToChain_MaxLevel(t *testing.T) {
	state := newChainTestState()

	// Fill chain to max level
	for i := 0; i < MaxChainLevel; i++ {
		entry := model.ChainEntry{
			ActionType:       "component_effect",
			SourcePlayerID:   "player1",
			SourceInstanceID: "src",
		}
		if err := PushToChain(state, entry); err != nil {
			t.Fatalf("PushToChain[%d] failed: %v", i, err)
		}
	}

	// Next push should fail
	entry := model.ChainEntry{
		ActionType:     "component_effect",
		SourcePlayerID: "player1",
	}
	if err := PushToChain(state, entry); err == nil {
		t.Fatal("expected error for exceeding max chain level")
	}
}

func TestPushToChain_ReactiveCannotChainOnReactive(t *testing.T) {
	state := newChainTestState()

	// Push an attack
	_ = PushToChain(state, model.ChainEntry{
		ActionType:     "attack",
		SourcePlayerID: "player1",
	})

	// Push a reactive
	_ = PushToChain(state, model.ChainEntry{
		ActionType:     "reactive",
		SourcePlayerID: "player2",
	})

	// Another reactive should fail
	err := PushToChain(state, model.ChainEntry{
		ActionType:     "reactive",
		SourcePlayerID: "player1",
	})
	if err == nil {
		t.Fatal("expected error for reactive chaining on reactive")
	}
}

func TestCanChainReactive_EmptyChain(t *testing.T) {
	state := newChainTestState()

	can, err := CanChainReactive(state)
	if err != nil {
		t.Fatalf("CanChainReactive failed: %v", err)
	}
	if can {
		t.Error("should not be able to chain reactive on empty chain")
	}
}

func TestCanChainReactive_AfterAttack(t *testing.T) {
	state := newChainTestState()

	_ = PushToChain(state, model.ChainEntry{
		ActionType:     "attack",
		SourcePlayerID: "player1",
	})

	can, err := CanChainReactive(state)
	if err != nil {
		t.Fatalf("CanChainReactive failed: %v", err)
	}
	if !can {
		t.Error("should be able to chain reactive after attack")
	}
}

func TestCanChainReactive_AfterReactive(t *testing.T) {
	state := newChainTestState()

	_ = PushToChain(state, model.ChainEntry{
		ActionType:     "attack",
		SourcePlayerID: "player1",
	})
	_ = PushToChain(state, model.ChainEntry{
		ActionType:     "reactive",
		SourcePlayerID: "player2",
	})

	can, err := CanChainReactive(state)
	if err != nil {
		t.Fatalf("CanChainReactive failed: %v", err)
	}
	if can {
		t.Error("should not be able to chain reactive after reactive")
	}
}

func TestIsChainActive_EmptyChain(t *testing.T) {
	state := newChainTestState()

	if IsChainActive(state) {
		t.Error("should not be active with empty chain")
	}
}

func TestIsChainActive_WithUnresolved(t *testing.T) {
	state := newChainTestState()

	_ = PushToChain(state, model.ChainEntry{
		ActionType:     "attack",
		SourcePlayerID: "player1",
	})

	if !IsChainActive(state) {
		t.Error("should be active with unresolved entries")
	}
}

func TestIsChainActive_AllResolved(t *testing.T) {
	state := newChainTestState()

	// Manually inject a resolved chain
	resolved := []model.ChainEntry{
		{ChainLevel: 1, ActionType: "attack", Resolved: true},
	}
	data, _ := json.Marshal(resolved)
	state.ChainStack = data

	if IsChainActive(state) {
		t.Error("should not be active when all entries are resolved")
	}
}

func TestPushToChain_ChainLevelIncrementing(t *testing.T) {
	state := newChainTestState()

	_ = PushToChain(state, model.ChainEntry{ActionType: "attack", SourcePlayerID: "player1"})
	_ = PushToChain(state, model.ChainEntry{ActionType: "component_effect", SourcePlayerID: "player2"})

	stack, _ := state.GetChainStack()
	if len(stack) != 2 {
		t.Fatalf("chain length = %d, want 2", len(stack))
	}
	if stack[0].ChainLevel != 1 {
		t.Errorf("entry 0 chain level = %d, want 1", stack[0].ChainLevel)
	}
	if stack[1].ChainLevel != 2 {
		t.Errorf("entry 1 chain level = %d, want 2", stack[1].ChainLevel)
	}
}

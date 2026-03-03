package service

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

func newViewTestState(gameID string) *model.GameState {
	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{1, 2, 3})
	emptyTrash, _ := json.Marshal([]int64{})
	return &model.GameState{
		GameID:            gameID,
		Version:           1,
		CurrentTurn:       3,
		CurrentPhase:      model.PhaseMain,
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

func newViewTestGame(gameID string) *model.Game {
	return &model.Game{
		GameID:    gameID,
		Player1ID: "player1",
		Player2ID: "player2",
		Status:    model.GameStatusPlaying,
	}
}

func injectViewTestCards(cc *cache.CardCache) {
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo: 1, CardName: "Test Compute", Faction: "SD",
		CardType: "Compute", Resizable: true,
		Stats: json.RawMessage(`{"throughput":700,"availability":1400,"maintenance_cost":200,"deploy_cost":400,"sla_penalty":400}`),
	})
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo: 3, CardName: "Test Database", Faction: "Tenki",
		CardType: "Database", Resizable: true,
		Stats: json.RawMessage(`{"yield":200,"yield_max":600,"deploy_cost":300}`),
	})
	cc.InjectForTest(5, &model.CardDefinition{
		CardNo: 5, CardName: "Test Reactive", Faction: "SD",
		CardType: "Reactive",
		Stats:    json.RawMessage(`{"deploy_cost":0}`),
	})
	cc.InjectForTest(7, &model.CardDefinition{
		CardNo: 7, CardName: "Test Platform", Faction: "SD",
		CardType: "Platform",
		Stats:    json.RawMessage(`{"deploy_cost":0}`),
	})
}

func newViewEffectRegistry() *effect.EffectRegistry {
	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)
	return reg
}

// findViewAction returns the first AvailableAction matching the given type.
func findViewAction(actions []engine.AvailableAction, actionType string) *engine.AvailableAction {
	for i, a := range actions {
		if a.Type == actionType {
			return &actions[i]
		}
	}
	return nil
}

// findViewActions returns all AvailableActions matching the given type.
func findViewActions(actions []engine.AvailableAction, actionType string) []engine.AvailableAction {
	var result []engine.AvailableAction
	for _, a := range actions {
		if a.Type == actionType {
			result = append(result, a)
		}
	}
	return result
}

// findViewActionByCardID returns the first AvailableAction with the given card ID.
func findViewActionByCardID(actions []engine.AvailableAction, cardID int64) *engine.AvailableAction {
	for i, a := range actions {
		if a.CardID == cardID {
			return &actions[i]
		}
	}
	return nil
}

// ---------------------------------------------------------------------------
// Test 1: AvailableActions in MyView (active vs non-active player)
// ---------------------------------------------------------------------------

func TestBuildClientGameState_AvailableActionsInMyView(t *testing.T) {
	cc := cache.NewCardCache()
	injectViewTestCards(cc)
	reg := newViewEffectRegistry()

	state := newViewTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 5000

	// Put a Compute card (card 1) in Player 1's hand
	hand1 := []model.HandCard{{InstanceID: "h1", CardID: 1}}
	_ = state.SetHand(1, hand1)

	game := newViewTestGame("g1")

	// --- Player 1 (active player) ---
	cgs1, err := buildClientGameState(state, game, 1, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState for player 1 failed: %v", err)
	}

	if cgs1.MyView.AvailableActions == nil || len(cgs1.MyView.AvailableActions) == 0 {
		t.Fatal("expected Player 1 (active) to have available actions, got nil/empty")
	}

	// Find the play_card action
	playActions := findViewActions(cgs1.MyView.AvailableActions, model.ActionPlayCard)
	if len(playActions) == 0 {
		t.Fatal("expected at least one play_card action for Player 1")
	}

	action := findViewActionByCardID(playActions, 1)
	if action == nil {
		t.Fatal("expected play_card action for card ID 1")
	}

	// Compute cards can go to frontend and backend zones
	hasFrontend := false
	hasBackend := false
	for _, z := range action.ValidZones {
		if strings.HasPrefix(z, "frontend_") {
			hasFrontend = true
		}
		if strings.HasPrefix(z, "backend_") {
			hasBackend = true
		}
	}
	if !hasFrontend {
		t.Errorf("expected valid_zones to contain frontend_* entries, got %v", action.ValidZones)
	}
	if !hasBackend {
		t.Errorf("expected valid_zones to contain backend_* entries, got %v", action.ValidZones)
	}

	// --- Player 2 (not active) ---
	cgs2, err := buildClientGameState(state, game, 2, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState for player 2 failed: %v", err)
	}

	if len(cgs2.MyView.AvailableActions) != 0 {
		t.Errorf("expected Player 2 (inactive) to have NO available actions, got %d", len(cgs2.MyView.AvailableActions))
	}
}

// ---------------------------------------------------------------------------
// Test 2: Opponent hand is hidden (count only, no card details)
// ---------------------------------------------------------------------------

func TestBuildClientGameState_OpponentHandHidden(t *testing.T) {
	cc := cache.NewCardCache()
	injectViewTestCards(cc)
	reg := newViewEffectRegistry()

	state := newViewTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	// Player 1 has 3 hand cards
	hand1 := []model.HandCard{
		{InstanceID: "h1", CardID: 1},
		{InstanceID: "h2", CardID: 3},
		{InstanceID: "h3", CardID: 1},
	}
	_ = state.SetHand(1, hand1)

	// Player 2 has 2 hand cards
	hand2 := []model.HandCard{
		{InstanceID: "h4", CardID: 1},
		{InstanceID: "h5", CardID: 3},
	}
	_ = state.SetHand(2, hand2)

	game := newViewTestGame("g1")

	cgs, err := buildClientGameState(state, game, 1, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState failed: %v", err)
	}

	// MyView should show full hand
	if len(cgs.MyView.Hand) != 3 {
		t.Fatalf("expected MyView.Hand to have 3 cards, got %d", len(cgs.MyView.Hand))
	}
	for i, hc := range cgs.MyView.Hand {
		if hc.CardID == 0 {
			t.Errorf("expected MyView.Hand[%d].CardID to be visible (non-zero)", i)
		}
	}

	// OppView should only show hand count
	if cgs.OppView.HandCount != 2 {
		t.Errorf("expected OppView.HandCount = 2, got %d", cgs.OppView.HandCount)
	}

	// OpponentView struct does not have a Hand field — HandCount is the only info.
	// Verify via JSON that "hand" key does not appear in opponent.
	data, _ := json.Marshal(cgs.OppView)
	var oppMap map[string]interface{}
	_ = json.Unmarshal(data, &oppMap)

	if _, exists := oppMap["hand"]; exists {
		t.Error("opponent JSON should NOT have a 'hand' key, only 'handCount'")
	}
	if _, exists := oppMap["handCount"]; !exists {
		t.Error("opponent JSON should have a 'handCount' key")
	}
}

// ---------------------------------------------------------------------------
// Test 3: Opponent support zone hides face-down reactive cards
// ---------------------------------------------------------------------------

func TestBuildClientGameState_OpponentSupportHidden(t *testing.T) {
	cc := cache.NewCardCache()
	injectViewTestCards(cc)
	reg := newViewEffectRegistry()

	state := newViewTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	// Set up opponent (Player 2) field with:
	// - support[0]: face-down reactive card
	// - support[1]: face-up platform card
	oppField := &model.Field{}
	oppField.Support[0] = &model.SupportInstance{
		InstanceID: "s1",
		CardID:     5, // Reactive
		FaceDown:   true,
	}
	oppField.Support[1] = &model.SupportInstance{
		InstanceID: "s2",
		CardID:     7, // Platform
		FaceDown:   false,
	}
	_ = state.SetField(2, oppField)

	game := newViewTestGame("g1")

	cgs, err := buildClientGameState(state, game, 1, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState failed: %v", err)
	}

	// Face-down reactive: CardID should be hidden (nil), FaceDown should be true
	hidden0 := cgs.OppView.Field.Support[0]
	if hidden0 == nil {
		t.Fatal("expected OppView.Field.Support[0] to be non-nil")
	}
	if !hidden0.FaceDown {
		t.Error("expected face-down reactive to have FaceDown == true")
	}
	if hidden0.CardID != nil {
		t.Errorf("expected face-down reactive CardID to be nil, got %d", *hidden0.CardID)
	}

	// Face-up platform: CardID should be visible (non-nil)
	hidden1 := cgs.OppView.Field.Support[1]
	if hidden1 == nil {
		t.Fatal("expected OppView.Field.Support[1] to be non-nil")
	}
	if hidden1.FaceDown {
		t.Error("expected face-up platform to have FaceDown == false")
	}
	if hidden1.CardID == nil {
		t.Error("expected face-up platform CardID to be non-nil")
	} else if *hidden1.CardID != 7 {
		t.Errorf("expected face-up platform CardID = 7, got %d", *hidden1.CardID)
	}
}

// ---------------------------------------------------------------------------
// Test 4: JSON shape validation
// ---------------------------------------------------------------------------

func TestBuildClientGameState_JSONShape(t *testing.T) {
	cc := cache.NewCardCache()
	injectViewTestCards(cc)
	reg := newViewEffectRegistry()

	state := newViewTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 5000

	// Give Player 1 a hand card so they have play_card actions
	hand := []model.HandCard{{InstanceID: "h1", CardID: 1}}
	_ = state.SetHand(1, hand)

	game := newViewTestGame("g1")

	// --- Active player (Player 1): should have available_actions ---
	cgs1, err := buildClientGameState(state, game, 1, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState failed: %v", err)
	}

	data1, err := json.Marshal(cgs1)
	if err != nil {
		t.Fatalf("json.Marshal failed: %v", err)
	}

	var m1 map[string]interface{}
	if err := json.Unmarshal(data1, &m1); err != nil {
		t.Fatalf("json.Unmarshal failed: %v", err)
	}

	// Top-level keys
	for _, key := range []string{"gameId", "currentTurn", "currentPhase", "activePlayer", "isMyTurn", "my", "opponent"} {
		if _, exists := m1[key]; !exists {
			t.Errorf("expected top-level key %q in JSON", key)
		}
	}

	// "my" should have "available_actions" when active
	myMap1, ok := m1["my"].(map[string]interface{})
	if !ok {
		t.Fatal("expected 'my' to be an object")
	}
	if _, exists := myMap1["available_actions"]; !exists {
		t.Error("expected 'my' to have 'available_actions' key when player is active")
	}

	// "opponent" should NOT have "hand" key (only "handCount")
	oppMap1, ok := m1["opponent"].(map[string]interface{})
	if !ok {
		t.Fatal("expected 'opponent' to be an object")
	}
	if _, exists := oppMap1["hand"]; exists {
		t.Error("expected 'opponent' to NOT have 'hand' key")
	}
	if _, exists := oppMap1["handCount"]; !exists {
		t.Error("expected 'opponent' to have 'handCount' key")
	}

	// --- Inactive player (Player 2): should NOT have available_actions ---
	cgs2, err := buildClientGameState(state, game, 2, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState for player 2 failed: %v", err)
	}

	data2, err := json.Marshal(cgs2)
	if err != nil {
		t.Fatalf("json.Marshal failed: %v", err)
	}

	var m2 map[string]interface{}
	if err := json.Unmarshal(data2, &m2); err != nil {
		t.Fatalf("json.Unmarshal failed: %v", err)
	}

	myMap2, ok := m2["my"].(map[string]interface{})
	if !ok {
		t.Fatal("expected 'my' to be an object")
	}
	if _, exists := myMap2["available_actions"]; exists {
		t.Error("expected 'my' to NOT have 'available_actions' key when player is inactive (omitempty)")
	}
}

// ---------------------------------------------------------------------------
// Test 6: ValidZones format (zone_index, not bare zone)
// ---------------------------------------------------------------------------

func TestBuildClientGameState_ValidZonesFormat(t *testing.T) {
	cc := cache.NewCardCache()
	injectViewTestCards(cc)
	reg := newViewEffectRegistry()

	state := newViewTestState("g1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 5000

	// Hand: Compute (card 1) and Database (card 3)
	hand := []model.HandCard{
		{InstanceID: "h1", CardID: 1},
		{InstanceID: "h2", CardID: 3},
	}
	_ = state.SetHand(1, hand)

	game := newViewTestGame("g1")

	cgs, err := buildClientGameState(state, game, 1, cc, reg)
	if err != nil {
		t.Fatalf("buildClientGameState failed: %v", err)
	}

	playActions := findViewActions(cgs.MyView.AvailableActions, model.ActionPlayCard)
	if len(playActions) < 2 {
		t.Fatalf("expected at least 2 play_card actions (Compute + Database), got %d", len(playActions))
	}

	// --- Compute card (card 1): eligible for frontend + backend ---
	computeAction := findViewActionByCardID(playActions, 1)
	if computeAction == nil {
		t.Fatal("expected play_card action for Compute card (id=1)")
	}

	hasFrontendSlot := false
	hasBackendSlot := false
	for _, z := range computeAction.ValidZones {
		// Should be "frontend_0", "frontend_1", etc. — not bare "frontend"
		if z == "frontend" || z == "backend" {
			t.Errorf("expected indexed zone format (e.g. 'frontend_0'), got bare %q", z)
		}
		if strings.HasPrefix(z, "frontend_") {
			hasFrontendSlot = true
		}
		if strings.HasPrefix(z, "backend_") {
			hasBackendSlot = true
		}
	}
	if !hasFrontendSlot {
		t.Errorf("Compute card should have frontend_* zones, got %v", computeAction.ValidZones)
	}
	if !hasBackendSlot {
		t.Errorf("Compute card should have backend_* zones, got %v", computeAction.ValidZones)
	}

	// --- Database card (card 3): backend only ---
	dbAction := findViewActionByCardID(playActions, 3)
	if dbAction == nil {
		t.Fatal("expected play_card action for Database card (id=3)")
	}

	for _, z := range dbAction.ValidZones {
		if z == "backend" {
			t.Errorf("expected indexed zone format (e.g. 'backend_0'), got bare %q", z)
		}
		if !strings.HasPrefix(z, "backend_") {
			t.Errorf("Database card should only have backend_* zones, got %q", z)
		}
	}
	if len(dbAction.ValidZones) == 0 {
		t.Error("Database card should have at least one backend_* zone")
	}
}

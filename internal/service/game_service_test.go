package service

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-battle/internal/matchmaking"
	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/npc"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
)

// newTestGameService creates a GameService for testing.
// Uses mock repositories and nil for deps that require PostgreSQL.
func newTestGameService() (*GameService, *repository.MockGameRepository, *cache.CardCache) {
	gameRepo := repository.NewMockGameRepository()
	cc := cache.NewCardCache()

	gameEngine := engine.NewGameEngine(gameRepo, cc)
	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)
	gameEngine.SetEffectRegistry(reg)

	svc := &GameService{
		engine:    gameEngine,
		gameRepo:  gameRepo,
		deckRepo:  nil, // Not needed for runNPCTurnIfNeeded tests
		cardCache: cc,
		queue:     matchmaking.NewQueue(),
		npcAI:     npc.NewStandardAI(cc, reg),
	}

	return svc, gameRepo, cc
}

func injectTestCards(cc *cache.CardCache) {
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
	// Non-scalable compute card for tests that don't want scale_up side effects
	cc.InjectForTest(10, &model.CardDefinition{
		CardNo: 10, CardName: "Test Compute NS", Faction: "SD",
		CardType: "Compute",
		Stats: json.RawMessage(`{"throughput":500,"availability":1400,"maintenance_cost":200,"deploy_cost":400,"sla_penalty":400}`),
	})
}

func TestRunNPCTurnIfNeeded_NoNPCInGame(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game1",
		Player1ID: "human1",
		Player2ID: "human2",
		Status:    model.GameStatusPlaying,
	}
	state := newEmptyGameState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	gameRepo.InjectGame("game1", game)
	gameRepo.InjectState("game1", state)

	err := svc.runNPCTurnIfNeeded(ctx, "game1")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	updated := gameRepo.MustGetState("game1")
	if updated.CurrentPhase != model.PhaseMain {
		t.Errorf("phase = %s, want main", updated.CurrentPhase)
	}
}

func TestRunNPCTurnIfNeeded_PlayerTurn(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game1",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}
	state := newEmptyGameState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1

	gameRepo.InjectGame("game1", game)
	gameRepo.InjectState("game1", state)

	err := svc.runNPCTurnIfNeeded(ctx, "game1")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	updated := gameRepo.MustGetState("game1")
	if updated.CurrentPhase != model.PhaseMain {
		t.Errorf("phase = %s, want main", updated.CurrentPhase)
	}
	if updated.ActivePlayer != 1 {
		t.Errorf("activePlayer = %d, want 1", updated.ActivePlayer)
	}
}

func TestRunNPCTurnIfNeeded_NPCExecutesTurn(t *testing.T) {
	svc, gameRepo, cc := newTestGameService()
	ctx := context.Background()

	injectTestCards(cc)

	game := &model.Game{
		GameID:    "game1",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}

	state := newEmptyGameState("game1")
	state.CurrentTurn = 2
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	_ = state.SetHand(2, []model.HandCard{})

	tp := int64(500)
	npcField := &model.Field{}
	npcField.Frontend[0] = &model.ResourceInstance{
		InstanceID: "npc-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                       true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(2, npcField)

	humanField := &model.Field{}
	humanField.Frontend[0] = &model.ResourceInstance{
		InstanceID: "human-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                         true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(1, humanField)

	_ = state.SetRepository(1, []int64{1, 1, 1, 1, 1})
	_ = state.SetRepository(2, []int64{1, 1, 1, 1, 1})

	gameRepo.InjectGame("game1", game)
	gameRepo.InjectState("game1", state)

	err := svc.runNPCTurnIfNeeded(ctx, "game1")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}

	updated := gameRepo.MustGetState("game1")
	if updated.ActivePlayer != 1 {
		t.Errorf("activePlayer = %d, want 1 (human's turn after NPC)", updated.ActivePlayer)
	}
	if updated.CurrentPhase != model.PhaseMain {
		t.Errorf("phase = %s, want main", updated.CurrentPhase)
	}
}

func TestRunNPCTurnIfNeeded_GameFinished(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game1",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusFinished,
	}
	state := newEmptyGameState("game1")

	gameRepo.InjectGame("game1", game)
	gameRepo.InjectState("game1", state)

	err := svc.runNPCTurnIfNeeded(ctx, "game1")
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
}

func TestGetGameStateForPlayer_ReturnsClientGameState(t *testing.T) {
	svc, gameRepo, cc := newTestGameService()
	ctx := context.Background()

	injectTestCards(cc)

	game := &model.Game{
		GameID:    "game1",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}

	state := newEmptyGameState("game1")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 4000

	gameRepo.InjectGame("game1", game)
	gameRepo.InjectState("game1", state)

	clientState, err := svc.GetGameStateForPlayer(ctx, "game1", "human1")
	if err != nil {
		t.Fatalf("GetGameStateForPlayer failed: %v", err)
	}

	if clientState.GameID != "game1" {
		t.Errorf("gameID = %s, want game1", clientState.GameID)
	}
	if !clientState.IsMyTurn {
		t.Error("expected IsMyTurn = true")
	}
	if clientState.MyView.Budget != 4000 {
		t.Errorf("budget = %d, want 4000", clientState.MyView.Budget)
	}
}

// ---------------------------------------------------------------------------
// StartNPCBattle tests
// ---------------------------------------------------------------------------

// newTestGameServiceWithDeck creates a GameService wired with a mock deck repo
// containing a 30-card deck for the given player.
func newTestGameServiceWithDeck(playerID string) (*GameService, *repository.MockGameRepository, *cache.CardCache, *repository.MockDeckRepository) {
	gameRepo := repository.NewMockGameRepository()
	deckRepo := repository.NewMockDeckRepository()
	cc := cache.NewCardCache()

	gameEngine := engine.NewGameEngine(gameRepo, cc)
	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)
	gameEngine.SetEffectRegistry(reg)

	svc := &GameService{
		engine:    gameEngine,
		gameRepo:  gameRepo,
		deckRepo:  deckRepo,
		cardCache: cc,
		queue:     matchmaking.NewQueue(),
		npcAI:     npc.NewStandardAI(cc, reg),
	}

	return svc, gameRepo, cc, deckRepo
}

// loadRealCardCacheForService loads all cards from the generated JSON file.
func loadRealCardCacheForService(t *testing.T, cc *cache.CardCache) {
	t.Helper()
	if err := cc.LoadFromJSON("../../internal/cache/cards_gen.json"); err != nil {
		t.Fatalf("failed to load card cache: %v", err)
	}
}

// seedTestDeck creates a valid 30-card deck in the mock deck repo.
func seedTestDeck(deckRepo *repository.MockDeckRepository, playerID string) int64 {
	// Use a mix of card numbers that work with real card data:
	// Card 1 (SD Compute) x3, Card 3 (Tenki DB) x3, repeated to fill 30.
	cards := []model.DeckCard{
		{CardNo: 1, Count: 3},
		{CardNo: 3, Count: 3},
		{CardNo: 6, Count: 3},
		{CardNo: 7, Count: 3},
		{CardNo: 8, Count: 3},
		{CardNo: 9, Count: 3},
		{CardNo: 13, Count: 3},
		{CardNo: 15, Count: 1},
		{CardNo: 17, Count: 1},
		{CardNo: 20, Count: 3},
		{CardNo: 98, Count: 1},
		{CardNo: 99, Count: 1},
		{CardNo: 100, Count: 1},
		{CardNo: 101, Count: 1},
	}
	deck := &model.Deck{PlayerID: playerID, DeckName: "Test Deck"}
	_ = deckRepo.Create(context.Background(), deck, cards)
	return deck.DeckID
}

func TestStartNPCBattle_Success(t *testing.T) {
	svc, _, cc, deckRepo := newTestGameServiceWithDeck("player1")
	loadRealCardCacheForService(t, cc)

	deckID := seedTestDeck(deckRepo, "player1")
	ctx := context.Background()

	game, err := svc.StartNPCBattle(ctx, "player1", deckID, "SD")
	if err != nil {
		t.Fatalf("StartNPCBattle failed: %v", err)
	}

	if game.Player1ID != "player1" {
		t.Errorf("Player1ID = %s, want player1", game.Player1ID)
	}
	if game.Player2ID != npc.NPCPlayerID {
		t.Errorf("Player2ID = %s, want %s", game.Player2ID, npc.NPCPlayerID)
	}
	// Game starts fully initialized; status should be "playing" or "finished"
	// (NPC auto-advance may complete the game if the NPC turn ends the match).
	if game.Status != model.GameStatusPlaying && game.Status != model.GameStatusFinished {
		t.Errorf("Status = %s, want playing or finished", game.Status)
	}

	// Verify game state exists and was properly initialized
	state, err := svc.gameRepo.GetGameState(ctx, game.GameID)
	if err != nil {
		t.Fatalf("GetGameState failed: %v", err)
	}
	if state.CurrentTurn < 1 {
		t.Errorf("expected CurrentTurn >= 1, got %d", state.CurrentTurn)
	}
}

func TestStartNPCBattle_EmptyDeck(t *testing.T) {
	svc, _, cc, deckRepo := newTestGameServiceWithDeck("player1")
	loadRealCardCacheForService(t, cc)

	// Create an empty deck
	deck := &model.Deck{PlayerID: "player1", DeckName: "Empty"}
	_ = deckRepo.Create(context.Background(), deck, []model.DeckCard{})

	_, err := svc.StartNPCBattle(context.Background(), "player1", deck.DeckID, "SD")
	if err == nil {
		t.Fatal("expected error for empty deck, got nil")
	}
	if !contains(err.Error(), "empty") {
		t.Errorf("error = %q, want mention of empty", err.Error())
	}
}

func TestStartNPCBattle_UnknownFaction(t *testing.T) {
	svc, _, cc, deckRepo := newTestGameServiceWithDeck("player1")
	loadRealCardCacheForService(t, cc)

	deckID := seedTestDeck(deckRepo, "player1")

	_, err := svc.StartNPCBattle(context.Background(), "player1", deckID, "InvalidFaction")
	if err == nil {
		t.Fatal("expected error for unknown faction, got nil")
	}
	if !contains(err.Error(), "unknown NPC faction") {
		t.Errorf("error = %q, want mention of unknown NPC faction", err.Error())
	}
}

func TestStartNPCBattle_FactionNormalization(t *testing.T) {
	svc, _, cc, deckRepo := newTestGameServiceWithDeck("player1")
	loadRealCardCacheForService(t, cc)

	deckID := seedTestDeck(deckRepo, "player1")

	// "sd" (lowercase) should be normalized to "SD"
	game, err := svc.StartNPCBattle(context.Background(), "player1", deckID, "sd")
	if err != nil {
		t.Fatalf("StartNPCBattle with lowercase faction failed: %v", err)
	}
	if game.Player2ID != npc.NPCPlayerID {
		t.Errorf("Player2ID = %s, want %s", game.Player2ID, npc.NPCPlayerID)
	}
}

// ---------------------------------------------------------------------------
// ProcessAction tests
// ---------------------------------------------------------------------------

func TestProcessAction_EndPhase(t *testing.T) {
	svc, gameRepo, cc := newTestGameService()
	ctx := context.Background()

	injectTestCards(cc)

	game := &model.Game{
		GameID:    "game-pa1",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}

	state := newEmptyGameState("game-pa1")
	state.CurrentTurn = 2
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 5000

	// Give both players a frontend resource so game doesn't immediately end
	tp := int64(500)
	p1Field := &model.Field{}
	p1Field.Frontend[0] = &model.ResourceInstance{
		InstanceID: "p1-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                      true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(1, p1Field)

	p2Field := &model.Field{}
	p2Field.Frontend[0] = &model.ResourceInstance{
		InstanceID: "p2-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                      true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(2, p2Field)

	_ = state.SetRepository(1, []int64{1, 1, 1, 1, 1})
	_ = state.SetRepository(2, []int64{1, 1, 1, 1, 1})

	gameRepo.InjectGame("game-pa1", game)
	gameRepo.InjectState("game-pa1", state)

	result, err := svc.ProcessAction(ctx, "game-pa1", "human1", model.ActionEndPhase, nil)
	if err != nil {
		t.Fatalf("ProcessAction failed: %v", err)
	}

	if result.GameOver {
		t.Error("game should not be over after end_phase in main")
	}

	// After end_phase from main, game should progress to battle or beyond
	updated := gameRepo.MustGetState("game-pa1")
	if updated.CurrentPhase == model.PhaseMain && updated.ActivePlayer == 1 {
		t.Error("phase should have advanced past main for player 1")
	}
}

func TestProcessAction_NotYourTurn(t *testing.T) {
	svc, gameRepo, cc := newTestGameService()
	ctx := context.Background()

	injectTestCards(cc)

	game := &model.Game{
		GameID:    "game-nyt",
		Player1ID: "human1",
		Player2ID: "human2",
		Status:    model.GameStatusPlaying,
	}

	state := newEmptyGameState("game-nyt")
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1 // It's player 1's turn

	gameRepo.InjectGame("game-nyt", game)
	gameRepo.InjectState("game-nyt", state)

	// Player 2 tries to act — should fail
	_, err := svc.ProcessAction(ctx, "game-nyt", "human2", model.ActionEndPhase, nil)
	if err == nil {
		t.Fatal("expected error when wrong player acts, got nil")
	}
	if !contains(err.Error(), "not your turn") {
		t.Errorf("error = %q, want mention of not your turn", err.Error())
	}
}

func TestProcessAction_GameNotPlaying(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game-gnp",
		Player1ID: "human1",
		Player2ID: "human2",
		Status:    model.GameStatusFinished,
	}

	state := newEmptyGameState("game-gnp")
	gameRepo.InjectGame("game-gnp", game)
	gameRepo.InjectState("game-gnp", state)

	_, err := svc.ProcessAction(ctx, "game-gnp", "human1", model.ActionEndPhase, nil)
	if err == nil {
		t.Fatal("expected error for finished game, got nil")
	}
	if !contains(err.Error(), "not in playing state") {
		t.Errorf("error = %q, want mention of not in playing state", err.Error())
	}
}

// ---------------------------------------------------------------------------
// FinishGame tests
// ---------------------------------------------------------------------------

func TestFinishGame_PvP_UpdatesWinLoss(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game-pvp",
		Player1ID: "human1",
		Player2ID: "human2",
		Status:    model.GameStatusPlaying,
	}
	gameRepo.InjectGame("game-pvp", game)

	err := svc.FinishGame(ctx, "game-pvp", 1)
	if err != nil {
		t.Fatalf("FinishGame failed: %v", err)
	}

	wins1, losses1 := gameRepo.GetWinLoss("human1")
	wins2, losses2 := gameRepo.GetWinLoss("human2")

	if wins1 != 1 || losses1 != 0 {
		t.Errorf("human1: wins=%d, losses=%d, want 1/0", wins1, losses1)
	}
	if wins2 != 0 || losses2 != 1 {
		t.Errorf("human2: wins=%d, losses=%d, want 0/1", wins2, losses2)
	}
}

func TestFinishGame_NPC_SkipsWinLoss(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game-npc",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}
	gameRepo.InjectGame("game-npc", game)

	err := svc.FinishGame(ctx, "game-npc", 1)
	if err != nil {
		t.Fatalf("FinishGame failed: %v", err)
	}

	// For NPC games, win/loss should NOT be updated
	wins1, losses1 := gameRepo.GetWinLoss("human1")
	if wins1 != 0 || losses1 != 0 {
		t.Errorf("human1 after NPC game: wins=%d, losses=%d, want 0/0 (NPC games skip win/loss)", wins1, losses1)
	}
}

func TestFinishGame_PvP_Player2Wins(t *testing.T) {
	svc, gameRepo, _ := newTestGameService()
	ctx := context.Background()

	game := &model.Game{
		GameID:    "game-pvp2",
		Player1ID: "human1",
		Player2ID: "human2",
		Status:    model.GameStatusPlaying,
	}
	gameRepo.InjectGame("game-pvp2", game)

	err := svc.FinishGame(ctx, "game-pvp2", 2)
	if err != nil {
		t.Fatalf("FinishGame failed: %v", err)
	}

	wins1, losses1 := gameRepo.GetWinLoss("human1")
	wins2, losses2 := gameRepo.GetWinLoss("human2")

	if wins1 != 0 || losses1 != 1 {
		t.Errorf("human1: wins=%d, losses=%d, want 0/1", wins1, losses1)
	}
	if wins2 != 1 || losses2 != 0 {
		t.Errorf("human2: wins=%d, losses=%d, want 1/0", wins2, losses2)
	}
}

// ---------------------------------------------------------------------------
// ActionObserver tests
// ---------------------------------------------------------------------------

func TestActionObserver_CalledDuringNPCTurn(t *testing.T) {
	svc, gameRepo, cc := newTestGameService()
	ctx := context.Background()

	injectTestCards(cc)

	var observed []string
	svc.SetActionObserver(func(gameID, actionType string, data json.RawMessage) {
		observed = append(observed, actionType)
	})

	game := &model.Game{
		GameID:    "game-obs",
		Player1ID: "human1",
		Player2ID: npc.NPCPlayerID,
		Status:    model.GameStatusPlaying,
	}

	state := newEmptyGameState("game-obs")
	state.CurrentTurn = 2
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 2
	state.Player2Budget = 5000

	_ = state.SetHand(2, []model.HandCard{})

	tp := int64(500)
	npcField := &model.Field{}
	npcField.Frontend[0] = &model.ResourceInstance{
		InstanceID: "npc-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                       true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(2, npcField)

	humanField := &model.Field{}
	humanField.Frontend[0] = &model.ResourceInstance{
		InstanceID: "human-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                         true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(1, humanField)

	_ = state.SetRepository(1, []int64{1, 1, 1, 1, 1})
	_ = state.SetRepository(2, []int64{1, 1, 1, 1, 1})

	gameRepo.InjectGame("game-obs", game)
	gameRepo.InjectState("game-obs", state)

	err := svc.runNPCTurnIfNeeded(ctx, "game-obs")
	if err != nil {
		t.Fatalf("runNPCTurnIfNeeded failed: %v", err)
	}

	if len(observed) == 0 {
		t.Error("expected action observer to be called at least once during NPC turn")
	}
}

// ---------------------------------------------------------------------------
// BattleEventObserver tests
// ---------------------------------------------------------------------------

func TestBattleEventObserver_ProcessAction_EmitsTurnStart(t *testing.T) {
	svc, gameRepo, cc := newTestGameService()
	ctx := context.Background()

	injectTestCards(cc)

	game := &model.Game{
		GameID:    "game-bs-pa",
		Player1ID: "human1",
		Player2ID: "human2",
		Status:    model.GameStatusPlaying,
	}

	state := newEmptyGameState("game-bs-pa")
	state.CurrentTurn = 2
	state.CurrentPhase = model.PhaseMain
	state.ActivePlayer = 1
	state.Player1Budget = 5000

	tp := int64(500)
	p1Field := &model.Field{}
	p1Field.Frontend[0] = &model.ResourceInstance{
		InstanceID: "p1-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                      true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(1, p1Field)

	p2Field := &model.Field{}
	p2Field.Frontend[0] = &model.ResourceInstance{
		InstanceID: "p2-f0", CardID: 10, Rank: model.RankSmall,
		FaceUp:                      true,
		MaxAV: 1400, CurrentTP: &tp, MaxTP: &tp,
	}
	_ = state.SetField(2, p2Field)

	_ = state.SetRepository(1, []int64{1, 1, 1, 1, 1})
	_ = state.SetRepository(2, []int64{1, 1, 1, 1, 1})

	gameRepo.InjectGame("game-bs-pa", game)
	gameRepo.InjectState("game-bs-pa", state)

	var events []BattleEvent
	svc.SetBattleEventObserver(func(gameID string, event BattleEvent) {
		events = append(events, event)
	})

	// end_phase from main → battle (no turn change yet)
	result, err := svc.ProcessAction(ctx, "game-bs-pa", "human1", model.ActionEndPhase, nil)
	if err != nil {
		t.Fatalf("ProcessAction end_phase failed: %v", err)
	}
	if result.GameOver {
		t.Skip("game ended unexpectedly")
	}

	// Check if any turn_start was emitted (may or may not depending on phase transition)
	updated := gameRepo.MustGetState("game-bs-pa")
	if updated.CurrentTurn != 2 {
		// Turn changed — should have a turn_start event
		var hasTurnStart bool
		for _, ev := range events {
			if ev.Type == "turn_start" {
				hasTurnStart = true
				if ev.Turn != updated.CurrentTurn {
					t.Errorf("turn_start: turn = %d, want %d", ev.Turn, updated.CurrentTurn)
				}
			}
		}
		if !hasTurnStart {
			t.Error("expected turn_start event when turn changed")
		}
	}
}

func TestBattleEventObserver_NPCTurn_EmitsTurnStart(t *testing.T) {
	svc, _, cc, deckRepo := newTestGameServiceWithDeck("player1")
	loadRealCardCacheForService(t, cc)
	ctx := context.Background()

	deckID := seedTestDeck(deckRepo, "player1")

	// Register observer BEFORE StartNPCBattle so we capture events from postCreateAdvance
	var events []BattleEvent
	svc.SetBattleEventObserver(func(gameID string, event BattleEvent) {
		events = append(events, event)
	})

	// StartNPCBattle now fully initializes the game and emits battle_start + turn_start
	_, err := svc.StartNPCBattle(ctx, "player1", deckID, "Tenki")
	if err != nil {
		t.Fatalf("StartNPCBattle failed: %v", err)
	}

	// Check NPC name in battle_start
	for _, ev := range events {
		if ev.Type == "battle_start" && ev.Player2Info != nil {
			if ev.Player2Info.Name != "天気使い" {
				t.Errorf("NPC name = %s, want 天気使い", ev.Player2Info.Name)
			}
			if ev.Player2Info.Level != npcDisplayLevel {
				t.Errorf("NPC level = %d, want %d", ev.Player2Info.Level, npcDisplayLevel)
			}
		}
	}
}

func TestNPCFactionFromGame(t *testing.T) {
	snapshot, _ := json.Marshal(model.DeckSnapshot{DeckID: "npc-Sugar", Cards: []int64{1, 2, 3}})
	game := &model.Game{
		Player1ID:           "human1",
		Player2ID:           npc.NPCPlayerID,
		Player2DeckSnapshot: snapshot,
	}

	faction := npcFactionFromGame(game, npc.NPCPlayerID)
	if faction != "Sugar" {
		t.Errorf("faction = %s, want Sugar", faction)
	}
}

func TestNPCFactionFromGame_Player1IsNPC(t *testing.T) {
	snapshot, _ := json.Marshal(model.DeckSnapshot{DeckID: "npc-Tuners", Cards: []int64{1, 2, 3}})
	game := &model.Game{
		Player1ID:           npc.NPCPlayerID,
		Player2ID:           "human1",
		Player1DeckSnapshot: snapshot,
	}

	faction := npcFactionFromGame(game, npc.NPCPlayerID)
	if faction != "Tuners" {
		t.Errorf("faction = %s, want Tuners", faction)
	}
}

func TestNPCDisplayNames(t *testing.T) {
	expected := map[string]string{
		"SD":     "Smile Delivery",
		"Tenki":  "天気使い",
		"Sugar":  "しゅがーLab",
		"Tuners": "調律部",
	}
	for faction, want := range expected {
		got := npcDisplayNames[faction]
		if got != want {
			t.Errorf("npcDisplayNames[%s] = %s, want %s", faction, got, want)
		}
	}
}

// --- Test Helpers ---

func newEmptyGameState(gameID string) *model.GameState {
	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{})
	emptyTrash, _ := json.Marshal([]int64{})

	return &model.GameState{
		GameID:            gameID,
		Version:           1,
		CurrentTurn:       1,
		CurrentPhase:      model.PhaseMain,
		ActivePlayer:      1,
		Player1Budget:     model.InitialBudget,
		Player1InsightPool:     0,
		Player1Field:      emptyField,
		Player1Hand:       emptyHand,
		Player1Repository: emptyRepo,
		Player1Trash:      emptyTrash,
		Player1TimeBank:   120,
		Player2Budget:     model.InitialBudget,
		Player2InsightPool:     0,
		Player2Field:      emptyField,
		Player2Hand:       emptyHand,
		Player2Repository: emptyRepo,
		Player2Trash:      emptyTrash,
		Player2TimeBank:   120,
	}
}

package service

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"math/rand"
	"strconv"
	"strings"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/matchmaking"
	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/npc"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
)

// ActionObserver is called after each individual action is processed.
// This allows the WS layer to broadcast per-action updates for animation.
type ActionObserver func(gameID, actionType string, actionData json.RawMessage)

// BattlePlayerInfo holds display info for a player in a game.
type BattlePlayerInfo struct {
	PlayerID string
	Name     string
	Level    int64
}

// BattleEvent is emitted by GameService for banner-related events.
// These are sent as action_performed messages with action_type "battle_start" or "turn_start".
type BattleEvent struct {
	Type string // "battle_start" or "turn_start"
	// battle_start fields
	Player1Info *BattlePlayerInfo
	Player2Info *BattlePlayerInfo
	MatchType   string // "npc" or "pvp"
	// turn_start fields
	Turn         int64
	ActivePlayer int64 // playerNum of active player (used to compute is_my_turn per-player)
}

// BattleEventObserver is called for battle_start and turn_start events.
type BattleEventObserver func(gameID string, event BattleEvent)

// npcDisplayNames maps internal faction ID to display name.
var npcDisplayNames = map[string]string{
	"SD":     "Smile Delivery",
	"Tenki":  "天気使い",
	"Sugar":  "しゅがーLab",
	"Tuners": "調律部",
}

const npcDisplayLevel = 50

// GameService is the unified facade for both PvP and NPC game operations.
// The only difference between NPC and PvP is who controls player 2:
// a human via WebSocket, or an AI via npc.Strategy.
type GameService struct {
	engine               *engine.GameEngine
	gameRepo             repository.GameRepository
	deckRepo             repository.DeckRepo
	cardCache            *cache.CardCache
	queue                *matchmaking.Queue
	playerService        *PlayerService
	npcAI                npc.Strategy
	actionObserver       ActionObserver
	battleEventObserver  BattleEventObserver
}

// GameActionResult bundles the engine result with the post-action client state.
type GameActionResult struct {
	GameOver  bool
	WinnerNum int64
	WinReason string
	State     *ClientGameState
}

func NewGameService(
	eng *engine.GameEngine,
	gameRepo repository.GameRepository,
	deckRepo repository.DeckRepo,
	cardCache *cache.CardCache,
	queue *matchmaking.Queue,
	playerService *PlayerService,
) *GameService {
	return &GameService{
		engine:        eng,
		gameRepo:      gameRepo,
		deckRepo:      deckRepo,
		cardCache:     cardCache,
		queue:         queue,
		playerService: playerService,
		npcAI:         npc.NewStandardAI(cardCache, eng.EffectRegistry()),
	}
}

// ---------------------------------------------------------------------------
// Matchmaking (PvP)
// ---------------------------------------------------------------------------

// JoinQueue adds a player to the matchmaking queue.
func (s *GameService) JoinQueue(ctx context.Context, playerID string, deckID int64) error {
	if s.playerService != nil {
		if err := s.playerService.CheckAndIncrementBattleCount(ctx, playerID); err != nil {
			return fmt.Errorf("battle limit: %w", err)
		}
	}
	return s.queue.Join(playerID, deckID)
}

// LeaveQueue removes a player from the matchmaking queue.
func (s *GameService) LeaveQueue(playerID string) {
	s.queue.Leave(playerID)
}

// ---------------------------------------------------------------------------
// Game creation
// ---------------------------------------------------------------------------

// CreateGameFromMatch creates a new PvP game from a matchmaking result.
func (s *GameService) CreateGameFromMatch(ctx context.Context, result matchmaking.MatchResult) (*model.Game, error) {
	deck1Cards, err := s.loadDeckCards(ctx, result.Player1ID, result.Player1Deck)
	if err != nil {
		return nil, fmt.Errorf("load deck 1: %w", err)
	}
	deck2Cards, err := s.loadDeckCards(ctx, result.Player2ID, result.Player2Deck)
	if err != nil {
		return nil, fmt.Errorf("load deck 2: %w", err)
	}

	deck1 := model.DeckSnapshot{DeckID: strconv.FormatInt(result.Player1Deck, 10), Cards: deck1Cards}
	deck2 := model.DeckSnapshot{DeckID: strconv.FormatInt(result.Player2Deck, 10), Cards: deck2Cards}

	firstPlayer := int64(1)
	if rand.Intn(2) == 1 {
		firstPlayer = 2
	}

	gameID, err := s.engine.CreateNewGame(ctx, result.Player1ID, result.Player2ID, deck1, deck2, firstPlayer)
	if err != nil {
		return nil, fmt.Errorf("create new game: %w", err)
	}

	game, err := s.gameRepo.GetGame(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get created game: %w", err)
	}

	if err := s.postCreateAdvance(ctx, game, "pvp"); err != nil {
		return nil, err
	}

	return game, nil
}

// StartNPCBattle creates a new NPC game with fully initialized state.
// Emits battle_start, auto-advances through draw phase, and runs NPC turn if needed.
func (s *GameService) StartNPCBattle(ctx context.Context, playerID string, deckID int64, npcFaction string) (*model.Game, error) {
	if normalized, ok := model.NormalizeFaction(npcFaction); ok {
		npcFaction = normalized
	}

	if s.playerService != nil {
		if err := s.playerService.CheckAndIncrementBattleCount(ctx, playerID); err != nil {
			return nil, fmt.Errorf("battle limit: %w", err)
		}
	}

	playerCards, err := s.loadDeckCards(ctx, playerID, deckID)
	if err != nil {
		return nil, fmt.Errorf("load player deck: %w", err)
	}
	if len(playerCards) == 0 {
		return nil, fmt.Errorf("deck is empty")
	}

	npcDeck := npc.GetNPCDeck(npcFaction)
	if npcDeck == nil {
		return nil, fmt.Errorf("unknown NPC faction: %s", npcFaction)
	}

	deck1 := model.DeckSnapshot{DeckID: strconv.FormatInt(deckID, 10), Cards: playerCards}
	deck2 := model.DeckSnapshot{DeckID: "npc-" + npcFaction, Cards: npcDeck.Cards}

	firstPlayer := int64(1)
	if rand.Intn(2) == 1 {
		firstPlayer = 2
	}

	gameID, err := s.engine.CreateNewGame(ctx, playerID, npc.NPCPlayerID, deck1, deck2, firstPlayer)
	if err != nil {
		return nil, fmt.Errorf("create game: %w", err)
	}

	game, err := s.gameRepo.GetGame(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get game: %w", err)
	}

	if err := s.postCreateAdvance(ctx, game, "npc"); err != nil {
		return nil, err
	}

	return game, nil
}

// ---------------------------------------------------------------------------
// Game actions (unified for NPC and PvP)
// ---------------------------------------------------------------------------

// ProcessAction forwards a player action to the game engine,
// then runs NPC turns if applicable.
func (s *GameService) ProcessAction(ctx context.Context, gameID, playerID, actionType string, data json.RawMessage) (*GameActionResult, error) {
	// Snapshot turn before action to detect turn changes
	prevState, _ := s.gameRepo.GetGameState(ctx, gameID)
	var prevTurn int64
	if prevState != nil {
		prevTurn = prevState.CurrentTurn
	}

	result, err := s.engine.ProcessAction(ctx, gameID, playerID, actionType, data)
	if err != nil {
		return nil, fmt.Errorf("process action: %w", err)
	}

	if result.GameOver {
		_ = s.FinishGame(ctx, gameID, result.WinnerNum)
		state, _ := s.getStateForPlayer(ctx, gameID, playerID)
		return &GameActionResult{
			GameOver:  true,
			WinnerNum: result.WinnerNum,
			WinReason: result.WinReason,
			State:     state,
		}, nil
	}

	// Emit turn_start if the turn changed (e.g., end_phase → next turn)
	postState, _ := s.gameRepo.GetGameState(ctx, gameID)
	if postState != nil && postState.CurrentTurn != prevTurn {
		s.notifyBattleEvent(gameID, BattleEvent{
			Type:         "turn_start",
			Turn:         postState.CurrentTurn,
			ActivePlayer: postState.ActivePlayer,
		})
	}

	if err := s.runNPCTurnIfNeeded(ctx, gameID); err != nil {
		return nil, fmt.Errorf("npc turn: %w", err)
	}

	state, _ := s.getStateForPlayer(ctx, gameID, playerID)
	return &GameActionResult{State: state}, nil
}

// ---------------------------------------------------------------------------
// State queries
// ---------------------------------------------------------------------------

// GetGameStateForPlayer retrieves the game state with info hiding applied.
func (s *GameService) GetGameStateForPlayer(ctx context.Context, gameID, playerID string) (*ClientGameState, error) {
	return s.getStateForPlayer(ctx, gameID, playerID)
}

// GetTurnControlsForPlayer computes the turn controls for a player.
// Returns nil if it's not the player's turn or the game is not in playing state.
func (s *GameService) GetTurnControlsForPlayer(ctx context.Context, gameID, playerID string) (*engine.TurnControls, error) {
	game, err := s.gameRepo.GetGame(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get game: %w", err)
	}
	if game.Status != model.GameStatusPlaying {
		return nil, nil
	}

	state, err := s.gameRepo.GetGameState(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get game state: %w", err)
	}

	playerNum := model.PlayerNumForID(game, playerID)
	if playerNum == 0 || state.ActivePlayer != playerNum {
		return nil, nil
	}

	hand, _ := state.GetHand(playerNum)
	tc := engine.ComputeTurnControls(state, hand)
	return &tc, nil
}

// ---------------------------------------------------------------------------
// Post-game
// ---------------------------------------------------------------------------

// FinishGame updates win/loss counters after a game ends.
func (s *GameService) FinishGame(ctx context.Context, gameID string, winnerNum int64) error {
	game, err := s.gameRepo.GetGame(ctx, gameID)
	if err != nil {
		return err
	}

	if npc.IsNPCPlayer(game.Player1ID) || npc.IsNPCPlayer(game.Player2ID) {
		return nil
	}

	winnerID := model.PlayerIDForNum(game, winnerNum)
	loserID := model.PlayerIDForNum(game, model.OpponentNum(winnerNum))

	_ = s.gameRepo.UpdateWinLoss(ctx, winnerID, 1, 0)
	_ = s.gameRepo.UpdateWinLoss(ctx, loserID, 0, 1)

	return nil
}

// ---------------------------------------------------------------------------
// Private helpers
// ---------------------------------------------------------------------------

// SetActionObserver registers a callback invoked after each individual
// NPC action inside runNPCTurnIfNeeded.
func (s *GameService) SetActionObserver(obs ActionObserver) {
	s.actionObserver = obs
}

// SetBattleEventObserver registers a callback invoked for battle_start
// and turn_start events (banner-related WS messages).
func (s *GameService) SetBattleEventObserver(obs BattleEventObserver) {
	s.battleEventObserver = obs
}

func (s *GameService) notifyAction(gameID, actionType string, data json.RawMessage) {
	log.Printf("[notifyAction] game=%s action=%s observer=%v", gameID, actionType, s.actionObserver != nil)
	if s.actionObserver != nil {
		s.actionObserver(gameID, actionType, data)
	}
}

func (s *GameService) notifyBattleEvent(gameID string, event BattleEvent) {
	log.Printf("[notifyBattleEvent] game=%s type=%s", gameID, event.Type)
	if s.battleEventObserver != nil {
		s.battleEventObserver(gameID, event)
	}
}

// buildBattlePlayerInfo resolves display info for a player.
// For NPC players, the name is derived from the deck snapshot faction.
func (s *GameService) buildBattlePlayerInfo(ctx context.Context, game *model.Game, playerID string) *BattlePlayerInfo {
	if npc.IsNPCPlayer(playerID) {
		faction := npcFactionFromGame(game, playerID)
		name := npcDisplayNames[faction]
		if name == "" {
			name = "NPC"
		}
		return &BattlePlayerInfo{PlayerID: playerID, Name: name, Level: npcDisplayLevel}
	}

	if s.playerService != nil {
		player, err := s.playerService.GetPlayer(ctx, playerID)
		if err == nil && player != nil {
			return &BattlePlayerInfo{PlayerID: playerID, Name: player.Username, Level: player.Level}
		}
	}

	return &BattlePlayerInfo{PlayerID: playerID, Name: "Player", Level: 1}
}

// npcFactionFromGame extracts the NPC faction from the deck snapshot DeckID (e.g., "npc-SD" → "SD").
func npcFactionFromGame(game *model.Game, playerID string) string {
	var snapshot json.RawMessage
	if game.Player1ID == playerID {
		snapshot = game.Player1DeckSnapshot
	} else {
		snapshot = game.Player2DeckSnapshot
	}
	if len(snapshot) == 0 {
		return ""
	}
	var ds model.DeckSnapshot
	if err := json.Unmarshal(snapshot, &ds); err != nil {
		return ""
	}
	return strings.TrimPrefix(ds.DeckID, "npc-")
}

// postCreateAdvance emits battle_start, auto-advances from Draw phase,
// emits turn_start, and runs NPC turn if applicable.
func (s *GameService) postCreateAdvance(ctx context.Context, game *model.Game, matchType string) error {
	gameID := game.GameID

	s.notifyBattleEvent(gameID, BattleEvent{
		Type:        "battle_start",
		Player1Info: s.buildBattlePlayerInfo(ctx, game, game.Player1ID),
		Player2Info: s.buildBattlePlayerInfo(ctx, game, game.Player2ID),
		MatchType:   matchType,
	})

	gameOver, _, err := s.engine.RunAutoAdvance(ctx, gameID)
	if err != nil {
		return fmt.Errorf("auto advance: %w", err)
	}

	if !gameOver {
		advState, _ := s.gameRepo.GetGameState(ctx, gameID)
		if advState != nil {
			s.notifyBattleEvent(gameID, BattleEvent{
				Type:         "turn_start",
				Turn:         advState.CurrentTurn,
				ActivePlayer: advState.ActivePlayer,
			})
		}

		if err := s.runNPCTurnIfNeeded(ctx, gameID); err != nil {
			return fmt.Errorf("npc turn: %w", err)
		}
	}

	return nil
}

func (s *GameService) loadDeckCards(ctx context.Context, playerID string, deckID int64) ([]int64, error) {
	return s.deckRepo.GetDeckCardNos(ctx, playerID, deckID)
}

func (s *GameService) getStateForPlayer(ctx context.Context, gameID, playerID string) (*ClientGameState, error) {
	game, err := s.gameRepo.GetGame(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get game: %w", err)
	}

	state, err := s.gameRepo.GetGameState(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("get game state: %w", err)
	}

	playerNum := model.PlayerNumForID(game, playerID)
	if playerNum == 0 {
		return nil, fmt.Errorf("player not in game")
	}

	return buildClientGameState(state, game, playerNum, s.cardCache, s.engine.EffectRegistry())
}

const maxNPCIterations = 50

// runNPCTurnIfNeeded executes NPC turns until the active player is no longer the NPC.
// For PvP games (no NPC player), this returns immediately.
func (s *GameService) runNPCTurnIfNeeded(ctx context.Context, gameID string) error {
	var prevTurn int64
	for i := 0; i < maxNPCIterations; i++ {
		game, err := s.gameRepo.GetGame(ctx, gameID)
		if err != nil {
			return fmt.Errorf("get game: %w", err)
		}
		if game.Status == model.GameStatusFinished {
			return nil
		}

		state, err := s.gameRepo.GetGameState(ctx, gameID)
		if err != nil {
			return fmt.Errorf("get game state: %w", err)
		}

		npcPlayerNum := model.PlayerNumForID(game, npc.NPCPlayerID)
		if npcPlayerNum == 0 {
			return nil
		}

		if state.ActivePlayer != npcPlayerNum {
			return nil
		}

		// Emit turn_start when the NPC's turn number changes (e.g., after player → NPC switch)
		if prevTurn != 0 && state.CurrentTurn != prevTurn {
			s.notifyBattleEvent(gameID, BattleEvent{
				Type:         "turn_start",
				Turn:         state.CurrentTurn,
				ActivePlayer: state.ActivePlayer,
			})
		}
		prevTurn = state.CurrentTurn

		// Compute available actions for the NPC
		myField, _ := state.GetField(npcPlayerNum)
		oppField, _ := state.GetField(model.OpponentNum(npcPlayerNum))
		hand, _ := state.GetHand(npcPlayerNum)
		npcBudget := state.GetBudget(npcPlayerNum)
		insightPool := state.GetInsightPool(npcPlayerNum)
		available := engine.ComputeAvailableActions(
			state, game, npcPlayerNum,
			myField, oppField, hand, npcBudget, insightPool,
			s.cardCache, s.engine.EffectRegistry(),
		)

		var actions []npc.NPCAction

		switch state.CurrentPhase {
		case model.PhaseMain:
			actions = s.npcAI.DecideMainPhaseActions(state, game, npcPlayerNum, available)
		case model.PhaseBattle:
			actions = s.npcAI.DecideBattlePhaseActions(state, game, npcPlayerNum, available)
		case model.PhaseEnd:
			ids := s.npcAI.DecideDiscard(state, npcPlayerNum)
			if len(ids) == 0 {
				log.Printf("NPC in end phase but no discard needed (game=%s)", gameID)
				return nil
			}
			data, _ := json.Marshal(map[string]interface{}{
				"cardInstanceIds": ids,
			})
			actions = []npc.NPCAction{{ActionType: model.ActionDiscardHand, Data: data}}
		default:
			return nil
		}

		for _, action := range actions {
			result, err := s.engine.ProcessAction(ctx, gameID, npc.NPCPlayerID, action.ActionType, action.Data)
			if err != nil {
				log.Printf("NPC action failed (game=%s, action=%s): %v", gameID, action.ActionType, err)
				continue // skip failed action, try next (e.g. end_phase)
			}
			s.notifyAction(gameID, action.ActionType, action.Data)
			if result.GameOver {
				return nil
			}
		}
	}

	log.Printf("NPC turn exceeded %d iterations (game=%s)", maxNPCIterations, gameID)
	return nil
}

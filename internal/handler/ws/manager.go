package ws

import (
	"context"
	"encoding/json"
	"log"
	"sync"
	"time"

	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/service"
)

const disconnectTimeout = 60 * time.Second

type disconnectInfo struct {
	playerID string
	gameID   string
	timer    *time.Timer
}

type Manager struct {
	mu          sync.RWMutex
	connections map[string]*Connection // playerID -> Connection
	gameMembers map[string][]string    // gameID -> []playerID
	playerGames map[string]string      // playerID -> gameID

	disconnects map[string]*disconnectInfo // playerID -> disconnect timer

	gameService *service.GameService
}

func NewManager(gameService *service.GameService) *Manager {
	return &Manager{
		connections: make(map[string]*Connection),
		gameMembers: make(map[string][]string),
		playerGames: make(map[string]string),
		disconnects: make(map[string]*disconnectInfo),
		gameService: gameService,
	}
}

func (m *Manager) Register(conn *Connection) {
	m.mu.Lock()
	defer m.mu.Unlock()

	// Cancel disconnect timer if reconnecting
	if info, ok := m.disconnects[conn.playerID]; ok {
		info.timer.Stop()
		delete(m.disconnects, conn.playerID)
		log.Printf("player %s reconnected", conn.playerID)
	}

	// Replace existing connection
	if old, ok := m.connections[conn.playerID]; ok {
		old.Close()
	}
	m.connections[conn.playerID] = conn
}

func (m *Manager) Unregister(conn *Connection) {
	m.mu.Lock()
	defer m.mu.Unlock()

	if existing, ok := m.connections[conn.playerID]; ok && existing == conn {
		delete(m.connections, conn.playerID)

		// Start disconnect timer if player is in a game
		if gameID, inGame := m.playerGames[conn.playerID]; inGame {
			timer := time.AfterFunc(disconnectTimeout, func() {
				m.handleDisconnectTimeout(conn.playerID, gameID)
			})
			m.disconnects[conn.playerID] = &disconnectInfo{
				playerID: conn.playerID,
				gameID:   gameID,
				timer:    timer,
			}
			log.Printf("player %s disconnected, %v timeout started", conn.playerID, disconnectTimeout)
		}
	}
}

func (m *Manager) handleDisconnectTimeout(playerID, gameID string) {
	m.mu.Lock()
	delete(m.disconnects, playerID)
	m.mu.Unlock()

	log.Printf("player %s disconnect timeout expired for game %s, forfeit", playerID, gameID)

	if m.gameService != nil {
		// Process forfeit via engine
		ctx := context.Background()
		result, err := m.gameService.ProcessAction(ctx, gameID, playerID, "forfeit", nil)
		if err != nil {
			log.Printf("forfeit error: %v", err)
		}
		if result != nil && result.GameOver {
			m.broadcastGameOver(gameID, result.WinnerNum, model.WinReasonDisconnect)
		}
	}
}

func (m *Manager) JoinGame(playerID, gameID string) {
	m.mu.Lock()
	defer m.mu.Unlock()

	m.playerGames[playerID] = gameID
	m.gameMembers[gameID] = appendUnique(m.gameMembers[gameID], playerID)
}

func (m *Manager) LeaveGame(playerID string) {
	m.mu.Lock()
	defer m.mu.Unlock()

	if gameID, ok := m.playerGames[playerID]; ok {
		delete(m.playerGames, playerID)
		m.gameMembers[gameID] = removeString(m.gameMembers[gameID], playerID)
		if len(m.gameMembers[gameID]) == 0 {
			delete(m.gameMembers, gameID)
		}
	}
}

func (m *Manager) BroadcastToGame(gameID string, msg *WSMessage) {
	m.mu.RLock()
	players := m.gameMembers[gameID]
	m.mu.RUnlock()

	for _, pid := range players {
		m.mu.RLock()
		conn, ok := m.connections[pid]
		m.mu.RUnlock()
		if ok {
			conn.SendMessage(msg)
		}
	}
}

func (m *Manager) SendToPlayer(playerID string, msg *WSMessage) {
	m.mu.RLock()
	conn, ok := m.connections[playerID]
	m.mu.RUnlock()

	if ok {
		conn.SendMessage(msg)
	}
}

// SendGameStateToPlayers sends per-player info-hidden game states to all players in a game.
func (m *Manager) SendGameStateToPlayers(gameID string) {
	m.mu.RLock()
	players := m.gameMembers[gameID]
	m.mu.RUnlock()

	ctx := context.Background()
	for _, pid := range players {
		clientState, err := m.gameService.GetGameStateForPlayer(ctx, gameID, pid)
		if err != nil {
			log.Printf("get game state for player %s: %v", pid, err)
			continue
		}
		m.SendToPlayer(pid, &WSMessage{
			Type: model.WSMsgGameState,
			Data: service.MarshalClientGameState(clientState),
		})
	}
}

// SendTurnControlsToPlayers sends turn_controls to the active player in a game.
func (m *Manager) SendTurnControlsToPlayers(gameID string) {
	m.mu.RLock()
	players := m.gameMembers[gameID]
	m.mu.RUnlock()

	ctx := context.Background()
	for _, pid := range players {
		tc, err := m.gameService.GetTurnControlsForPlayer(ctx, gameID, pid)
		if err != nil {
			log.Printf("get turn controls for player %s: %v", pid, err)
			continue
		}
		if tc == nil {
			continue
		}
		m.SendToPlayer(pid, &WSMessage{
			Type: model.WSMsgTurnControls,
			Data: mustMarshal(TurnControlsMessage{
				CanEndPhase:     tc.CanEndPhase,
				DiscardRequired: tc.DiscardRequired,
			}),
		})
	}
}

// BroadcastActionPerformed sends an action_performed message to all connected
// players in the game (including the actor). The client uses this to trigger
// animations for every action (attack slash, deploy, etc.).
func (m *Manager) BroadcastActionPerformed(gameID, actionType string, actionData json.RawMessage) {
	m.mu.RLock()
	players := m.gameMembers[gameID]
	m.mu.RUnlock()

	log.Printf("[BroadcastActionPerformed] game=%s action=%s players=%v", gameID, actionType, players)

	ctx := context.Background()
	for _, pid := range players {
		clientState, err := m.gameService.GetGameStateForPlayer(ctx, gameID, pid)
		if err != nil {
			log.Printf("action_performed: get state for %s: %v", pid, err)
			continue
		}

		m.SendToPlayer(pid, &WSMessage{
			Type: model.WSMsgActionPerformed,
			Data: mustMarshal(ActionPerformedMessage{
				ActionType: actionType,
				ActionData: actionData,
				State:      service.MarshalClientGameState(clientState),
			}),
		})
	}
}

// BroadcastBattleEvent sends a battle_start or turn_start message as an
// action_performed envelope. Each player receives per-player action_data
// (my_name/opponent_name for battle_start, is_my_turn for turn_start).
func (m *Manager) BroadcastBattleEvent(gameID string, event service.BattleEvent) {
	m.mu.RLock()
	players := m.gameMembers[gameID]
	m.mu.RUnlock()

	log.Printf("[BroadcastBattleEvent] game=%s type=%s players=%v", gameID, event.Type, players)

	ctx := context.Background()
	for _, pid := range players {
		clientState, err := m.gameService.GetGameStateForPlayer(ctx, gameID, pid)
		if err != nil {
			log.Printf("battle_event: get state for %s: %v", pid, err)
			continue
		}

		actionData := m.buildBattleEventData(event, gameID, pid)

		m.SendToPlayer(pid, &WSMessage{
			Type: model.WSMsgActionPerformed,
			Data: mustMarshal(ActionPerformedMessage{
				ActionType: event.Type,
				ActionData: actionData,
				State:      service.MarshalClientGameState(clientState),
			}),
		})
	}
}

// buildBattleEventData creates per-player action_data for a BattleEvent.
func (m *Manager) buildBattleEventData(event service.BattleEvent, gameID, playerID string) json.RawMessage {
	switch event.Type {
	case "battle_start":
		p1 := event.Player1Info
		p2 := event.Player2Info
		var myInfo, oppInfo *service.BattlePlayerInfo
		if p1 != nil && p1.PlayerID == playerID {
			myInfo, oppInfo = p1, p2
		} else {
			myInfo, oppInfo = p2, p1
		}
		data, _ := json.Marshal(map[string]interface{}{
			"my_name":        myInfo.Name,
			"my_level":       myInfo.Level,
			"opponent_name":  oppInfo.Name,
			"opponent_level": oppInfo.Level,
			"match_type":     event.MatchType,
		})
		return data

	case "turn_start":
		// Determine playerNum for this player to compute is_my_turn
		ctx := context.Background()
		game, err := m.gameService.GetGameStateForPlayer(ctx, gameID, playerID)
		isMyTurn := false
		if err == nil && game != nil {
			isMyTurn = game.IsMyTurn
		}
		data, _ := json.Marshal(map[string]interface{}{
			"turn":       event.Turn,
			"is_my_turn": isMyTurn,
		})
		return data

	default:
		return json.RawMessage(`{}`)
	}
}

func (m *Manager) broadcastGameOver(gameID string, winnerNum int64, reason string) {
	m.BroadcastToGame(gameID, &WSMessage{
		Type: model.WSMsgGameOver,
		Data: mustMarshal(GameOverMessage{
			GameID:    gameID,
			WinnerNum: winnerNum,
			WinReason: reason,
		}),
	})
}

func (m *Manager) HandleMessage(conn *Connection, msg *WSMessage) {
	ctx := context.Background()

	switch msg.Type {
	case model.WSMsgGameEnter:
		var join GameEnterMessage
		if err := json.Unmarshal(msg.Data, &join); err != nil {
			conn.SendMessage(&WSMessage{
				Type: model.WSMsgError,
				Data: mustMarshal(ErrorMessage{Code: "invalid_data", Message: "invalid game_enter data", Retryable: false}),
			})
			return
		}
		m.JoinGame(conn.playerID, join.GameID)
		conn.SendMessage(&WSMessage{Type: model.WSMsgGameEntered, Data: mustMarshal(map[string]string{"game_id": join.GameID})})

		// Send current game state
		m.SendGameStateToPlayers(join.GameID)
		m.SendTurnControlsToPlayers(join.GameID)

	case model.WSMsgMatchmakingStart:
		var qj MatchmakingStartMessage
		if err := json.Unmarshal(msg.Data, &qj); err != nil {
			conn.SendMessage(&WSMessage{
				Type: model.WSMsgError,
				Data: mustMarshal(ErrorMessage{Code: "invalid_data", Message: "invalid matchmaking_start data", Retryable: false}),
			})
			return
		}
		if m.gameService != nil {
			if err := m.gameService.JoinQueue(ctx, conn.playerID, qj.DeckID); err != nil {
				conn.SendMessage(&WSMessage{
					Type: model.WSMsgError,
					Data: mustMarshal(ErrorMessage{Code: "matchmaking_error", Message: err.Error(), Retryable: true}),
				})
				return
			}
		}
		conn.SendMessage(&WSMessage{Type: model.WSMsgMatchmakingStarted})

	case model.WSMsgMatchmakingCancel:
		if m.gameService != nil {
			m.gameService.LeaveQueue(conn.playerID)
		}
		conn.SendMessage(&WSMessage{Type: model.WSMsgMatchmakingCancelled})

	case model.WSMsgNpcBattleStart:
		var req NPCBattleStartMessage
		if err := json.Unmarshal(msg.Data, &req); err != nil {
			conn.SendMessage(&WSMessage{
				Type: model.WSMsgError,
				Data: mustMarshal(ErrorMessage{Code: "invalid_data", Message: "invalid npc_battle_start data", Retryable: false}),
			})
			return
		}
		if m.gameService != nil {
			game, err := m.gameService.StartNPCBattle(ctx, conn.playerID, req.DeckID, req.NPCFaction)
			if err != nil {
				conn.SendMessage(&WSMessage{
					Type: model.WSMsgError,
					Data: mustMarshal(ErrorMessage{Code: "npc_battle_error", Message: err.Error(), Retryable: true}),
				})
				return
			}
			conn.SendMessage(&WSMessage{
				Type: model.WSMsgNpcBattleCreated,
				Data: mustMarshal(NPCBattleCreatedMessage{
					GameID:    game.GameID,
					Player1ID: game.Player1ID,
					Player2ID: game.Player2ID,
				}),
			})
		}

	case model.WSMsgGameAction:
		var action GameActionMessage
		if err := json.Unmarshal(msg.Data, &action); err != nil {
			conn.SendMessage(&WSMessage{
				Type: model.WSMsgError,
				Data: mustMarshal(ErrorMessage{Code: "invalid_data", Message: "invalid game_action data", Retryable: false}),
			})
			return
		}
		if m.gameService != nil {
			result, err := m.gameService.ProcessAction(ctx, action.GameID, conn.playerID, action.ActionType, action.Data)
			if err != nil {
				conn.SendMessage(&WSMessage{
					Type: model.WSMsgActionRejected,
					Data: mustMarshal(ActionRejectedMessage{
						GameID:     action.GameID,
						ActionType: action.ActionType,
						Reason:     err.Error(),
					}),
				})
				return
			}

			// Send action_performed to all players (triggers animations on client)
			m.BroadcastActionPerformed(action.GameID, action.ActionType, action.Data)

			// Broadcast updated state to both players
			m.SendGameStateToPlayers(action.GameID)
			m.SendTurnControlsToPlayers(action.GameID)

			// Handle game over (FinishGame already called inside ProcessAction)
			if result != nil && result.GameOver {
				m.broadcastGameOver(action.GameID, result.WinnerNum, result.WinReason)

				// Clean up game membership
				m.mu.RLock()
				players := m.gameMembers[action.GameID]
				m.mu.RUnlock()
				for _, pid := range players {
					m.LeaveGame(pid)
				}
			}
		}

	case model.WSMsgUseStamp:
		var req UseStampMessage
		if err := json.Unmarshal(msg.Data, &req); err != nil {
			conn.SendMessage(&WSMessage{
				Type: model.WSMsgError,
				Data: mustMarshal(ErrorMessage{Code: "invalid_data", Message: "invalid use_stamp data", Retryable: false}),
			})
			return
		}
		// Ownership validation skipped for MVP (cosmetic only, no gameplay impact).
		m.BroadcastToGame(req.GameID, &WSMessage{
			Type: model.WSMsgStampUsed,
			Data: mustMarshal(StampUsedMessage{
				GameID:   req.GameID,
				PlayerID: conn.playerID,
				StampNo:  req.StampNo,
			}),
		})

	case model.WSMsgPing:
		conn.SendMessage(&WSMessage{Type: model.WSMsgPong})

	default:
		log.Printf("unhandled message type: %s from player %s", msg.Type, conn.playerID)
	}
}

func appendUnique(slice []string, s string) []string {
	for _, v := range slice {
		if v == s {
			return slice
		}
	}
	return append(slice, s)
}

func removeString(slice []string, s string) []string {
	for i, v := range slice {
		if v == s {
			return append(slice[:i], slice[i+1:]...)
		}
	}
	return slice
}

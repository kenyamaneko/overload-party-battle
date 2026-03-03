package ws

import "encoding/json"

type WSMessage struct {
	Type string          `json:"type"`
	Data json.RawMessage `json:"data,omitempty"`
}

type GameEnterMessage struct {
	GameID string `json:"game_id"`
	DeckID int64  `json:"deck_id"`
}

type ErrorMessage struct {
	Code      string `json:"error_code"`
	Message   string `json:"message"`
	Retryable bool   `json:"retryable"`
}

// --- Game Action Messages ---

type MatchmakingStartMessage struct {
	DeckID int64 `json:"deck_id"`
}

type GameActionMessage struct {
	GameID     string          `json:"game_id"`
	ActionType string          `json:"action_type"`
	Data       json.RawMessage `json:"data"`
}

// --- Server → Client Messages ---

type MatchFoundMessage struct {
	GameID    string `json:"game_id"`
	Player1ID string `json:"player1_id"`
	Player2ID string `json:"player2_id"`
}

type GameStateUpdateMessage struct {
	GameID string          `json:"game_id"`
	State  json.RawMessage `json:"state"`
}

type GameOverMessage struct {
	GameID    string `json:"game_id"`
	WinnerNum int64  `json:"winner_num"`
	WinReason string `json:"win_reason"`
}

type ActionRejectedMessage struct {
	GameID     string `json:"game_id"`
	ActionType string `json:"action_type"`
	Reason     string `json:"reason"`
}

// --- Action Performed (Server → Client) ---

// ActionPerformedMessage is sent per-action to opponents so clients can
// animate each action individually (NPC turns and PvP opponent moves).
type ActionPerformedMessage struct {
	ActionType string          `json:"action_type"`
	ActionData json.RawMessage `json:"action_data"`
	State      json.RawMessage `json:"state"` // per-player info-hidden ClientGameState
}

// --- Turn Controls (Server → Client) ---

// TurnControlsMessage conveys game-flow controls that are not tied to any card.
// Sent as a separate WS message alongside game_state.
type TurnControlsMessage struct {
	CanEndPhase     bool `json:"can_end_phase"`
	DiscardRequired int  `json:"discard_required"`
}

// --- Stamp Messages ---

// UseStampMessage is sent by the client to use a stamp during a game.
type UseStampMessage struct {
	GameID  string `json:"game_id"`
	StampNo int64  `json:"stamp_no"`
}

// StampUsedMessage is broadcast to both players when a stamp is used.
type StampUsedMessage struct {
	GameID   string `json:"game_id"`
	PlayerID string `json:"player_id"`
	StampNo  int64  `json:"stamp_no"`
}

// --- NPC Battle Messages ---

// NPCBattleStartMessage is sent by the client to start an NPC battle.
type NPCBattleStartMessage struct {
	DeckID     int64  `json:"deck_id"`
	NPCFaction string `json:"npc_faction"`
}

// NPCBattleCreatedMessage is sent to the client after an NPC game is created.
type NPCBattleCreatedMessage struct {
	GameID    string `json:"game_id"`
	Player1ID string `json:"player1_id"`
	Player2ID string `json:"player2_id"`
}

package ws_test

import (
	"encoding/json"
	"testing"

	ws "github.com/kenyamaneko/overload-party-battle/internal/handler/ws"
)

// ---------------------------------------------------------------------------
// Client → Server: parse (unmarshal) tests
// ---------------------------------------------------------------------------

func TestWSMessage_GameAction_Parse(t *testing.T) {
	raw := `{"type":"game_action","data":{"game_id":"g1","action_type":"play_card","data":{"zone":"frontend","index":0}}}`

	var msg ws.WSMessage
	if err := json.Unmarshal([]byte(raw), &msg); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}
	if msg.Type != "game_action" {
		t.Fatalf("expected type game_action, got %s", msg.Type)
	}

	var action ws.GameActionMessage
	if err := json.Unmarshal(msg.Data, &action); err != nil {
		t.Fatalf("unmarshal GameActionMessage: %v", err)
	}
	if action.GameID != "g1" {
		t.Errorf("expected game_id g1, got %s", action.GameID)
	}
	if action.ActionType != "play_card" {
		t.Errorf("expected action_type play_card, got %s", action.ActionType)
	}

	// Verify nested action data
	var actionData struct {
		Zone  string `json:"zone"`
		Index int    `json:"index"`
	}
	if err := json.Unmarshal(action.Data, &actionData); err != nil {
		t.Fatalf("unmarshal action data: %v", err)
	}
	if actionData.Zone != "frontend" {
		t.Errorf("expected zone frontend, got %s", actionData.Zone)
	}
	if actionData.Index != 0 {
		t.Errorf("expected index 0, got %d", actionData.Index)
	}
}

func TestWSMessage_GameEnter_Parse(t *testing.T) {
	raw := `{"type":"game_enter","data":{"game_id":"g1","deck_id":42}}`

	var msg ws.WSMessage
	if err := json.Unmarshal([]byte(raw), &msg); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}
	if msg.Type != "game_enter" {
		t.Fatalf("expected type game_enter, got %s", msg.Type)
	}

	var enter ws.GameEnterMessage
	if err := json.Unmarshal(msg.Data, &enter); err != nil {
		t.Fatalf("unmarshal GameEnterMessage: %v", err)
	}
	if enter.GameID != "g1" {
		t.Errorf("expected game_id g1, got %s", enter.GameID)
	}
	if enter.DeckID != 42 {
		t.Errorf("expected deck_id 42, got %d", enter.DeckID)
	}
}

func TestWSMessage_Ping_Parse(t *testing.T) {
	raw := `{"type":"ping"}`

	var msg ws.WSMessage
	if err := json.Unmarshal([]byte(raw), &msg); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}
	if msg.Type != "ping" {
		t.Fatalf("expected type ping, got %s", msg.Type)
	}
	if msg.Data != nil {
		t.Errorf("expected nil data for ping, got %s", string(msg.Data))
	}
}

func TestWSMessage_InvalidJSON(t *testing.T) {
	raw := `{not valid json}`

	var msg ws.WSMessage
	err := json.Unmarshal([]byte(raw), &msg)
	if err == nil {
		t.Fatal("expected error for invalid JSON, got nil")
	}
}

func TestWSMessage_UnknownType(t *testing.T) {
	raw := `{"type":"totally_unknown","data":{"foo":"bar"}}`

	var msg ws.WSMessage
	if err := json.Unmarshal([]byte(raw), &msg); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}
	if msg.Type != "totally_unknown" {
		t.Fatalf("expected type totally_unknown, got %s", msg.Type)
	}
	// Type is just a string; unknown types should parse without error.
}

// ---------------------------------------------------------------------------
// Server → Client: marshal (serialize) tests
// ---------------------------------------------------------------------------

func TestTurnControlsMessage_Marshal(t *testing.T) {
	tc := ws.TurnControlsMessage{
		CanEndPhase:     true,
		DiscardRequired: 2,
	}

	data, err := json.Marshal(tc)
	if err != nil {
		t.Fatalf("marshal TurnControlsMessage: %v", err)
	}

	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if v, ok := m["can_end_phase"]; !ok || v != true {
		t.Errorf("expected can_end_phase true, got %v", v)
	}
	if v, ok := m["discard_required"]; !ok || v != float64(2) {
		t.Errorf("expected discard_required 2, got %v", v)
	}
}

func TestActionPerformedMessage_Marshal(t *testing.T) {
	ap := ws.ActionPerformedMessage{
		ActionType: "play_card",
		ActionData: json.RawMessage(`{"zone":"frontend","index":0}`),
		State:      json.RawMessage(`{"turn":3}`),
	}

	data, err := json.Marshal(ap)
	if err != nil {
		t.Fatalf("marshal ActionPerformedMessage: %v", err)
	}

	var m map[string]json.RawMessage
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	// Verify action_type
	var actionType string
	if err := json.Unmarshal(m["action_type"], &actionType); err != nil {
		t.Fatalf("unmarshal action_type: %v", err)
	}
	if actionType != "play_card" {
		t.Errorf("expected action_type play_card, got %s", actionType)
	}

	// Verify action_data is preserved as nested JSON
	var actionData struct {
		Zone  string `json:"zone"`
		Index int    `json:"index"`
	}
	if err := json.Unmarshal(m["action_data"], &actionData); err != nil {
		t.Fatalf("unmarshal action_data: %v", err)
	}
	if actionData.Zone != "frontend" {
		t.Errorf("expected zone frontend, got %s", actionData.Zone)
	}

	// Verify state is preserved
	var state struct {
		Turn int `json:"turn"`
	}
	if err := json.Unmarshal(m["state"], &state); err != nil {
		t.Fatalf("unmarshal state: %v", err)
	}
	if state.Turn != 3 {
		t.Errorf("expected turn 3, got %d", state.Turn)
	}
}

func TestGameStateUpdateMessage_Marshal(t *testing.T) {
	gsu := ws.GameStateUpdateMessage{
		GameID: "g1",
		State:  json.RawMessage(`{"turn":1,"phase":"deploy","player1":{"hp":10}}`),
	}

	data, err := json.Marshal(gsu)
	if err != nil {
		t.Fatalf("marshal GameStateUpdateMessage: %v", err)
	}

	// Round-trip: unmarshal back to verify structure
	var decoded ws.GameStateUpdateMessage
	if err := json.Unmarshal(data, &decoded); err != nil {
		t.Fatalf("unmarshal GameStateUpdateMessage: %v", err)
	}
	if decoded.GameID != "g1" {
		t.Errorf("expected game_id g1, got %s", decoded.GameID)
	}

	// Verify the nested state is preserved
	var state map[string]json.RawMessage
	if err := json.Unmarshal(decoded.State, &state); err != nil {
		t.Fatalf("unmarshal state: %v", err)
	}
	if _, ok := state["turn"]; !ok {
		t.Error("expected state to contain 'turn' key")
	}
	if _, ok := state["phase"]; !ok {
		t.Error("expected state to contain 'phase' key")
	}
	if _, ok := state["player1"]; !ok {
		t.Error("expected state to contain 'player1' key")
	}
}

func TestActionRejectedMessage_Marshal(t *testing.T) {
	ar := ws.ActionRejectedMessage{
		GameID:     "g1",
		ActionType: "play_card",
		Reason:     "not your turn",
	}

	data, err := json.Marshal(ar)
	if err != nil {
		t.Fatalf("marshal ActionRejectedMessage: %v", err)
	}

	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if v, ok := m["game_id"]; !ok || v != "g1" {
		t.Errorf("expected game_id g1, got %v", v)
	}
	if v, ok := m["action_type"]; !ok || v != "play_card" {
		t.Errorf("expected action_type play_card, got %v", v)
	}
	if v, ok := m["reason"]; !ok || v != "not your turn" {
		t.Errorf("expected reason 'not your turn', got %v", v)
	}
}

// ---------------------------------------------------------------------------
// Additional message type tests
// ---------------------------------------------------------------------------

func TestMatchmakingStartMessage_Parse(t *testing.T) {
	raw := `{"type":"matchmaking_start","data":{"deck_id":7}}`

	var msg ws.WSMessage
	if err := json.Unmarshal([]byte(raw), &msg); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}
	if msg.Type != "matchmaking_start" {
		t.Fatalf("expected type matchmaking_start, got %s", msg.Type)
	}

	var mm ws.MatchmakingStartMessage
	if err := json.Unmarshal(msg.Data, &mm); err != nil {
		t.Fatalf("unmarshal MatchmakingStartMessage: %v", err)
	}
	if mm.DeckID != 7 {
		t.Errorf("expected deck_id 7, got %d", mm.DeckID)
	}
}

func TestUseStampMessage_Parse(t *testing.T) {
	raw := `{"type":"use_stamp","data":{"game_id":"g1","stamp_no":5}}`

	var msg ws.WSMessage
	if err := json.Unmarshal([]byte(raw), &msg); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}

	var stamp ws.UseStampMessage
	if err := json.Unmarshal(msg.Data, &stamp); err != nil {
		t.Fatalf("unmarshal UseStampMessage: %v", err)
	}
	if stamp.GameID != "g1" {
		t.Errorf("expected game_id g1, got %s", stamp.GameID)
	}
	if stamp.StampNo != 5 {
		t.Errorf("expected stamp_no 5, got %d", stamp.StampNo)
	}
}

func TestGameOverMessage_Marshal(t *testing.T) {
	go_ := ws.GameOverMessage{
		GameID:    "g1",
		WinnerNum: 1,
		WinReason: "hp_zero",
	}

	data, err := json.Marshal(go_)
	if err != nil {
		t.Fatalf("marshal GameOverMessage: %v", err)
	}

	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if v, ok := m["game_id"]; !ok || v != "g1" {
		t.Errorf("expected game_id g1, got %v", v)
	}
	if v, ok := m["winner_num"]; !ok || v != float64(1) {
		t.Errorf("expected winner_num 1, got %v", v)
	}
	if v, ok := m["win_reason"]; !ok || v != "hp_zero" {
		t.Errorf("expected win_reason hp_zero, got %v", v)
	}
}

func TestMatchFoundMessage_Marshal(t *testing.T) {
	mf := ws.MatchFoundMessage{
		GameID:    "g1",
		Player1ID: "p1",
		Player2ID: "p2",
	}

	data, err := json.Marshal(mf)
	if err != nil {
		t.Fatalf("marshal MatchFoundMessage: %v", err)
	}

	var decoded ws.MatchFoundMessage
	if err := json.Unmarshal(data, &decoded); err != nil {
		t.Fatalf("unmarshal MatchFoundMessage: %v", err)
	}
	if decoded.GameID != "g1" {
		t.Errorf("expected game_id g1, got %s", decoded.GameID)
	}
	if decoded.Player1ID != "p1" {
		t.Errorf("expected player1_id p1, got %s", decoded.Player1ID)
	}
	if decoded.Player2ID != "p2" {
		t.Errorf("expected player2_id p2, got %s", decoded.Player2ID)
	}
}

func TestStampUsedMessage_Marshal(t *testing.T) {
	su := ws.StampUsedMessage{
		GameID:   "g1",
		PlayerID: "p1",
		StampNo:  3,
	}

	data, err := json.Marshal(su)
	if err != nil {
		t.Fatalf("marshal StampUsedMessage: %v", err)
	}

	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if v, ok := m["game_id"]; !ok || v != "g1" {
		t.Errorf("expected game_id g1, got %v", v)
	}
	if v, ok := m["player_id"]; !ok || v != "p1" {
		t.Errorf("expected player_id p1, got %v", v)
	}
	if v, ok := m["stamp_no"]; !ok || v != float64(3) {
		t.Errorf("expected stamp_no 3, got %v", v)
	}
}

func TestErrorMessage_Marshal(t *testing.T) {
	em := ws.ErrorMessage{
		Code:      "invalid_data",
		Message:   "invalid game_enter data",
		Retryable: false,
	}

	data, err := json.Marshal(em)
	if err != nil {
		t.Fatalf("marshal ErrorMessage: %v", err)
	}

	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if v, ok := m["error_code"]; !ok || v != "invalid_data" {
		t.Errorf("expected error_code invalid_data, got %v", v)
	}
	if v, ok := m["message"]; !ok || v != "invalid game_enter data" {
		t.Errorf("expected message 'invalid game_enter data', got %v", v)
	}
	if v, ok := m["retryable"]; !ok || v != false {
		t.Errorf("expected retryable false, got %v", v)
	}
}

func TestErrorMessage_Retryable_Marshal(t *testing.T) {
	em := ws.ErrorMessage{
		Code:      "matchmaking_error",
		Message:   "queue full",
		Retryable: true,
	}

	data, err := json.Marshal(em)
	if err != nil {
		t.Fatalf("marshal ErrorMessage: %v", err)
	}

	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if v, ok := m["retryable"]; !ok || v != true {
		t.Errorf("expected retryable true, got %v", v)
	}
}

// ---------------------------------------------------------------------------
// WSMessage envelope round-trip test
// ---------------------------------------------------------------------------

func TestWSMessage_RoundTrip(t *testing.T) {
	// Build a WSMessage with an embedded GameOverMessage payload.
	payload, err := json.Marshal(ws.GameOverMessage{
		GameID:    "g42",
		WinnerNum: 2,
		WinReason: "disconnect",
	})
	if err != nil {
		t.Fatalf("marshal payload: %v", err)
	}

	original := ws.WSMessage{
		Type: "game_over",
		Data: payload,
	}

	data, err := json.Marshal(original)
	if err != nil {
		t.Fatalf("marshal WSMessage: %v", err)
	}

	var decoded ws.WSMessage
	if err := json.Unmarshal(data, &decoded); err != nil {
		t.Fatalf("unmarshal WSMessage: %v", err)
	}
	if decoded.Type != "game_over" {
		t.Errorf("expected type game_over, got %s", decoded.Type)
	}

	var go_ ws.GameOverMessage
	if err := json.Unmarshal(decoded.Data, &go_); err != nil {
		t.Fatalf("unmarshal GameOverMessage from round-trip: %v", err)
	}
	if go_.GameID != "g42" {
		t.Errorf("expected game_id g42, got %s", go_.GameID)
	}
	if go_.WinnerNum != 2 {
		t.Errorf("expected winner_num 2, got %d", go_.WinnerNum)
	}
	if go_.WinReason != "disconnect" {
		t.Errorf("expected win_reason disconnect, got %s", go_.WinReason)
	}
}

// ---------------------------------------------------------------------------
// battle_start / turn_start action_performed message tests
// ---------------------------------------------------------------------------

func TestActionPerformedMessage_BattleStart_Marshal(t *testing.T) {
	actionData, _ := json.Marshal(map[string]interface{}{
		"my_name":        "Ken",
		"my_level":       24,
		"opponent_name":  "Smile Delivery",
		"opponent_level": 50,
		"match_type":     "npc",
	})

	ap := ws.ActionPerformedMessage{
		ActionType: "battle_start",
		ActionData: actionData,
		State:      json.RawMessage(`{"gameId":"g1","currentTurn":1}`),
	}

	data, err := json.Marshal(ap)
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	var m map[string]json.RawMessage
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}

	var actionType string
	if err := json.Unmarshal(m["action_type"], &actionType); err != nil {
		t.Fatalf("unmarshal action_type: %v", err)
	}
	if actionType != "battle_start" {
		t.Errorf("action_type = %s, want battle_start", actionType)
	}

	var ad struct {
		MyName       string `json:"my_name"`
		MyLevel      int64  `json:"my_level"`
		OpponentName string `json:"opponent_name"`
		OpponentLevel int64 `json:"opponent_level"`
		MatchType    string `json:"match_type"`
	}
	if err := json.Unmarshal(m["action_data"], &ad); err != nil {
		t.Fatalf("unmarshal action_data: %v", err)
	}
	if ad.MyName != "Ken" {
		t.Errorf("my_name = %s, want Ken", ad.MyName)
	}
	if ad.MyLevel != 24 {
		t.Errorf("my_level = %d, want 24", ad.MyLevel)
	}
	if ad.OpponentName != "Smile Delivery" {
		t.Errorf("opponent_name = %s, want Smile Delivery", ad.OpponentName)
	}
	if ad.OpponentLevel != 50 {
		t.Errorf("opponent_level = %d, want 50", ad.OpponentLevel)
	}
	if ad.MatchType != "npc" {
		t.Errorf("match_type = %s, want npc", ad.MatchType)
	}
}

func TestActionPerformedMessage_TurnStart_Marshal(t *testing.T) {
	actionData, _ := json.Marshal(map[string]interface{}{
		"turn":       3,
		"is_my_turn": true,
	})

	ap := ws.ActionPerformedMessage{
		ActionType: "turn_start",
		ActionData: actionData,
		State:      json.RawMessage(`{"gameId":"g1","currentTurn":3}`),
	}

	data, err := json.Marshal(ap)
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}

	var m map[string]json.RawMessage
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}

	var actionType string
	if err := json.Unmarshal(m["action_type"], &actionType); err != nil {
		t.Fatalf("unmarshal action_type: %v", err)
	}
	if actionType != "turn_start" {
		t.Errorf("action_type = %s, want turn_start", actionType)
	}

	var ad struct {
		Turn     int  `json:"turn"`
		IsMyTurn bool `json:"is_my_turn"`
	}
	if err := json.Unmarshal(m["action_data"], &ad); err != nil {
		t.Fatalf("unmarshal action_data: %v", err)
	}
	if ad.Turn != 3 {
		t.Errorf("turn = %d, want 3", ad.Turn)
	}
	if !ad.IsMyTurn {
		t.Error("expected is_my_turn = true")
	}
}

func TestWSMessage_EmptyData_OmitsField(t *testing.T) {
	msg := ws.WSMessage{Type: "pong"}

	data, err := json.Marshal(msg)
	if err != nil {
		t.Fatalf("marshal WSMessage: %v", err)
	}

	var m map[string]json.RawMessage
	if err := json.Unmarshal(data, &m); err != nil {
		t.Fatalf("unmarshal to map: %v", err)
	}

	if _, ok := m["data"]; ok {
		t.Error("expected 'data' field to be omitted for pong message, but it was present")
	}
}

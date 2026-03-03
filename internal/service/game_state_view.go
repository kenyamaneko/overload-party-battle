package service

import (
	"encoding/json"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
)

// ClientGameState is the info-hidden game state sent to a player.
type ClientGameState struct {
	GameID           string                   `json:"gameId"`
	CurrentTurn      int64                    `json:"currentTurn"`
	CurrentPhase     string                   `json:"currentPhase"`
	ActivePlayer     int64                    `json:"activePlayer"`
	IsMyTurn         bool                     `json:"isMyTurn"`
	MyView           *PlayerView              `json:"my"`
	OppView          *OpponentView            `json:"opponent"`
}

// PlayerView is the player's own full state (no hiding).
type PlayerView struct {
	PlayerNum        int64                    `json:"playerNum"`
	Budget           int64                    `json:"budget"`
	InsightPool      int64                    `json:"insightPool"`
	TimeBank         int64                    `json:"timeBank"`
	Field            *model.Field             `json:"field"`
	Hand             []model.HandCard         `json:"hand"`
	RepoCount        int                      `json:"repoCount"`
	TrashCount       int                      `json:"trashCount"`
	AvailableActions []engine.AvailableAction  `json:"available_actions,omitempty"`
}

// OpponentView is the opponent's state with info hiding applied.
type OpponentView struct {
	PlayerNum   int64          `json:"playerNum"`
	Budget      int64          `json:"budget"`
	InsightPool int64          `json:"insightPool"`
	TimeBank    int64          `json:"timeBank"`
	Field       *OpponentField `json:"field"`
	HandCount   int            `json:"handCount"`
	RepoCount   int            `json:"repoCount"`
	TrashCount  int            `json:"trashCount"`
}

// OpponentField shows the opponent's field with reactive cards hidden.
type OpponentField struct {
	Frontend [3]*model.ResourceInstance `json:"frontend"`
	Backend  [3]*model.ResourceInstance `json:"backend"`
	Support  [3]*HiddenSupportInstance  `json:"support"`
}

// HiddenSupportInstance hides face-down reactive card details.
type HiddenSupportInstance struct {
	InstanceID string `json:"instanceId"`
	CardID     *int64 `json:"cardId,omitempty"` // nil if face-down
	FaceDown   bool   `json:"faceDown"`
}

func buildClientGameState(state *model.GameState, game *model.Game, playerNum int64, cc *cache.CardCache, effects *effect.EffectRegistry) (*ClientGameState, error) {
	oppNum := model.OpponentNum(playerNum)

	// My view (full)
	myField, _ := state.GetField(playerNum)
	myHand, _ := state.GetHand(playerNum)
	myRepo, _ := state.GetRepository(playerNum)
	myTrash, _ := state.GetTrash(playerNum)
	budget := state.GetBudget(playerNum)
	insightPool := state.GetInsightPool(playerNum)

	myView := &PlayerView{
		PlayerNum:   playerNum,
		Budget:      budget,
		InsightPool: insightPool,
		TimeBank:    state.GetTimeBank(playerNum),
		Field:      myField,
		Hand:       myHand,
		RepoCount:  len(myRepo),
		TrashCount: len(myTrash),
	}

	// Opponent view (hidden)
	oppField, _ := state.GetField(oppNum)
	oppHand, _ := state.GetHand(oppNum)
	oppRepo, _ := state.GetRepository(oppNum)
	oppTrash, _ := state.GetTrash(oppNum)

	hiddenField := buildOpponentField(oppField)

	oppView := &OpponentView{
		PlayerNum:   oppNum,
		Budget:      state.GetBudget(oppNum),
		InsightPool: state.GetInsightPool(oppNum),
		TimeBank:    state.GetTimeBank(oppNum),
		Field:      hiddenField,
		HandCount:  len(oppHand),
		RepoCount:  len(oppRepo),
		TrashCount: len(oppTrash),
	}

	cgs := &ClientGameState{
		GameID:       game.GameID,
		CurrentTurn:  state.CurrentTurn,
		CurrentPhase: state.CurrentPhase,
		ActivePlayer: state.ActivePlayer,
		IsMyTurn:     state.ActivePlayer == playerNum,
		MyView:       myView,
		OppView:      oppView,
	}

	// Compute available actions for the active player only
	if state.ActivePlayer == playerNum && game.Status == model.GameStatusPlaying {
		myView.AvailableActions = engine.ComputeAvailableActions(
			state, game, playerNum,
			myField, oppField, myHand, budget, insightPool,
			cc, effects,
		)
	}

	return cgs, nil
}

func buildOpponentField(field *model.Field) *OpponentField {
	if field == nil {
		return &OpponentField{}
	}

	result := &OpponentField{}

	// Hide face-down resources in frontend/backend
	for i, res := range field.Frontend {
		result.Frontend[i] = hideResourceIfFaceDown(res)
	}
	for i, res := range field.Backend {
		result.Backend[i] = hideResourceIfFaceDown(res)
	}

	// Hide face-down reactive/deploying cards in support zone
	for i, sup := range field.Support {
		if sup == nil {
			continue
		}
		hidden := &HiddenSupportInstance{
			InstanceID: sup.InstanceID,
			FaceDown:   sup.FaceDown,
		}
		if !sup.FaceDown {
			hidden.CardID = &sup.CardID
		}
		result.Support[i] = hidden
	}

	return result
}

// hideResourceIfFaceDown returns the full resource if face-up, or a minimal
// hidden version (only InstanceID + FaceUp=false) if still deploying.
func hideResourceIfFaceDown(res *model.ResourceInstance) *model.ResourceInstance {
	if res == nil {
		return nil
	}
	if res.FaceUp {
		return res
	}
	return &model.ResourceInstance{
		InstanceID:         res.InstanceID,
		FaceUp:             false,
		DeployingTurnsLeft: res.DeployingTurnsLeft,
	}
}

// MarshalClientGameState serializes to JSON for WebSocket transmission.
func MarshalClientGameState(state *ClientGameState) json.RawMessage {
	data, _ := json.Marshal(state)
	return data
}

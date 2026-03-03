package engine

import "github.com/kenyamaneko/overload-party-common/model"

// CheckWinCondition examines the current game state for game-ending conditions.
// Returns (winnerPlayerNum, reason, gameOver).
// A winnerPlayerNum of 0 with gameOver=false means no winner yet.
// A winnerPlayerNum of 0 with gameOver=true means a draw.
func CheckWinCondition(state *model.GameState, game *model.Game) (int64, string, bool) {
	// Check budget zero for both players
	if state.GetBudget(1) <= 0 {
		return 2, model.WinReasonBudgetZero, true
	}
	if state.GetBudget(2) <= 0 {
		return 1, model.WinReasonBudgetZero, true
	}

	// Check system down (all frontend + backend resources destroyed)
	if isSystemDown(state, 1) {
		return 2, model.WinReasonSystemDown, true
	}
	if isSystemDown(state, 2) {
		return 1, model.WinReasonSystemDown, true
	}

	// Check turn limit (30 turns = 15 rounds, each player has 15 turns)
	if state.CurrentTurn >= 30 {
		budget1 := state.GetBudget(1)
		budget2 := state.GetBudget(2)
		if budget1 > budget2 {
			return 1, model.WinReasonTurnLimit, true
		} else if budget2 > budget1 {
			return 2, model.WinReasonTurnLimit, true
		} else {
			// Budget equal = draw
			return 0, model.WinReasonDraw, true
		}
	}

	// Check time bank
	if state.GetTimeBank(1) <= 0 {
		return 2, model.WinReasonTimeout, true
	}
	if state.GetTimeBank(2) <= 0 {
		return 1, model.WinReasonTimeout, true
	}

	return 0, "", false
}

// checkLaunchFailure returns true if the active player has failed to deploy
// any resource within 3 personal turns (Launch Failure loss condition).
// personalTurn = (currentTurn + 1) / 2 — works because turns alternate.
func checkLaunchFailure(state *model.GameState, playerNum int64) bool {
	field, err := state.GetField(playerNum)
	if err != nil {
		return false
	}
	if field.HasHadActiveResource {
		return false
	}
	personalTurn := (state.CurrentTurn + 1) / 2
	return personalTurn >= 3
}

// isSystemDown returns true if a player has no resources on the field
// (all frontend and backend slots are empty) AND has previously had an active resource.
// A player who has never deployed a resource cannot be "System Down".
func isSystemDown(state *model.GameState, playerNum int64) bool {
	field, err := state.GetField(playerNum)
	if err != nil {
		return false // Can't determine, assume not down
	}

	// No active resource has ever existed — cannot be System Down
	if !field.HasHadActiveResource {
		return false
	}

	for _, res := range field.Frontend {
		if res != nil && res.FaceUp {
			return false
		}
	}
	for _, res := range field.Backend {
		if res != nil && res.FaceUp {
			return false
		}
	}
	return true
}

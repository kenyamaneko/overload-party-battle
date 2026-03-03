package engine

import "github.com/kenyamaneko/overload-party-common/model"

// nextPhase returns the next phase in the sequence.
// After "end", the caller must switch active player and return to "draw".
func nextPhase(current string) string {
	switch current {
	case model.PhaseDraw:
		return model.PhaseMain
	case model.PhaseMain:
		return model.PhaseBattle
	case model.PhaseBattle:
		return model.PhaseEnd
	case model.PhaseEnd:
		return model.PhaseDraw // caller handles active player switch
	default:
		return model.PhaseDraw
	}
}

// isFirstTurn returns true on the very first turn of the game (turn 1).
// The first player skips battle phase and cannot distribute yield.
func isFirstTurn(turn int64) bool {
	return turn == 1
}

// allowedActionsForPhase returns which action types are valid in the given phase.
func allowedActionsForPhase(phase string) []string {
	switch phase {
	case model.PhaseMain:
		return []string{model.ActionPlayCard, model.ActionScaleUp, model.ActionDistributeYield, model.ActionActivateEffect, model.ActionMigrate, model.ActionEndPhase}
	case model.PhaseBattle:
		return []string{model.ActionAttack, model.ActionActivateEffect, model.ActionSetReactive, model.ActionEndPhase}
	case model.PhaseEnd:
		return []string{model.ActionDiscardHand}
	default:
		return nil
	}
}

// isActionAllowedInPhase checks if a specific action is valid in the current phase.
func isActionAllowedInPhase(phase, actionType string) bool {
	for _, a := range allowedActionsForPhase(phase) {
		if a == actionType {
			return true
		}
	}
	return false
}

package npc

// ================================================================
// NPC AI Parameters
//
// All tunable AI constants in one place. Adjust values here to
// change NPC behaviour without touching logic.
// ================================================================

// FactionParams holds per-faction AI tuning values.
type FactionParams struct {
	InstanceFamily string
}

// factionParamsTable maps faction name → tuning values.
// Used by FactionAI constructors.
var factionParamsTable = map[string]FactionParams{
	"SD":     {InstanceFamily: "M"},
	"Tenki":  {InstanceFamily: "R"},
	"Sugar":  {InstanceFamily: "C"},
	"Tuners": {InstanceFamily: "M"},
}

// --- Effect Evaluation Priorities ---
// Each value is the priority returned by evaluateCategory for a given effect category.

const (
	// Budget threshold below which BudgetGain priority is boosted.
	LowBudgetThreshold = int64(1500)

	PriBudgetGainHigh = 90
	PriBudgetGainLow  = 40
	PriBudgetPenalty   = 60
	PriInsightGain     = 50
	PriInsightAbsorb   = 65
	PriDrawHigh        = 80
	PriDrawLow         = 30
	PriSearchHigh      = 70
	PriSearchLow       = 25
	PriAoEDamage       = 75
	PriSingleDamage    = 60
	PriDebuff          = 55
	PriBuff            = 45
	PriHeal            = 50
	PriDeployFree      = 75
	PriRecoverCard     = 35
	PriRevealTrap      = 35
	PriDestroyPlatform = 70
)

// --- Incident Damage Reduction (effect package constant) ---
// The per-source reduction amount is defined in effect/generic.go as
// IncidentDamageReductionAmount = 200. We reference it here for documentation
// but do not duplicate the constant.

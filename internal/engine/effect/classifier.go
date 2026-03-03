package effect

// EffectCategory represents what an effect does, used for NPC decision-making.
type EffectCategory string

const (
	CatBudgetGain      EffectCategory = "budget_gain"
	CatBudgetPenalty   EffectCategory = "budget_penalty"
	CatInsightAbsorb   EffectCategory = "insight_absorb"
	CatInsightGain     EffectCategory = "insight_gain"
	CatSingleDamage    EffectCategory = "single_damage"
	CatAoEDamage       EffectCategory = "aoe_damage"
	CatBuff            EffectCategory = "buff"
	CatDebuff          EffectCategory = "debuff"
	CatHeal            EffectCategory = "heal"
	CatDraw            EffectCategory = "draw"
	CatSearch          EffectCategory = "search"
	CatDeployFree      EffectCategory = "deploy_free"
	CatRecoverCard     EffectCategory = "recover_card"
	CatRevealTrap      EffectCategory = "reveal_trap"
	CatDestroyPlatform EffectCategory = "destroy_platform"
	CatCancelAction    EffectCategory = "cancel_action"
	CatSurvive         EffectCategory = "survive"
)

// TargetType describes how the NPC should select targets for an effect.
type TargetType string

const (
	TargetNone   TargetType = "none"
	TargetChoice TargetType = "choice"
	TargetAllOpp TargetType = "all_opp"
	TargetSelf   TargetType = "self"
)

// EffectCondition represents a prerequisite for an effect to fire.
type EffectCondition struct {
	Type    string // "min_budget", "max_budget", "faction_count", "opponent_backend"
	Value   int64
	Faction string
}

// EffectInfo is the classification result for an effect pipeline.
type EffectInfo struct {
	Categories []EffectCategory
	TargetType TargetType
	TargetZone string // "frontend", "backend", "" (any)
	Conditions []EffectCondition
	HasBranch  bool
}

// HasCategory returns true if the info contains the given category.
func (info *EffectInfo) HasCategory(cat EffectCategory) bool {
	for _, c := range info.Categories {
		if c == cat {
			return true
		}
	}
	return false
}

func (info *EffectInfo) addCategory(cat EffectCategory) {
	if !info.HasCategory(cat) {
		info.Categories = append(info.Categories, cat)
	}
}

func (info *EffectInfo) mergeCategories(other *EffectInfo) {
	for _, cat := range other.Categories {
		info.addCategory(cat)
	}
	// Inherit target info if not yet set
	if info.TargetType == "" || info.TargetType == TargetNone {
		info.TargetType = other.TargetType
		info.TargetZone = other.TargetZone
	}
}

// ClassifyOps inspects a sequence of Ops and returns an EffectInfo describing
// the effect's categories, target requirements, and conditions.
// This enables the NPC to make decisions based on what an effect DOES,
// without referencing specific card numbers.
func ClassifyOps(ops []Op) *EffectInfo {
	info := &EffectInfo{}
	for _, op := range ops {
		classifyOp(info, op)
	}
	// Default to TargetNone if nothing set it
	if info.TargetType == "" {
		info.TargetType = TargetNone
	}
	return info
}

func classifyOp(info *EffectInfo, op Op) {
	switch o := op.(type) {
	// --- Budget ---
	case GainBudget:
		if o.Player == Self {
			info.addCategory(CatBudgetGain)
		}
	case LoseBudget:
		if o.Player == Opponent {
			info.addCategory(CatBudgetPenalty)
		}

	// --- Insight ---
	case GainInsight:
		info.addCategory(CatInsightGain)
	case AbsorbInsight:
		info.addCategory(CatInsightAbsorb)

	// --- Damage ---
	case DealDamage:
		classifyDamageTarget(info, o.Sel)
	case IncidentDamage:
		classifyDamageTarget(info, o.Sel)

	// --- Buff / Debuff ---
	case ApplyBuff:
		classifyBuffTarget(info, o.Sel)

	// --- Heal ---
	case HealDamage:
		info.addCategory(CatHeal)
		classifySelTarget(info, o.Sel)

	// --- Card Movement ---
	case DrawCards:
		info.addCategory(CatDraw)
	case SearchRepo:
		info.addCategory(CatSearch)
	case DeployFromHand:
		info.addCategory(CatDeployFree)
	case DeployFromRepo:
		info.addCategory(CatDeployFree)
	case DeployFromRepoSameCard:
		info.addCategory(CatDeployFree)
	case AddToHand:
		info.addCategory(CatRecoverCard)
	case TrashToHand:
		info.addCategory(CatRecoverCard)

	// --- Field ---
	case RevealTrap:
		info.addCategory(CatRevealTrap)
	case DestroyPlatform:
		info.addCategory(CatDestroyPlatform)

	// --- Reactive ---
	case SetCancelAction:
		info.addCategory(CatCancelAction)
	case SurviveDestruction:
		info.addCategory(CatSurvive)

	// --- Conditions ---
	case RequireBudget:
		info.Conditions = append(info.Conditions, EffectCondition{Type: "min_budget", Value: o.Min})
	case RequireMaxBudget:
		info.Conditions = append(info.Conditions, EffectCondition{Type: "max_budget", Value: o.Max})
	case RequireFactionCount:
		info.Conditions = append(info.Conditions, EffectCondition{
			Type: "faction_count", Value: int64(o.Min), Faction: o.Faction,
		})
	case RequireOpponentBackend:
		info.Conditions = append(info.Conditions, EffectCondition{Type: "opponent_backend"})

	// --- Branching ---
	case BranchOnChoice:
		info.HasBranch = true
		// Classify all branches — categories are the union
		for _, branchOps := range o.Branches {
			inner := ClassifyOps(branchOps)
			info.mergeCategories(inner)
		}
	case IfCondition:
		inner := ClassifyOps(o.Then)
		info.mergeCategories(inner)

	// --- CustomFn with tags ---
	case CustomFnTagged:
		for _, cat := range o.Categories {
			info.addCategory(cat)
		}
		if o.Target != "" {
			info.TargetType = o.Target
		}
		if o.Zone != "" {
			info.TargetZone = o.Zone
		}

	// --- Plain CustomFn: unclassifiable ---
	case CustomFn:
		// Cannot inspect — needs CustomFnTagged for classification
	}
}

// classifyDamageTarget determines single vs AoE and target type from the selector.
func classifyDamageTarget(info *EffectInfo, sel Selector) {
	switch s := sel.(type) {
	case ByChoiceSel:
		info.addCategory(CatSingleDamage)
		info.TargetType = TargetChoice
		info.TargetZone = s.Zone
	case AllOpponentSel:
		info.addCategory(CatAoEDamage)
		info.TargetType = TargetAllOpp
	case AllOwnSel:
		// Self-damage (e.g., cascade failure) — not useful for NPC to initiate
		info.addCategory(CatAoEDamage)
		info.TargetType = TargetSelf
	case SourceSel:
		// Self-damage on source
		info.TargetType = TargetSelf
	default:
		info.addCategory(CatSingleDamage)
	}
}

// classifyBuffTarget determines buff vs debuff from the selector.
func classifyBuffTarget(info *EffectInfo, sel Selector) {
	switch s := sel.(type) {
	case SourceSel:
		info.addCategory(CatBuff)
		info.TargetType = TargetSelf
	case AllOwnSel:
		info.addCategory(CatBuff)
		info.TargetType = TargetSelf
	case ByChoiceSel:
		if s.Owner == Opponent {
			info.addCategory(CatDebuff)
		} else {
			info.addCategory(CatBuff)
		}
		info.TargetType = TargetChoice
		info.TargetZone = s.Zone
	case AllOpponentSel:
		info.addCategory(CatDebuff)
		info.TargetType = TargetAllOpp
	default:
		info.addCategory(CatBuff)
	}
}

// classifySelTarget sets TargetType from a generic selector.
func classifySelTarget(info *EffectInfo, sel Selector) {
	switch s := sel.(type) {
	case ByChoiceSel:
		info.TargetType = TargetChoice
		info.TargetZone = s.Zone
	case AllOpponentSel:
		info.TargetType = TargetAllOpp
	case AllOwnSel:
		info.TargetType = TargetSelf
	case SourceSel:
		info.TargetType = TargetSelf
	}
}

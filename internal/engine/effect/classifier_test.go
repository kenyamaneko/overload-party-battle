package effect

import (
	"testing"
)

func TestClassifyOps_BudgetGain(t *testing.T) {
	info := ClassifyOps([]Op{
		GainBudget{Player: Self, Value: Static(500)},
	})

	assertCategories(t, info, CatBudgetGain)
	assertTargetType(t, info, TargetNone)
}

func TestClassifyOps_BudgetPenalty(t *testing.T) {
	info := ClassifyOps([]Op{
		LoseBudget{Player: Opponent, Value: Static(300)},
	})

	assertCategories(t, info, CatBudgetPenalty)
}

func TestClassifyOps_BudgetGainSelf_NotPenalty(t *testing.T) {
	// GainBudget for opponent should NOT produce CatBudgetGain
	info := ClassifyOps([]Op{
		GainBudget{Player: Opponent, Value: Static(500)},
	})
	if info.HasCategory(CatBudgetGain) {
		t.Error("GainBudget{Opponent} should not be classified as CatBudgetGain")
	}
}

func TestClassifyOps_InsightGain(t *testing.T) {
	info := ClassifyOps([]Op{
		GainInsight{Value: Static(100)},
	})

	assertCategories(t, info, CatInsightGain)
}

func TestClassifyOps_InsightAbsorb(t *testing.T) {
	info := ClassifyOps([]Op{
		AbsorbInsight{Value: Static(200)},
	})

	assertCategories(t, info, CatInsightAbsorb)
}

func TestClassifyOps_SingleDamage_ByChoice(t *testing.T) {
	info := ClassifyOps([]Op{
		IncidentDamage{
			Sel:   ByChoiceSel{Zone: "frontend", Owner: Opponent},
			Value: Static(500),
		},
	})

	assertCategories(t, info, CatSingleDamage)
	assertTargetType(t, info, TargetChoice)
	if info.TargetZone != "frontend" {
		t.Errorf("TargetZone = %q, want %q", info.TargetZone, "frontend")
	}
}

func TestClassifyOps_AoEDamage_AllOpponent(t *testing.T) {
	info := ClassifyOps([]Op{
		IncidentDamage{
			Sel:   AllOpponentSel{},
			Value: Static(300),
		},
	})

	assertCategories(t, info, CatAoEDamage)
	assertTargetType(t, info, TargetAllOpp)
}

func TestClassifyOps_SelfDamage_AllOwn(t *testing.T) {
	info := ClassifyOps([]Op{
		DealDamage{
			Sel:   AllOwnSel{},
			Value: Static(100),
		},
	})

	assertCategories(t, info, CatAoEDamage)
	assertTargetType(t, info, TargetSelf)
}

func TestClassifyOps_SelfDamage_Source(t *testing.T) {
	info := ClassifyOps([]Op{
		DealDamage{
			Sel:   SourceSel{},
			Value: Static(100),
		},
	})

	// SourceSel damage doesn't add a damage category (self-inflicted)
	assertTargetType(t, info, TargetSelf)
}

func TestClassifyOps_BuffSelf(t *testing.T) {
	info := ClassifyOps([]Op{
		ApplyBuff{Sel: SourceSel{}, EffectType: "tp", Value: Static(200)},
	})

	assertCategories(t, info, CatBuff)
	assertTargetType(t, info, TargetSelf)
}

func TestClassifyOps_BuffAllOwn(t *testing.T) {
	info := ClassifyOps([]Op{
		ApplyBuff{Sel: AllOwnSel{Zone: "frontend"}, EffectType: "av", Value: Static(100)},
	})

	assertCategories(t, info, CatBuff)
	assertTargetType(t, info, TargetSelf)
}

func TestClassifyOps_DebuffOpponent_ByChoice(t *testing.T) {
	info := ClassifyOps([]Op{
		ApplyBuff{
			Sel:        ByChoiceSel{Zone: "frontend", Owner: Opponent},
			EffectType: "tp",
			Value:      Static(-200),
		},
	})

	assertCategories(t, info, CatDebuff)
	assertTargetType(t, info, TargetChoice)
	if info.TargetZone != "frontend" {
		t.Errorf("TargetZone = %q, want %q", info.TargetZone, "frontend")
	}
}

func TestClassifyOps_DebuffOpponent_AllOpp(t *testing.T) {
	info := ClassifyOps([]Op{
		ApplyBuff{Sel: AllOpponentSel{}, EffectType: "tp", Value: Static(-100)},
	})

	assertCategories(t, info, CatDebuff)
	assertTargetType(t, info, TargetAllOpp)
}

func TestClassifyOps_Heal(t *testing.T) {
	info := ClassifyOps([]Op{
		HealDamage{Sel: ByChoiceSel{Owner: Self}, Value: Static(300)},
	})

	assertCategories(t, info, CatHeal)
	assertTargetType(t, info, TargetChoice)
}

func TestClassifyOps_Draw(t *testing.T) {
	info := ClassifyOps([]Op{
		DrawCards{Count: 2},
	})

	assertCategories(t, info, CatDraw)
}

func TestClassifyOps_Search(t *testing.T) {
	info := ClassifyOps([]Op{
		SearchRepo{},
	})

	assertCategories(t, info, CatSearch)
}

func TestClassifyOps_DeployFree_FromHand(t *testing.T) {
	info := ClassifyOps([]Op{
		DeployFromHand{},
	})
	assertCategories(t, info, CatDeployFree)
}

func TestClassifyOps_DeployFree_FromRepo(t *testing.T) {
	info := ClassifyOps([]Op{
		DeployFromRepo{},
	})
	assertCategories(t, info, CatDeployFree)
}

func TestClassifyOps_DeployFree_SameCard(t *testing.T) {
	info := ClassifyOps([]Op{
		DeployFromRepoSameCard{},
	})
	assertCategories(t, info, CatDeployFree)
}

func TestClassifyOps_RecoverCard_AddToHand(t *testing.T) {
	info := ClassifyOps([]Op{
		AddToHand{},
	})
	assertCategories(t, info, CatRecoverCard)
}

func TestClassifyOps_RecoverCard_TrashToHand(t *testing.T) {
	info := ClassifyOps([]Op{
		TrashToHand{},
	})
	assertCategories(t, info, CatRecoverCard)
}

func TestClassifyOps_RevealTrap(t *testing.T) {
	info := ClassifyOps([]Op{
		RevealTrap{},
	})
	assertCategories(t, info, CatRevealTrap)
}

func TestClassifyOps_DestroyPlatform(t *testing.T) {
	info := ClassifyOps([]Op{
		DestroyPlatform{},
	})
	assertCategories(t, info, CatDestroyPlatform)
}

func TestClassifyOps_CancelAction(t *testing.T) {
	info := ClassifyOps([]Op{
		SetCancelAction{},
	})
	assertCategories(t, info, CatCancelAction)
}

func TestClassifyOps_Survive(t *testing.T) {
	info := ClassifyOps([]Op{
		SurviveDestruction{SurviveAV: 100},
	})
	assertCategories(t, info, CatSurvive)
}

func TestClassifyOps_MultiCategory(t *testing.T) {
	// A card that deals single damage AND gains budget
	info := ClassifyOps([]Op{
		IncidentDamage{
			Sel:   ByChoiceSel{Zone: "frontend", Owner: Opponent},
			Value: Static(400),
		},
		GainBudget{Player: Self, Value: Static(500)},
	})

	assertCategories(t, info, CatSingleDamage, CatBudgetGain)
	assertTargetType(t, info, TargetChoice)
}

func TestClassifyOps_WithConditions_Budget(t *testing.T) {
	info := ClassifyOps([]Op{
		RequireBudget{Min: 1000},
		GainInsight{Value: Static(300)},
	})

	assertCategories(t, info, CatInsightGain)
	if len(info.Conditions) != 1 {
		t.Fatalf("Conditions len = %d, want 1", len(info.Conditions))
	}
	if info.Conditions[0].Type != "min_budget" || info.Conditions[0].Value != 1000 {
		t.Errorf("Condition = %+v, want min_budget=1000", info.Conditions[0])
	}
}

func TestClassifyOps_WithConditions_MaxBudget(t *testing.T) {
	info := ClassifyOps([]Op{
		RequireMaxBudget{Max: 500},
		DrawCards{Count: 3},
	})

	assertCategories(t, info, CatDraw)
	if len(info.Conditions) != 1 {
		t.Fatalf("Conditions len = %d, want 1", len(info.Conditions))
	}
	if info.Conditions[0].Type != "max_budget" || info.Conditions[0].Value != 500 {
		t.Errorf("Condition = %+v, want max_budget=500", info.Conditions[0])
	}
}

func TestClassifyOps_WithConditions_FactionCount(t *testing.T) {
	info := ClassifyOps([]Op{
		RequireFactionCount{Faction: "Smile", Min: 2},
		GainBudget{Player: Self, Value: Static(800)},
	})

	assertCategories(t, info, CatBudgetGain)
	if len(info.Conditions) != 1 {
		t.Fatalf("Conditions len = %d, want 1", len(info.Conditions))
	}
	cond := info.Conditions[0]
	if cond.Type != "faction_count" || cond.Value != 2 || cond.Faction != "Smile" {
		t.Errorf("Condition = %+v, want faction_count=2/Smile", cond)
	}
}

func TestClassifyOps_WithConditions_OpponentBackend(t *testing.T) {
	info := ClassifyOps([]Op{
		RequireOpponentBackend{},
		AbsorbInsight{Value: Static(200)},
	})

	assertCategories(t, info, CatInsightAbsorb)
	if len(info.Conditions) != 1 {
		t.Fatalf("Conditions len = %d, want 1", len(info.Conditions))
	}
	if info.Conditions[0].Type != "opponent_backend" {
		t.Errorf("Condition type = %q, want opponent_backend", info.Conditions[0].Type)
	}
}

func TestClassifyOps_BranchOnChoice(t *testing.T) {
	info := ClassifyOps([]Op{
		BranchOnChoice{
			Branches: map[string][]Op{
				"damage": {IncidentDamage{Sel: AllOpponentSel{}, Value: Static(300)}},
				"heal":   {HealDamage{Sel: SourceSel{}, Value: Static(0)}},
			},
		},
	})

	if !info.HasBranch {
		t.Error("HasBranch should be true")
	}
	// Union of categories from all branches
	assertCategories(t, info, CatAoEDamage, CatHeal)
}

func TestClassifyOps_IfCondition(t *testing.T) {
	info := ClassifyOps([]Op{
		IfCondition{
			Cond: func(octx *OpContext) bool { return true },
			Then: []Op{DrawCards{Count: 1}},
		},
	})

	assertCategories(t, info, CatDraw)
}

func TestClassifyOps_CustomFnTagged(t *testing.T) {
	info := ClassifyOps([]Op{
		CustomFnTagged{
			Fn:         func(octx *OpContext) error { return nil },
			Categories: []EffectCategory{CatSingleDamage, CatBudgetPenalty},
			Target:     TargetChoice,
			Zone:       "backend",
		},
	})

	assertCategories(t, info, CatSingleDamage, CatBudgetPenalty)
	assertTargetType(t, info, TargetChoice)
	if info.TargetZone != "backend" {
		t.Errorf("TargetZone = %q, want %q", info.TargetZone, "backend")
	}
}

func TestClassifyOps_CustomFnUnclassifiable(t *testing.T) {
	info := ClassifyOps([]Op{
		CustomFn{Fn: func(octx *OpContext) error { return nil }},
	})

	if len(info.Categories) != 0 {
		t.Errorf("Plain CustomFn should have no categories, got %v", info.Categories)
	}
}

func TestClassifyOps_EmptyOps(t *testing.T) {
	info := ClassifyOps([]Op{})

	if len(info.Categories) != 0 {
		t.Errorf("Empty ops should have no categories, got %v", info.Categories)
	}
	assertTargetType(t, info, TargetNone)
}

func TestClassifyOps_DuplicateCategories(t *testing.T) {
	// Two GainBudget ops should only produce one CatBudgetGain
	info := ClassifyOps([]Op{
		GainBudget{Player: Self, Value: Static(100)},
		GainBudget{Player: Self, Value: Static(200)},
	})

	if len(info.Categories) != 1 {
		t.Errorf("Expected 1 category (deduplicated), got %d: %v", len(info.Categories), info.Categories)
	}
	assertCategories(t, info, CatBudgetGain)
}

func TestClassifyOps_ComplexPipeline(t *testing.T) {
	// Simulate a complex card: require budget + single damage + budget gain + draw
	info := ClassifyOps([]Op{
		RequireBudget{Min: 500},
		IncidentDamage{
			Sel:   ByChoiceSel{Zone: "backend", Owner: Opponent},
			Value: Static(600),
		},
		GainBudget{Player: Self, Value: Static(300)},
		DrawCards{Count: 1},
	})

	assertCategories(t, info, CatSingleDamage, CatBudgetGain, CatDraw)
	assertTargetType(t, info, TargetChoice)
	if info.TargetZone != "backend" {
		t.Errorf("TargetZone = %q, want %q", info.TargetZone, "backend")
	}
	if len(info.Conditions) != 1 {
		t.Fatalf("Conditions len = %d, want 1", len(info.Conditions))
	}
}

func TestEffectInfo_HasCategory(t *testing.T) {
	info := &EffectInfo{
		Categories: []EffectCategory{CatBuff, CatDraw},
	}

	if !info.HasCategory(CatBuff) {
		t.Error("should have CatBuff")
	}
	if !info.HasCategory(CatDraw) {
		t.Error("should have CatDraw")
	}
	if info.HasCategory(CatDebuff) {
		t.Error("should not have CatDebuff")
	}
}

func TestEffectInfo_MergeCategories(t *testing.T) {
	a := &EffectInfo{
		Categories: []EffectCategory{CatBuff},
		TargetType: TargetSelf,
	}
	b := &EffectInfo{
		Categories: []EffectCategory{CatDraw, CatBuff},
		TargetType: TargetChoice,
		TargetZone: "frontend",
	}

	a.mergeCategories(b)

	// Categories should be the union (no duplicates)
	assertCategories(t, a, CatBuff, CatDraw)
	// TargetType should NOT be overwritten (a already had TargetSelf)
	assertTargetType(t, a, TargetSelf)
}

func TestEffectInfo_MergeCategories_InheritsTarget(t *testing.T) {
	a := &EffectInfo{
		Categories: []EffectCategory{CatBudgetGain},
		TargetType: TargetNone,
	}
	b := &EffectInfo{
		Categories: []EffectCategory{CatSingleDamage},
		TargetType: TargetChoice,
		TargetZone: "backend",
	}

	a.mergeCategories(b)

	assertCategories(t, a, CatBudgetGain, CatSingleDamage)
	// TargetType should be inherited since a was TargetNone
	assertTargetType(t, a, TargetChoice)
	if a.TargetZone != "backend" {
		t.Errorf("TargetZone = %q, want %q", a.TargetZone, "backend")
	}
}

// --- Test Helpers ---

func assertCategories(t *testing.T, info *EffectInfo, expected ...EffectCategory) {
	t.Helper()
	for _, cat := range expected {
		if !info.HasCategory(cat) {
			t.Errorf("expected category %q not found in %v", cat, info.Categories)
		}
	}
}

func assertTargetType(t *testing.T, info *EffectInfo, expected TargetType) {
	t.Helper()
	if info.TargetType != expected {
		t.Errorf("TargetType = %q, want %q", info.TargetType, expected)
	}
}

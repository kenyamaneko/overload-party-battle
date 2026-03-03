package effect

import (
	"encoding/json"
	"testing"
)

// --- ParseEffects ---

func TestParseEffects_Nil(t *testing.T) {
	defs, err := ParseEffects(nil)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if defs != nil {
		t.Errorf("expected nil, got %v", defs)
	}
}

func TestParseEffects_NullString(t *testing.T) {
	defs, err := ParseEffects(json.RawMessage(`null`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if defs != nil {
		t.Errorf("expected nil, got %v", defs)
	}
}

func TestParseEffects_SingleActivate(t *testing.T) {
	raw := json.RawMessage(`[{"trigger":"activate","ops":[{"op":"gain_budget","player":"self","value":500}]}]`)
	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(defs) != 1 {
		t.Fatalf("expected 1 def, got %d", len(defs))
	}
	if defs[0].Trigger != "activate" {
		t.Errorf("trigger = %s, want activate", defs[0].Trigger)
	}
	if len(defs[0].Ops) != 1 {
		t.Errorf("ops count = %d, want 1", len(defs[0].Ops))
	}
}

func TestParseEffects_MultipleTriggers(t *testing.T) {
	raw := json.RawMessage(`[
		{"trigger":"on_attack","ops":[{"op":"absorb_insight","value":600}]},
		{"trigger":"on_destroy","ops":[{"op":"deal_damage","selector":{"sel":"all_own","zone":"backend"},"value":400}]}
	]`)
	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(defs) != 2 {
		t.Fatalf("expected 2 defs, got %d", len(defs))
	}
	if defs[0].Trigger != "on_attack" {
		t.Errorf("defs[0].trigger = %s, want on_attack", defs[0].Trigger)
	}
	if defs[1].Trigger != "on_destroy" {
		t.Errorf("defs[1].trigger = %s, want on_destroy", defs[1].Trigger)
	}
}

func TestParseEffects_Passive(t *testing.T) {
	raw := json.RawMessage(`[{
		"trigger":"passive",
		"passive_type":"tp_bonus",
		"scope":"platform",
		"target_zone":"frontend",
		"target_faction":"SD",
		"value":200
	}]`)
	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(defs) != 1 {
		t.Fatalf("expected 1 def, got %d", len(defs))
	}
	d := defs[0]
	if d.Trigger != "passive" {
		t.Errorf("trigger = %s, want passive", d.Trigger)
	}
	if d.PassiveType != "tp_bonus" {
		t.Errorf("passive_type = %s, want tp_bonus", d.PassiveType)
	}
	if d.Scope != "platform" {
		t.Errorf("scope = %s, want platform", d.Scope)
	}
	if d.TargetZone != "frontend" {
		t.Errorf("target_zone = %s, want frontend", d.TargetZone)
	}
	if d.TargetFaction != "SD" {
		t.Errorf("target_faction = %s, want SD", d.TargetFaction)
	}
}

func TestParseEffects_InvalidJSON(t *testing.T) {
	_, err := ParseEffects(json.RawMessage(`{not valid`))
	if err == nil {
		t.Error("expected error for invalid JSON")
	}
}

// --- ParseOp: Budget ---

func TestParseOp_GainBudget(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"gain_budget","player":"self","value":500}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	gb, ok := op.(GainBudget)
	if !ok {
		t.Fatalf("expected GainBudget, got %T", op)
	}
	if gb.Player != Self {
		t.Errorf("player = %v, want Self", gb.Player)
	}
}

func TestParseOp_LoseBudget(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"lose_budget","player":"opponent","value":300}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	lb, ok := op.(LoseBudget)
	if !ok {
		t.Fatalf("expected LoseBudget, got %T", op)
	}
	if lb.Player != Opponent {
		t.Errorf("player = %v, want Opponent", lb.Player)
	}
}

// --- ParseOp: Insight ---

func TestParseOp_GainInsight(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"gain_insight","value":400}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(GainInsight); !ok {
		t.Fatalf("expected GainInsight, got %T", op)
	}
}

func TestParseOp_AbsorbInsight(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"absorb_insight","value":600}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(AbsorbInsight); !ok {
		t.Fatalf("expected AbsorbInsight, got %T", op)
	}
}

// --- ParseOp: Damage ---

func TestParseOp_DealDamage(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"deal_damage","selector":{"sel":"target"},"value":500}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	dd, ok := op.(DealDamage)
	if !ok {
		t.Fatalf("expected DealDamage, got %T", op)
	}
	if _, ok := dd.Sel.(TargetSel); !ok {
		t.Errorf("selector = %T, want TargetSel", dd.Sel)
	}
}

func TestParseOp_IncidentDamage(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{
		"op":"incident_damage",
		"selector":{"sel":"by_choice","zone":"frontend","owner":"opponent"},
		"value":500,
		"budget_penalty":200
	}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	id, ok := op.(IncidentDamage)
	if !ok {
		t.Fatalf("expected IncidentDamage, got %T", op)
	}
	bcs, ok := id.Sel.(ByChoiceSel)
	if !ok {
		t.Fatalf("selector = %T, want ByChoiceSel", id.Sel)
	}
	if bcs.Zone != "frontend" {
		t.Errorf("zone = %s, want frontend", bcs.Zone)
	}
	if bcs.Owner != Opponent {
		t.Errorf("owner = %v, want Opponent", bcs.Owner)
	}
}

func TestParseOp_IncidentDamage_NoBudgetPenalty(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{
		"op":"incident_damage",
		"selector":{"sel":"source"},
		"value":300
	}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	id, ok := op.(IncidentDamage)
	if !ok {
		t.Fatalf("expected IncidentDamage, got %T", op)
	}
	if id.BudgetPenalty != nil {
		t.Error("expected nil BudgetPenalty")
	}
}

func TestParseOp_HealDamage(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"heal_damage","selector":{"sel":"source"},"value":400}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(HealDamage); !ok {
		t.Fatalf("expected HealDamage, got %T", op)
	}
}

// --- ParseOp: Buff ---

func TestParseOp_ApplyBuff(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{
		"op":"apply_buff",
		"selector":{"sel":"source"},
		"effect_type":"buff_tp",
		"value":200,
		"duration":"permanent",
		"source_id":"test"
	}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	ab, ok := op.(ApplyBuff)
	if !ok {
		t.Fatalf("expected ApplyBuff, got %T", op)
	}
	if ab.EffectType != "buff_tp" {
		t.Errorf("effect_type = %s, want buff_tp", ab.EffectType)
	}
	if ab.Duration != "permanent" {
		t.Errorf("duration = %s, want permanent", ab.Duration)
	}
	if ab.SourceID != "test" {
		t.Errorf("source_id = %s, want test", ab.SourceID)
	}
}

// --- ParseOp: Card Move ---

func TestParseOp_SearchRepo(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"search_repo","faction":"SD"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	sr, ok := op.(SearchRepo)
	if !ok {
		t.Fatalf("expected SearchRepo, got %T", op)
	}
	if sr.Faction != "SD" {
		t.Errorf("faction = %s, want SD", sr.Faction)
	}
}

func TestParseOp_DeployFromRepo(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{
		"op":"deploy_from_repo",
		"filter":{"faction":"SD","card_type":"data"},
		"override_av":200
	}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	dfr, ok := op.(DeployFromRepo)
	if !ok {
		t.Fatalf("expected DeployFromRepo, got %T", op)
	}
	if dfr.OverrideAV != 200 {
		t.Errorf("override_av = %d, want 200", dfr.OverrideAV)
	}
	if dfr.Filter == nil {
		t.Error("expected non-nil filter")
	}
}

func TestParseOp_DeployFromHand(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"deploy_from_hand"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(DeployFromHand); !ok {
		t.Fatalf("expected DeployFromHand, got %T", op)
	}
}

func TestParseOp_DeploySameCard(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"deploy_same_card","override_av":200}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	dsc, ok := op.(DeployFromRepoSameCard)
	if !ok {
		t.Fatalf("expected DeployFromRepoSameCard, got %T", op)
	}
	if dsc.OverrideAV != 200 {
		t.Errorf("override_av = %d, want 200", dsc.OverrideAV)
	}
}

func TestParseOp_AddToHand(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"add_to_hand","card_no":42}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(AddToHand); !ok {
		t.Fatalf("expected AddToHand, got %T", op)
	}
}

func TestParseOp_TrashToHand(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"trash_to_hand"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(TrashToHand); !ok {
		t.Fatalf("expected TrashToHand, got %T", op)
	}
}

func TestParseOp_DrawCards(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"draw_cards","count":2,"tuners_trash":true,"keep_one":true}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	dc, ok := op.(DrawCards)
	if !ok {
		t.Fatalf("expected DrawCards, got %T", op)
	}
	if dc.Count != 2 {
		t.Errorf("count = %d, want 2", dc.Count)
	}
	if !dc.TunersTrash {
		t.Error("expected tuners_trash = true")
	}
	if !dc.KeepOne {
		t.Error("expected keep_one = true")
	}
}

// --- ParseOp: Field ---

func TestParseOp_RevealTrap(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"reveal_trap"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(RevealTrap); !ok {
		t.Fatalf("expected RevealTrap, got %T", op)
	}
}

func TestParseOp_ScaleToRank(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"scale_to_rank","rank":"medium"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	str, ok := op.(ScaleToRank)
	if !ok {
		t.Fatalf("expected ScaleToRank, got %T", op)
	}
	if str.Rank != "medium" {
		t.Errorf("rank = %s, want medium", str.Rank)
	}
}

func TestParseOp_DestroyPlatform(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"destroy_platform"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(DestroyPlatform); !ok {
		t.Fatalf("expected DestroyPlatform, got %T", op)
	}
}

func TestParseOp_DestroyCheck(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"destroy_check","player":"opponent"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	dc, ok := op.(DestroyCheck)
	if !ok {
		t.Fatalf("expected DestroyCheck, got %T", op)
	}
	if dc.Player != Opponent {
		t.Errorf("player = %v, want Opponent", dc.Player)
	}
}

// --- ParseOp: Control ---

func TestParseOp_CancelAction(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"cancel_action"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(SetCancelAction); !ok {
		t.Fatalf("expected SetCancelAction, got %T", op)
	}
}

func TestParseOp_RequireBudget(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"require_budget","min":400}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	rb, ok := op.(RequireBudget)
	if !ok {
		t.Fatalf("expected RequireBudget, got %T", op)
	}
	if rb.Min != 400 {
		t.Errorf("min = %d, want 400", rb.Min)
	}
}

func TestParseOp_RequireMaxBudget(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"require_max_budget","max":1000}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	rmb, ok := op.(RequireMaxBudget)
	if !ok {
		t.Fatalf("expected RequireMaxBudget, got %T", op)
	}
	if rmb.Max != 1000 {
		t.Errorf("max = %d, want 1000", rmb.Max)
	}
}

func TestParseOp_RequireFactionCount(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"require_faction_count","faction":"SD","min":3}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	rfc, ok := op.(RequireFactionCount)
	if !ok {
		t.Fatalf("expected RequireFactionCount, got %T", op)
	}
	if rfc.Faction != "SD" {
		t.Errorf("faction = %s, want SD", rfc.Faction)
	}
	if rfc.Min != 3 {
		t.Errorf("min = %d, want 3", rfc.Min)
	}
}

func TestParseOp_RequireOpponentBackend(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"require_opponent_backend"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(RequireOpponentBackend); !ok {
		t.Fatalf("expected RequireOpponentBackend, got %T", op)
	}
}

func TestParseOp_GuardFaction(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"guard_faction","faction":"SD","card_type":"data"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	gf, ok := op.(GuardFaction)
	if !ok {
		t.Fatalf("expected GuardFaction, got %T", op)
	}
	if gf.Faction != "SD" {
		t.Errorf("faction = %s, want SD", gf.Faction)
	}
	if gf.CardType != "data" {
		t.Errorf("card_type = %s, want data", gf.CardType)
	}
}

func TestParseOp_GuardNotSelf(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"guard_not_self"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := op.(GuardNotSelf); !ok {
		t.Fatalf("expected GuardNotSelf, got %T", op)
	}
}

func TestParseOp_GuardTargetAV(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"guard_target_av","max_av":400}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	gta, ok := op.(GuardTargetAV)
	if !ok {
		t.Fatalf("expected GuardTargetAV, got %T", op)
	}
	if gta.MaxAV != 400 {
		t.Errorf("max_av = %d, want 400", gta.MaxAV)
	}
}

func TestParseOp_SurviveDestruction(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{"op":"survive_destruction","survive_av":200}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	sd, ok := op.(SurviveDestruction)
	if !ok {
		t.Fatalf("expected SurviveDestruction, got %T", op)
	}
	if sd.SurviveAV != 200 {
		t.Errorf("survive_av = %d, want 200", sd.SurviveAV)
	}
}

// --- ParseOp: Branch ---

func TestParseOp_Branch(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{
		"op":"branch",
		"branches":{
			"use":[{"op":"gain_budget","player":"self","value":400}],
			"skip":[{"op":"gain_insight","value":200}]
		}
	}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	boc, ok := op.(BranchOnChoice)
	if !ok {
		t.Fatalf("expected BranchOnChoice, got %T", op)
	}
	if len(boc.Branches) != 2 {
		t.Errorf("branches count = %d, want 2", len(boc.Branches))
	}
	if len(boc.Branches["use"]) != 1 {
		t.Errorf("use branch ops = %d, want 1", len(boc.Branches["use"]))
	}
	if len(boc.Branches["skip"]) != 1 {
		t.Errorf("skip branch ops = %d, want 1", len(boc.Branches["skip"]))
	}
}

func TestParseOp_If(t *testing.T) {
	op, err := ParseOp(json.RawMessage(`{
		"op":"if",
		"condition":{"cond":"has_security_platform"},
		"then":[{"op":"lose_budget","player":"opponent","value":400}]
	}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	ic, ok := op.(IfCondition)
	if !ok {
		t.Fatalf("expected IfCondition, got %T", op)
	}
	if ic.Cond == nil {
		t.Error("expected non-nil condition")
	}
	if len(ic.Then) != 1 {
		t.Errorf("then ops = %d, want 1", len(ic.Then))
	}
}

func TestParseOp_Custom_Unknown(t *testing.T) {
	_, err := ParseOp(json.RawMessage(`{"op":"custom","fn":"nonexistent"}`))
	if err == nil {
		t.Error("expected error for unknown custom function")
	}
}

func TestParseOp_UnknownOp(t *testing.T) {
	_, err := ParseOp(json.RawMessage(`{"op":"unknown_op"}`))
	if err == nil {
		t.Error("expected error for unknown op")
	}
}

func TestParseOp_InvalidJSON(t *testing.T) {
	_, err := ParseOp(json.RawMessage(`{not valid}`))
	if err == nil {
		t.Error("expected error for invalid JSON")
	}
}

// --- ParseValue ---

func TestParseValue_Static(t *testing.T) {
	val := ParseValue(json.RawMessage(`500`))
	result, err := val.Resolve(&OpContext{})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if result != 500 {
		t.Errorf("static value = %d, want 500", result)
	}
}

func TestParseValue_Null(t *testing.T) {
	val := ParseValue(json.RawMessage(`null`))
	result, err := val.Resolve(&OpContext{})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if result != 0 {
		t.Errorf("null value = %d, want 0", result)
	}
}

func TestParseValue_Empty(t *testing.T) {
	val := ParseValue(nil)
	result, err := val.Resolve(&OpContext{})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if result != 0 {
		t.Errorf("empty value = %d, want 0", result)
	}
}

func TestParseValue_RefSourceYield(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"source_yield"}`))
	if val == nil {
		t.Fatal("expected non-nil Amount")
	}
}

func TestParseValue_RefHalfMaxAV(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"half_max_av"}`))
	if val == nil {
		t.Fatal("expected non-nil Amount")
	}
}

func TestParseValue_RefTargetCardID(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"target_card_id"}`))
	if val == nil {
		t.Fatal("expected non-nil Amount")
	}
}

func TestParseValue_RefSLAPenalty(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"sla_penalty"}`))
	if val == nil {
		t.Fatal("expected non-nil Amount")
	}
}

func TestParseValue_RefTargetTP(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"target_tp"}`))
	if val == nil {
		t.Fatal("expected non-nil Amount")
	}
}

func TestParseValue_RefBackendScaled(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"backend_scaled","base":400,"per_backend":200,"max_bonus":600}`))
	if val == nil {
		t.Fatal("expected non-nil Amount")
	}
}

func TestParseValue_UnknownRef(t *testing.T) {
	val := ParseValue(json.RawMessage(`{"ref":"nonexistent"}`))
	result, err := val.Resolve(&OpContext{})
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if result != 0 {
		t.Errorf("unknown ref value = %d, want 0 (fallback)", result)
	}
}

// --- ParseSelector ---

func TestParseSelector_Source(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`{"sel":"source"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := sel.(SourceSel); !ok {
		t.Errorf("expected SourceSel, got %T", sel)
	}
}

func TestParseSelector_Target(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`{"sel":"target"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := sel.(TargetSel); !ok {
		t.Errorf("expected TargetSel, got %T", sel)
	}
}

func TestParseSelector_ByChoice(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`{"sel":"by_choice","zone":"frontend","owner":"opponent","faction":"SD"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	bcs, ok := sel.(ByChoiceSel)
	if !ok {
		t.Fatalf("expected ByChoiceSel, got %T", sel)
	}
	if bcs.Zone != "frontend" {
		t.Errorf("zone = %s, want frontend", bcs.Zone)
	}
	if bcs.Owner != Opponent {
		t.Errorf("owner = %v, want Opponent", bcs.Owner)
	}
	if bcs.Faction != "SD" {
		t.Errorf("faction = %s, want SD", bcs.Faction)
	}
}

func TestParseSelector_ByChoice_WithCardType(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`{"sel":"by_choice","zone":"backend","owner":"self","card_type":"Database"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	bcs, ok := sel.(ByChoiceSel)
	if !ok {
		t.Fatalf("expected ByChoiceSel, got %T", sel)
	}
	if bcs.CardType != "Database" {
		t.Errorf("card_type = %s, want Database", bcs.CardType)
	}
}

func TestParseSelector_AllOwn(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`{"sel":"all_own","zone":"frontend","faction":"SD"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	aos, ok := sel.(AllOwnSel)
	if !ok {
		t.Fatalf("expected AllOwnSel, got %T", sel)
	}
	if aos.Zone != "frontend" {
		t.Errorf("zone = %s, want frontend", aos.Zone)
	}
	if aos.Faction != "SD" {
		t.Errorf("faction = %s, want SD", aos.Faction)
	}
}

func TestParseSelector_AllOpponent(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`{"sel":"all_opponent","zone":"backend"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	aos, ok := sel.(AllOpponentSel)
	if !ok {
		t.Fatalf("expected AllOpponentSel, got %T", sel)
	}
	if aos.Zone != "backend" {
		t.Errorf("zone = %s, want backend", aos.Zone)
	}
}

func TestParseSelector_Null(t *testing.T) {
	sel, err := ParseSelector(json.RawMessage(`null`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := sel.(SourceSel); !ok {
		t.Errorf("expected SourceSel for null, got %T", sel)
	}
}

func TestParseSelector_Empty(t *testing.T) {
	sel, err := ParseSelector(nil)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if _, ok := sel.(SourceSel); !ok {
		t.Errorf("expected SourceSel for empty, got %T", sel)
	}
}

func TestParseSelector_Unknown(t *testing.T) {
	_, err := ParseSelector(json.RawMessage(`{"sel":"unknown"}`))
	if err == nil {
		t.Error("expected error for unknown selector")
	}
}

func TestParseSelector_InvalidJSON(t *testing.T) {
	_, err := ParseSelector(json.RawMessage(`{not valid}`))
	if err == nil {
		t.Error("expected error for invalid JSON")
	}
}

// --- ParseCondition ---

func TestParseCondition_FactionCount(t *testing.T) {
	cond, err := ParseCondition(json.RawMessage(`{"cond":"faction_count","faction":"Tuners","min":3}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cond == nil {
		t.Fatal("expected non-nil condition")
	}
}

func TestParseCondition_HasSecurityPlatform(t *testing.T) {
	cond, err := ParseCondition(json.RawMessage(`{"cond":"has_security_platform"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cond == nil {
		t.Fatal("expected non-nil condition")
	}
}

func TestParseCondition_NotHasSecurityPlatform(t *testing.T) {
	cond, err := ParseCondition(json.RawMessage(`{"cond":"not_has_security_platform"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cond == nil {
		t.Fatal("expected non-nil condition")
	}
}

func TestParseCondition_CardOnField(t *testing.T) {
	cond, err := ParseCondition(json.RawMessage(`{"cond":"card_on_field","card_type":"ObjectStorage","faction":"SD"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if cond == nil {
		t.Fatal("expected non-nil condition")
	}
}

func TestParseCondition_Null(t *testing.T) {
	cond, err := ParseCondition(json.RawMessage(`null`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	// Null condition should always return true
	result := cond(&OpContext{})
	if !result {
		t.Error("null condition should return true")
	}
}

func TestParseCondition_Empty(t *testing.T) {
	cond, err := ParseCondition(nil)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	result := cond(&OpContext{})
	if !result {
		t.Error("empty condition should return true")
	}
}

func TestParseCondition_Unknown(t *testing.T) {
	_, err := ParseCondition(json.RawMessage(`{"cond":"unknown_condition"}`))
	if err == nil {
		t.Error("expected error for unknown condition")
	}
}

func TestParseCondition_InvalidJSON(t *testing.T) {
	_, err := ParseCondition(json.RawMessage(`{not valid}`))
	if err == nil {
		t.Error("expected error for invalid JSON")
	}
}

// --- BuildHandler ---

func TestBuildHandler_SingleOp(t *testing.T) {
	def := EffectDef{
		Trigger: "activate",
		Ops:     []json.RawMessage{json.RawMessage(`{"op":"gain_budget","player":"self","value":500}`)},
	}
	handler, err := BuildHandler(def)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if handler == nil {
		t.Fatal("expected non-nil handler")
	}
}

func TestBuildHandler_MultipleOps(t *testing.T) {
	def := EffectDef{
		Trigger: "activate",
		Ops: []json.RawMessage{
			json.RawMessage(`{"op":"gain_budget","player":"self","value":500}`),
			json.RawMessage(`{"op":"gain_insight","value":200}`),
		},
	}
	handler, err := BuildHandler(def)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if handler == nil {
		t.Fatal("expected non-nil handler")
	}
}

func TestBuildHandler_EmptyOps(t *testing.T) {
	def := EffectDef{
		Trigger: "activate",
		Ops:     []json.RawMessage{},
	}
	handler, err := BuildHandler(def)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if handler == nil {
		t.Fatal("expected non-nil handler")
	}
}

func TestBuildHandler_InvalidOp(t *testing.T) {
	def := EffectDef{
		Trigger: "activate",
		Ops:     []json.RawMessage{json.RawMessage(`{"op":"unknown_op"}`)},
	}
	_, err := BuildHandler(def)
	if err == nil {
		t.Error("expected error for invalid op")
	}
}

// --- parseFilter ---

func TestParseFilter_Null(t *testing.T) {
	filter, err := parseFilter(nil)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if filter != nil {
		t.Error("expected nil filter")
	}
}

func TestParseFilter_NullJSON(t *testing.T) {
	filter, err := parseFilter(json.RawMessage(`null`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if filter != nil {
		t.Error("expected nil filter")
	}
}

func TestParseFilter_ByCardNo(t *testing.T) {
	filter, err := parseFilter(json.RawMessage(`{"card_no":42}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if filter == nil {
		t.Fatal("expected non-nil filter")
	}
}

func TestParseFilter_ByFactionAndType(t *testing.T) {
	filter, err := parseFilter(json.RawMessage(`{"faction":"SD","card_type":"data"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if filter == nil {
		t.Fatal("expected non-nil filter")
	}
}

func TestParseFilter_ByFactionOnly(t *testing.T) {
	filter, err := parseFilter(json.RawMessage(`{"faction":"Tenki"}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if filter == nil {
		t.Fatal("expected non-nil filter")
	}
}

func TestParseFilter_Empty(t *testing.T) {
	filter, err := parseFilter(json.RawMessage(`{}`))
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if filter != nil {
		t.Error("expected nil filter for empty object")
	}
}

// --- parsePlayerRef ---

func TestParsePlayerRef_Self(t *testing.T) {
	if parsePlayerRef("self") != Self {
		t.Error("expected Self")
	}
}

func TestParsePlayerRef_Opponent(t *testing.T) {
	if parsePlayerRef("opponent") != Opponent {
		t.Error("expected Opponent")
	}
}

func TestParsePlayerRef_Default(t *testing.T) {
	if parsePlayerRef("") != Self {
		t.Error("expected Self for empty string")
	}
}

// --- End-to-end: Parse DDoS Attack card ---

func TestParseEffects_DDoSAttack(t *testing.T) {
	raw := json.RawMessage(`[{"trigger":"activate","ops":[
		{"op":"incident_damage",
		 "selector":{"sel":"by_choice","zone":"frontend","owner":"opponent"},
		 "value":500},
		{"op":"destroy_check","player":"opponent"}
	]}]`)

	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("ParseEffects: %v", err)
	}
	if len(defs) != 1 {
		t.Fatalf("expected 1 def, got %d", len(defs))
	}

	handler, err := BuildHandler(defs[0])
	if err != nil {
		t.Fatalf("BuildHandler: %v", err)
	}
	if handler == nil {
		t.Fatal("expected non-nil handler")
	}
}

// --- End-to-end: Parse Cache Engine card with branch ---

func TestParseEffects_CacheEngineBranch(t *testing.T) {
	raw := json.RawMessage(`[{"trigger":"deploy","ops":[
		{"op":"branch","branches":{
			"memcached":[{"op":"gain_budget","player":"self","value":400}],
			"redis":[{"op":"apply_buff","selector":{"sel":"source"},
					  "effect_type":"buff_yield","value":200,
					  "duration":"permanent","source_id":"cache_engine"}]
		}}
	]}]`)

	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("ParseEffects: %v", err)
	}
	if len(defs) != 1 {
		t.Fatalf("expected 1 def, got %d", len(defs))
	}
	if defs[0].Trigger != "deploy" {
		t.Errorf("trigger = %s, want deploy", defs[0].Trigger)
	}

	handler, err := BuildHandler(defs[0])
	if err != nil {
		t.Fatalf("BuildHandler: %v", err)
	}
	if handler == nil {
		t.Fatal("expected non-nil handler")
	}
}

// --- End-to-end: Multi-trigger card (on_attack + on_destroy) ---

func TestParseEffects_MultiTrigger(t *testing.T) {
	raw := json.RawMessage(`[
		{"trigger":"on_attack","ops":[{"op":"absorb_insight","value":600}]},
		{"trigger":"on_destroy","ops":[
			{"op":"deal_damage","selector":{"sel":"all_own","zone":"backend"},"value":400},
			{"op":"destroy_check","player":"self"}
		]}
	]`)

	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("ParseEffects: %v", err)
	}
	if len(defs) != 2 {
		t.Fatalf("expected 2 defs, got %d", len(defs))
	}

	for i, def := range defs {
		handler, err := BuildHandler(def)
		if err != nil {
			t.Fatalf("BuildHandler[%d]: %v", i, err)
		}
		if handler == nil {
			t.Fatalf("handler[%d] should not be nil", i)
		}
	}
}

// --- End-to-end: If condition ---

func TestParseEffects_ComplianceAudit(t *testing.T) {
	raw := json.RawMessage(`[{"trigger":"activate","ops":[
		{"op":"if",
		 "condition":{"cond":"has_security_platform"},
		 "then":[{"op":"lose_budget","player":"opponent","value":400}]},
		{"op":"if",
		 "condition":{"cond":"not_has_security_platform"},
		 "then":[{"op":"lose_budget","player":"opponent","value":800}]}
	]}]`)

	defs, err := ParseEffects(raw)
	if err != nil {
		t.Fatalf("ParseEffects: %v", err)
	}

	handler, err := BuildHandler(defs[0])
	if err != nil {
		t.Fatalf("BuildHandler: %v", err)
	}
	if handler == nil {
		t.Fatal("expected non-nil handler")
	}
}

package effect

import (
	"encoding/json"
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

// EffectDef is the parsed form of a single effect entry from the card's "effects" JSON array.
type EffectDef struct {
	Trigger string `json:"trigger"` // activate, deploy, passive, on_attack, on_destroy, reactive

	// For active effects (activate, deploy, on_attack, on_destroy, reactive):
	Ops []json.RawMessage `json:"ops,omitempty"`

	// For passive effects:
	PassiveType   string          `json:"passive_type,omitempty"` // tp_bonus, yield_bonus, av_bonus, scale_cost_reduction
	Scope         string          `json:"scope,omitempty"`        // platform, resource, attachment
	TargetZone    string          `json:"target_zone,omitempty"`  // frontend, backend
	TargetFaction string          `json:"target_faction,omitempty"`
	Value         json.RawMessage `json:"value,omitempty"`
	Condition     json.RawMessage `json:"condition,omitempty"`
}

// ParseEffects parses the card's "effects" JSON into a slice of EffectDef.
func ParseEffects(raw json.RawMessage) ([]EffectDef, error) {
	if len(raw) == 0 || string(raw) == "null" {
		return nil, nil
	}
	var defs []EffectDef
	if err := json.Unmarshal(raw, &defs); err != nil {
		return nil, fmt.Errorf("parse effects: %w", err)
	}
	return defs, nil
}

// BuildHandler constructs an EffectHandler from an EffectDef.
func BuildHandler(def EffectDef) (EffectHandler, error) {
	ops := make([]Op, 0, len(def.Ops))
	for i, raw := range def.Ops {
		op, err := ParseOp(raw)
		if err != nil {
			return nil, fmt.Errorf("parse op[%d]: %w", i, err)
		}
		ops = append(ops, op)
	}
	return Compose(ops...), nil
}

// --- Op Parsing ---

type opEnvelope struct {
	Op string `json:"op"`
}

// ParseOp converts a JSON op definition into an Op.
func ParseOp(raw json.RawMessage) (Op, error) {
	var env opEnvelope
	if err := json.Unmarshal(raw, &env); err != nil {
		return nil, fmt.Errorf("parse op envelope: %w", err)
	}

	switch env.Op {
	// Budget
	case "gain_budget":
		return parseGainBudget(raw)
	case "lose_budget":
		return parseLoseBudget(raw)

	// Insight
	case "gain_insight":
		return parseGainInsight(raw)
	case "absorb_insight":
		return parseAbsorbInsight(raw)

	// Damage
	case "deal_damage":
		return parseDealDamage(raw)
	case "incident_damage":
		return parseIncidentDamage(raw)
	case "heal_damage":
		return parseHealDamage(raw)

	// Buff
	case "apply_buff":
		return parseApplyBuff(raw)

	// Card move
	case "search_repo":
		return parseSearchRepo(raw)
	case "deploy_from_repo":
		return parseDeployFromRepo(raw)
	case "deploy_from_hand":
		return parseDeployFromHand(raw)
	case "deploy_same_card":
		return parseDeploySameCard(raw)
	case "add_to_hand":
		return parseAddToHand(raw)
	case "trash_to_hand":
		return Op(TrashToHand{}), nil
	case "draw_cards":
		return parseDrawCards(raw)

	// Field
	case "reveal_trap":
		return Op(RevealTrap{}), nil
	case "scale_to_rank":
		return parseScaleToRank(raw)
	case "destroy_platform":
		return Op(DestroyPlatform{}), nil
	case "destroy_check":
		return parseDestroyCheck(raw)

	// Control
	case "cancel_action":
		return Op(SetCancelAction{}), nil
	case "require_budget":
		return parseRequireBudget(raw)
	case "require_max_budget":
		return parseRequireMaxBudget(raw)
	case "require_faction_count":
		return parseRequireFactionCount(raw)
	case "require_opponent_backend":
		return Op(RequireOpponentBackend{}), nil
	case "guard_faction":
		return parseGuardFaction(raw)
	case "guard_not_self":
		return Op(GuardNotSelf{}), nil
	case "guard_target_av":
		return parseGuardTargetAV(raw)
	case "survive_destruction":
		return parseSurviveDestruction(raw)

	// Branch
	case "branch":
		return parseBranch(raw)
	case "if":
		return parseIf(raw)
	case "custom":
		return parseCustom(raw)

	default:
		return nil, fmt.Errorf("unknown op type: %s", env.Op)
	}
}

// --- Budget parsers ---

type budgetDef struct {
	Player string          `json:"player"`
	Value  json.RawMessage `json:"value"`
}

func parseGainBudget(raw json.RawMessage) (Op, error) {
	var d budgetDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return GainBudget{Player: parsePlayerRef(d.Player), Value: ParseValue(d.Value)}, nil
}

func parseLoseBudget(raw json.RawMessage) (Op, error) {
	var d budgetDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return LoseBudget{Player: parsePlayerRef(d.Player), Value: ParseValue(d.Value)}, nil
}

// --- Insight parsers ---

type insightDef struct {
	Value json.RawMessage `json:"value"`
}

func parseGainInsight(raw json.RawMessage) (Op, error) {
	var d insightDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return GainInsight{Value: ParseValue(d.Value)}, nil
}

func parseAbsorbInsight(raw json.RawMessage) (Op, error) {
	var d insightDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return AbsorbInsight{Value: ParseValue(d.Value)}, nil
}

// --- Damage parsers ---

type damageDef struct {
	Selector      json.RawMessage `json:"selector"`
	Value         json.RawMessage `json:"value"`
	BudgetPenalty json.RawMessage `json:"budget_penalty,omitempty"`
}

func parseDealDamage(raw json.RawMessage) (Op, error) {
	var d damageDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	sel, err := ParseSelector(d.Selector)
	if err != nil {
		return nil, fmt.Errorf("deal_damage selector: %w", err)
	}
	return DealDamage{Sel: sel, Value: ParseValue(d.Value)}, nil
}

func parseIncidentDamage(raw json.RawMessage) (Op, error) {
	var d damageDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	sel, err := ParseSelector(d.Selector)
	if err != nil {
		return nil, fmt.Errorf("incident_damage selector: %w", err)
	}
	var penalty Amount
	if len(d.BudgetPenalty) > 0 && string(d.BudgetPenalty) != "null" {
		penalty = ParseValue(d.BudgetPenalty)
	}
	return IncidentDamage{Sel: sel, Value: ParseValue(d.Value), BudgetPenalty: penalty}, nil
}

func parseHealDamage(raw json.RawMessage) (Op, error) {
	var d damageDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	sel, err := ParseSelector(d.Selector)
	if err != nil {
		return nil, fmt.Errorf("heal_damage selector: %w", err)
	}
	return HealDamage{Sel: sel, Value: ParseValue(d.Value)}, nil
}

// --- Buff parser ---

type buffDef struct {
	Selector   json.RawMessage `json:"selector"`
	EffectType string          `json:"effect_type"`
	Value      json.RawMessage `json:"value"`
	Duration   string          `json:"duration"`
	SourceID   string          `json:"source_id"`
}

func parseApplyBuff(raw json.RawMessage) (Op, error) {
	var d buffDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	sel, err := ParseSelector(d.Selector)
	if err != nil {
		return nil, fmt.Errorf("apply_buff selector: %w", err)
	}
	return ApplyBuff{
		Sel:        sel,
		EffectType: d.EffectType,
		Value:      ParseValue(d.Value),
		Duration:   d.Duration,
		SourceID:   d.SourceID,
	}, nil
}

// --- Card move parsers ---

type searchRepoDef struct {
	Faction string `json:"faction"`
}

func parseSearchRepo(raw json.RawMessage) (Op, error) {
	var d searchRepoDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return SearchRepo(d), nil
}

type deployFromRepoDef struct {
	Filter     json.RawMessage `json:"filter,omitempty"`
	OverrideAV int64           `json:"override_av,omitempty"`
}

func parseDeployFromRepo(raw json.RawMessage) (Op, error) {
	var d deployFromRepoDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	filter, err := parseFilter(d.Filter)
	if err != nil {
		return nil, err
	}
	return DeployFromRepo{Filter: filter, OverrideAV: d.OverrideAV}, nil
}

type deployFromHandDef struct {
	Filter json.RawMessage `json:"filter,omitempty"`
}

func parseDeployFromHand(raw json.RawMessage) (Op, error) {
	var d deployFromHandDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	filter, err := parseFilter(d.Filter)
	if err != nil {
		return nil, err
	}
	return DeployFromHand{Filter: filter}, nil
}

type deploySameCardDef struct {
	OverrideAV int64 `json:"override_av,omitempty"`
}

func parseDeploySameCard(raw json.RawMessage) (Op, error) {
	var d deploySameCardDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return DeployFromRepoSameCard(d), nil
}

type addToHandDef struct {
	CardNo json.RawMessage `json:"card_no"`
}

func parseAddToHand(raw json.RawMessage) (Op, error) {
	var d addToHandDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return AddToHand{CardNo: ParseValue(d.CardNo)}, nil
}

type drawCardsDef struct {
	Count        int  `json:"count"`
	TunersTrash bool `json:"tuners_trash,omitempty"`
	KeepOne      bool `json:"keep_one,omitempty"`
}

func parseDrawCards(raw json.RawMessage) (Op, error) {
	var d drawCardsDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return DrawCards(d), nil
}

// --- Field parsers ---

type scaleToRankDef struct {
	Rank string `json:"rank"`
}

func parseScaleToRank(raw json.RawMessage) (Op, error) {
	var d scaleToRankDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return ScaleToRank(d), nil
}

type destroyCheckDef struct {
	Player string `json:"player"`
}

func parseDestroyCheck(raw json.RawMessage) (Op, error) {
	var d destroyCheckDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return DestroyCheck{Player: parsePlayerRef(d.Player)}, nil
}

// --- Control parsers ---

type requireBudgetDef struct {
	Min int64 `json:"min"`
}

func parseRequireBudget(raw json.RawMessage) (Op, error) {
	var d requireBudgetDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return RequireBudget(d), nil
}

type requireMaxBudgetDef struct {
	Max int64 `json:"max"`
}

func parseRequireMaxBudget(raw json.RawMessage) (Op, error) {
	var d requireMaxBudgetDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return RequireMaxBudget(d), nil
}

type requireFactionCountDef struct {
	Faction string `json:"faction"`
	Min     int    `json:"min"`
}

func parseRequireFactionCount(raw json.RawMessage) (Op, error) {
	var d requireFactionCountDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return RequireFactionCount(d), nil
}

type guardFactionDef struct {
	Faction  string `json:"faction"`
	CardType string `json:"card_type,omitempty"`
}

func parseGuardFaction(raw json.RawMessage) (Op, error) {
	var d guardFactionDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return GuardFaction(d), nil
}

type guardTargetAVDef struct {
	MaxAV int64 `json:"max_av"`
}

func parseGuardTargetAV(raw json.RawMessage) (Op, error) {
	var d guardTargetAVDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return GuardTargetAV(d), nil
}

type surviveDestructionDef struct {
	SurviveAV int64 `json:"survive_av"`
}

func parseSurviveDestruction(raw json.RawMessage) (Op, error) {
	var d surviveDestructionDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	return SurviveDestruction(d), nil
}

// --- Branch parsers ---

type branchDef struct {
	Branches map[string][]json.RawMessage `json:"branches"`
}

func parseBranch(raw json.RawMessage) (Op, error) {
	var d branchDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	branches := make(map[string][]Op, len(d.Branches))
	for key, rawOps := range d.Branches {
		ops := make([]Op, 0, len(rawOps))
		for i, r := range rawOps {
			op, err := ParseOp(r)
			if err != nil {
				return nil, fmt.Errorf("branch[%s][%d]: %w", key, i, err)
			}
			ops = append(ops, op)
		}
		branches[key] = ops
	}
	return BranchOnChoice{Branches: branches}, nil
}

type ifDef struct {
	Condition json.RawMessage   `json:"condition"`
	Then      []json.RawMessage `json:"then"`
}

func parseIf(raw json.RawMessage) (Op, error) {
	var d ifDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	cond, err := ParseCondition(d.Condition)
	if err != nil {
		return nil, fmt.Errorf("if condition: %w", err)
	}
	thenOps := make([]Op, 0, len(d.Then))
	for i, r := range d.Then {
		op, err := ParseOp(r)
		if err != nil {
			return nil, fmt.Errorf("if then[%d]: %w", i, err)
		}
		thenOps = append(thenOps, op)
	}
	return IfCondition{Cond: cond, Then: thenOps}, nil
}

type customDef struct {
	Fn string `json:"fn"`
}

func parseCustom(raw json.RawMessage) (Op, error) {
	var d customDef
	if err := json.Unmarshal(raw, &d); err != nil {
		return nil, err
	}
	fn, ok := CustomFunctions[d.Fn]
	if !ok {
		return nil, fmt.Errorf("unknown custom function: %s", d.Fn)
	}
	return CustomFn{Fn: fn}, nil
}

// --- Value (Amount) Parsing ---

// ParseValue converts a JSON value into an Amount.
// Supports: plain number (static), or {"ref": "...", ...} for dynamic values.
func ParseValue(raw json.RawMessage) Amount {
	if len(raw) == 0 || string(raw) == "null" {
		return Static(0)
	}

	// Try static number first
	var num int64
	if err := json.Unmarshal(raw, &num); err == nil {
		return Static(num)
	}

	// Try ref object
	var ref struct {
		Ref        string `json:"ref"`
		Base       int64  `json:"base,omitempty"`
		PerBackend int64  `json:"per_backend,omitempty"`
		MaxBonus   int64  `json:"max_bonus,omitempty"`
	}
	if err := json.Unmarshal(raw, &ref); err == nil {
		switch ref.Ref {
		case "source_yield":
			return SourceYield()
		case "half_max_av":
			return HalfMaxAV()
		case "target_card_id":
			return TargetCardID()
		case "sla_penalty":
			return SLAPenaltyAmount()
		case "target_tp":
			return TargetTP()
		case "backend_scaled":
			return BackendScaled(ref.Base, ref.PerBackend, ref.MaxBonus)
		}
	}

	return Static(0)
}

// --- Selector Parsing ---

type selectorEnvelope struct {
	Sel      string `json:"sel"`
	Zone     string `json:"zone,omitempty"`
	Faction  string `json:"faction,omitempty"`
	CardType string `json:"card_type,omitempty"`
	Owner    string `json:"owner,omitempty"`
}

// ParseSelector converts a JSON selector into a Selector.
func ParseSelector(raw json.RawMessage) (Selector, error) {
	if len(raw) == 0 || string(raw) == "null" {
		return SourceSel{}, nil
	}

	var s selectorEnvelope
	if err := json.Unmarshal(raw, &s); err != nil {
		return nil, fmt.Errorf("parse selector: %w", err)
	}

	switch s.Sel {
	case "source":
		return SourceSel{}, nil
	case "target":
		return TargetSel{}, nil
	case "by_choice":
		return ByChoiceSel{
			Zone:     s.Zone,
			Faction:  s.Faction,
			CardType: s.CardType,
			Owner:    parsePlayerRef(s.Owner),
		}, nil
	case "all_own":
		return AllOwnSel{Zone: s.Zone, Faction: s.Faction}, nil
	case "all_opponent":
		return AllOpponentSel{Zone: s.Zone, Faction: s.Faction}, nil
	default:
		return nil, fmt.Errorf("unknown selector type: %s", s.Sel)
	}
}

// --- Condition Parsing ---

type conditionEnvelope struct {
	Cond     string `json:"cond"`
	Faction  string `json:"faction,omitempty"`
	Min      int    `json:"min,omitempty"`
	CardType string `json:"card_type,omitempty"`
}

// ParseCondition converts a JSON condition into a func(*OpContext) bool.
func ParseCondition(raw json.RawMessage) (func(octx *OpContext) bool, error) {
	if len(raw) == 0 || string(raw) == "null" {
		return func(_ *OpContext) bool { return true }, nil
	}

	var c conditionEnvelope
	if err := json.Unmarshal(raw, &c); err != nil {
		return nil, fmt.Errorf("parse condition: %w", err)
	}

	switch c.Cond {
	case "faction_count":
		faction := c.Faction
		min := c.Min
		return func(octx *OpContext) bool {
			field, err := octx.GetField(octx.Ctx.PlayerNum)
			if err != nil {
				return false
			}
			return CountFactionCards(field, faction, octx.Ctx.CardCache) >= min
		}, nil

	case "has_security_platform":
		return func(octx *OpContext) bool {
			oppField, err := octx.GetField(model.OpponentNum(octx.Ctx.PlayerNum))
			return err == nil && HasSecurityPlatform(oppField, octx.Ctx.CardCache)
		}, nil

	case "not_has_security_platform":
		return func(octx *OpContext) bool {
			oppField, err := octx.GetField(model.OpponentNum(octx.Ctx.PlayerNum))
			return err != nil || !HasSecurityPlatform(oppField, octx.Ctx.CardCache)
		}, nil

	case "card_on_field":
		cardType := c.CardType
		faction := c.Faction
		return func(octx *OpContext) bool {
			field, err := octx.GetField(octx.Ctx.PlayerNum)
			if err != nil {
				return false
			}
			return HasCardTypeOnField(field, cardType, faction, octx.Ctx.CardCache)
		}, nil

	default:
		return nil, fmt.Errorf("unknown condition type: %s", c.Cond)
	}
}

// --- Filter Parsing ---

type filterDef struct {
	Faction  string `json:"faction,omitempty"`
	CardType string `json:"card_type,omitempty"`
	CardNo   int64  `json:"card_no,omitempty"`
}

func parseFilter(raw json.RawMessage) (CardFilter, error) {
	if len(raw) == 0 || string(raw) == "null" {
		return nil, nil
	}

	var f filterDef
	if err := json.Unmarshal(raw, &f); err != nil {
		return nil, fmt.Errorf("parse filter: %w", err)
	}

	if f.CardNo > 0 {
		return CardNoFilter(f.CardNo), nil
	}

	if f.CardType != "" {
		typeCheck := cardTypeToFunc(f.CardType)
		return FactionAndTypeFilter(f.Faction, typeCheck), nil
	}

	if f.Faction != "" {
		return FactionFilter(f.Faction), nil
	}

	return nil, nil
}

// --- Helpers ---

func parsePlayerRef(s string) PlayerRef {
	if s == "opponent" {
		return Opponent
	}
	return Self
}

func cardTypeToFunc(cardType string) func(string) bool {
	switch cardType {
	case "compute":
		return model.IsComputeType
	case "data":
		return model.IsDataType
	default:
		return nil
	}
}

// CustomFunctions maps custom function names to their implementations.
// These are the escape hatches for effects that cannot be decomposed into Ops.
var CustomFunctions = map[string]func(*OpContext) error{}

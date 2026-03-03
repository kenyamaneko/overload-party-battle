package effect

import (
	"encoding/json"
	"fmt"

	"github.com/kenyamaneko/overload-party-common/model"
)

func init() {
	// Populate CustomFunctions map for parser.go
	CustomFunctions["pub_sub_chain_damage"] = pubSubChainDamage
	CustomFunctions["veloce_batch_buff"] = veloceBatchBuff
	CustomFunctions["config_error_debuff"] = configErrorDebuff
	CustomFunctions["failover_deploy"] = failoverDeploy
	CustomFunctions["chaos_redirect"] = chaosRedirect
	CustomFunctions["rate_limiter_fire"] = rateLimiterFire
	CustomFunctions["throttling_fire"] = throttlingFire
}

// RegisterAllEffects registers all card effect handlers into the registry.
// Uses RegisterComposed so that Ops are stored for NPC classification.
func RegisterAllEffects(r *EffectRegistry) {
	// ========================
	// SD (Smile Delivery)
	// ========================

	// #7 SD RDB - アデリース: Reserved Instance (optional deploy cost -200)
	r.RegisterComposed(7, TriggerDeploy,
		BranchOnChoice{Branches: map[string][]Op{
			"use": {
				GainBudget{Self, Static(200)},
				ApplyBuff{SourceSel{}, "reserved_instance", Static(1), "permanent", "reserved_instance"},
			},
			"skip": {},
		}},
	)

	// #9 SD Storage - えすす: Versioning (return destroyed SD backend to hand)
	r.RegisterComposed(9, TriggerOnDestroy,
		GuardFaction{model.FactionSD, ""},
		AddToHand{TargetCardID()},
		SetCancelAction{},
	)

	// #10 SD DB - ダイナ: On-Demand (pay 400, double Yield this turn)
	r.RegisterComposed(10, TriggerActivate,
		RequireBudget{400},
		LoseBudget{Self, Static(400)},
		ApplyBuff{SourceSel{}, "buff_yield", SourceYield(), "this_turn", "on_demand"},
	)

	// #11 SD Cache - メリー: Cache Engine (Memcached/Redis choice on deploy)
	r.RegisterComposed(11, TriggerDeploy,
		BranchOnChoice{Branches: map[string][]Op{
			"memcached": {GainBudget{Self, Static(400)}},
			"redis":     {ApplyBuff{SourceSel{}, "buff_yield", Static(200), "permanent", "cache_engine_redis"}},
		}},
	)

	// #14 SD Guard: Reveal 1 opponent trap
	r.RegisterComposed(14, TriggerActivate, RevealTrap{})

	// #15 SD Firewall: Block DDoS / Data Breach
	r.RegisterComposed(15, TriggerReactive, SetCancelAction{})

	// #18 SD Keys: Block Data Breach (attachment)
	r.RegisterComposed(18, TriggerReactive, SetCancelAction{})

	// #19 SD Formation: Search SD Component
	r.RegisterComposed(19, TriggerActivate, SearchRepo{model.FactionSD})

	// #20 SD Marketplace: Budget +600 if 3+ SD on field
	r.RegisterComposed(20, TriggerActivate,
		RequireFactionCount{model.FactionSD, 3},
		GainBudget{Self, Static(600)},
	)

	// #21 SD Cost Explorer: All Deploy Cost -200 this turn
	r.RegisterComposed(21, TriggerActivate,
		ApplyBuff{AllOwnSel{"", ""}, "deploy_discount", Static(200), "this_turn", "cost_explorer"},
	)

	// #22 Smile Recovery: AV +500 when SD resource ≤ 400 AV
	r.RegisterComposed(22, TriggerReactive,
		GuardFaction{model.FactionSD, ""},
		GuardTargetAV{MaxAV: 400},
		HealDamage{TargetSel{}, Static(500)},
		SetCancelAction{},
	)

	// #118 SD Ecosystem: SD frontend TP +200 this turn
	r.RegisterComposed(118, TriggerActivate,
		RequireFactionCount{model.FactionSD, 3},
		ApplyBuff{AllOwnSel{model.ZoneFrontend, model.FactionSD}, "buff_tp", Static(200), "this_turn", "sd_ecosystem"},
	)

	// #121 SD Smile Delivery: Deploy from hand at cost 0
	r.RegisterComposed(121, TriggerActivate,
		DeployFromHand{nil},
	)

	// ========================
	// Tenki (Tenki Cloud)
	// ========================

	// #30 Tenki DB - ハヤテ: Failover Group (Yield +400 until next own turn end when ally Tenki DB destroyed)
	r.RegisterComposed(30, TriggerOnDestroy,
		GuardFaction{model.FactionTenki, "data"},
		GuardNotSelf{},
		ApplyBuff{SourceSel{}, "buff_yield", Static(400), "until_next_own_turn_end", "failover_group"},
	)

	// #32 Tenki DB - 百花の天穹<コスモ>: Turnkey Global Distribution (deploy another from repo)
	r.RegisterComposed(32, TriggerDeploy,
		DeployFromRepo{CardNoFilter(32), 0},
	)

	// #36 Tenki Sentinel: Reveal trap + Incident damage -300
	r.RegisterComposed(36, TriggerActivate,
		RevealTrap{},
		ApplyBuff{AllOwnSel{"", ""}, "incident_reduction", Static(300), "this_turn", "sentinel"},
	)

	// #37 Tenki Protection: Block DDoS / Data Breach
	r.RegisterComposed(37, TriggerReactive, SetCancelAction{})

	// #38 Tenki Backup: Revival on destroy
	r.RegisterComposed(38, TriggerOnDestroy,
		ApplyBuff{TargetSel{}, "pending_revival", HalfMaxAV(), "next_turn", "tenki_backup"},
	)

	// #40 Tenki Key Vault: Block Data Breach
	r.RegisterComposed(40, TriggerReactive, SetCancelAction{})

	// #41 Tenki Site Recovery: Deploy copy from repo on destroy
	r.RegisterComposed(41, TriggerOnDestroy,
		DeployFromRepoSameCard{OverrideAV: 200},
	)

	// #42 Tenki Template: Search Tenki Component
	r.RegisterComposed(42, TriggerActivate, SearchRepo{model.FactionTenki})

	// #43 Tenki Migration: Return Component from trash to hand
	r.RegisterComposed(43, TriggerActivate, TrashToHand{})

	// #44 Tenki Policy: Block opponent Incidents this turn
	r.RegisterComposed(44, TriggerActivate,
		ApplyBuff{AllOpponentSel{model.ZoneFrontend, ""}, "incident_block", Static(1), "this_turn", "tenki_policy"},
	)

	// #45 Tenki Defender: Nullify Incident
	r.RegisterComposed(45, TriggerReactive, SetCancelAction{})

	// #46 Tenki Traffic: Deploy Tenki Compute from hand when frontend destroyed
	r.RegisterComposed(46, TriggerReactive,
		DeployFromHand{FactionAndTypeFilter(model.FactionTenki, model.IsComputeType)},
	)

	// #122 Windy 障害: Tenki frontends cannot attack this turn
	r.RegisterComposed(122, TriggerReactive,
		ApplyBuff{AllOwnSel{model.ZoneFrontend, model.FactionTenki}, "cannot_attack", Static(1), "this_turn", "madonosoft_failer"},
	)

	// #123 Windy Update: Deal 400 damage to all Tenki cards
	r.RegisterComposed(123, TriggerReactive,
		DealDamage{AllOwnSel{"", model.FactionTenki}, Static(400)},
		DestroyCheck{Self},
	)

	// ========================
	// Sugar
	// ========================

	// #50 Sugar Orchestrator - クーヘンバウムティス: Autopilot (auto scale to medium on deploy)
	r.RegisterComposed(50, TriggerDeploy, ScaleToRank{model.RankMedium})

	// #51 Sugar AI - バター X: Training Pipeline (absorb 400 insight on attack)
	r.RegisterComposed(51, TriggerOnAttack, AbsorbInsight{Static(400)})

	// #52 Sugar AI - Dr. テンソルベ: Training Pipeline (absorb 600 insight on attack) + Cascade Failure on destroy
	r.RegisterComposed(52, TriggerOnAttack, AbsorbInsight{Static(600)})
	r.RegisterComposed(52, TriggerOnDestroy,
		DealDamage{AllOwnSel{model.ZoneBackend, ""}, Static(400)},
		DestroyCheck{Self},
	)

	// #57 Sugar DB - ファイアトーストア: Realtime Sync (+200 insight on Sugar deploy)
	r.RegisterComposed(57, TriggerDeploy, GainInsight{Static(200)})

	// #58 Sugar Datawarehouse - ビッグ・アイスクエリム: Streaming Insert (+200 insight on Sugar frontend attack)
	r.RegisterComposed(58, TriggerOnAttack, GainInsight{Static(200)})

	// #61 Sugar ビッグ・アイスクエリム Analytics: Absorb 300 insight per Yield phase
	r.RegisterComposed(61, TriggerPassive, AbsorbInsight{Static(300)})

	// #63 Sugar ぱくぱくサブレ: Message Fanout (chain attack — bonus 200 damage via other Sugar Compute)
	r.RegisterComposed(63, TriggerOnAttack,
		CustomFnTagged{Fn: pubSubChainDamage, Categories: []EffectCategory{CatSingleDamage}, Target: TargetSelf},
	)

	// #65 Sugar Deployment: Search Sugar Component
	r.RegisterComposed(65, TriggerActivate, SearchRepo{model.FactionSugar})

	// #66 Sugar バター X Batch: Double 1 frontend Compute's TP this turn
	r.RegisterComposed(66, TriggerActivate,
		CustomFnTagged{Fn: veloceBatchBuff, Categories: []EffectCategory{CatBuff}, Target: TargetChoice, Zone: model.ZoneFrontend},
	)

	// #67 Sugar Knowledge: Absorb insight with backend bonus
	r.RegisterComposed(67, TriggerActivate,
		AbsorbInsight{BackendScaled(400, 200, 600)},
	)

	// #68 Sugar Error Budget: Survive destruction with AV 200
	r.RegisterComposed(68, TriggerReactive,
		GuardFaction{model.FactionSugar, ""},
		SurviveDestruction{200},
	)

	// #125 Sugar Cache - メレンゲもりもりストア: Cache Engine (Memcached/Redis choice)
	r.RegisterComposed(125, TriggerDeploy,
		BranchOnChoice{Branches: map[string][]Op{
			"memcached": {GainBudget{Self, Static(400)}},
			"redis":     {ApplyBuff{SourceSel{}, "buff_yield", Static(200), "permanent", "cache_engine_redis"}},
		}},
	)

	// ========================
	// Tuners
	// ========================

	// #72 Tuners Bare Metal: Self-damage on attack
	r.RegisterComposed(72, TriggerOnAttack,
		DealDamage{SourceSel{}, Static(300)},
	)

	// #83 Tuners Guard: Reveal trap + conditional Incident reduction
	r.RegisterComposed(83, TriggerActivate,
		RevealTrap{},
		IfCondition{
			Cond: func(octx *OpContext) bool {
				field, err := octx.GetField(octx.Ctx.PlayerNum)
				if err != nil {
					return false
				}
				return CountFactionCards(field, model.FactionTuners, octx.Ctx.CardCache) >= 3
			},
			Then: []Op{
				ApplyBuff{AllOwnSel{"", ""}, "incident_reduction", Static(200), "this_turn", "tuners_guard"},
			},
		},
	)

	// #84 Tuners WAF: Block DDoS / Data Breach
	r.RegisterComposed(84, TriggerReactive, SetCancelAction{})

	// #85 Tuners ノーツガード: Deploy Tuners DB from repo on destroy
	r.RegisterComposed(85, TriggerOnDestroy,
		DeployFromRepo{FactionAndTypeFilter(model.FactionTuners, model.IsDBType), 200},
	)

	// #89 Tuners License: Full AV restore on Tuners DB
	r.RegisterComposed(89, TriggerActivate,
		HealDamage{
			ByChoiceSel{Zone: model.ZoneBackend, Faction: model.FactionTuners, CardType: "data", Owner: Self},
			Static(0),
		},
	)

	// #90 Tuners Failback: Deploy Tuners DB from hand when Tuners DB destroyed
	r.RegisterComposed(90, TriggerReactive,
		DeployFromHand{FactionAndTypeFilter(model.FactionTuners, model.IsDBType)},
	)

	// ========================
	// Neutral
	// ========================

	// #98 Cloud Engineer: Draw 1, Tuners → trash
	r.RegisterComposed(98, TriggerActivate,
		DrawCards{Count: 1, TunersTrash: true},
	)

	// #99 Cloud Architect: Draw 2, discard 1, Tuners → trash
	r.RegisterComposed(99, TriggerActivate,
		DrawCards{Count: 2, TunersTrash: true, KeepOne: true},
	)

	// #100 寺リフォーム: Search any Component
	r.RegisterComposed(100, TriggerActivate, SearchRepo{""})

	// #101 プロジェクトマネージャー: Budget +400
	r.RegisterComposed(101, TriggerActivate, GainBudget{Self, Static(400)})

	// #102 クラウドファンディング: Budget +1000 (first turn only)
	// TODO: CARDS.md specifies first-turn-only restriction, but RequireFirstTurn guard is not yet implemented.
	r.RegisterComposed(102, TriggerActivate,
		GainBudget{Self, Static(1000)},
	)

	// #103 Open Source Migration: Destroy opponent Platform
	r.RegisterComposed(103, TriggerActivate, DestroyPlatform{})

	// #120 Venture Capital: Budget +900 if ≤ 1000
	r.RegisterComposed(120, TriggerActivate,
		RequireMaxBudget{1000},
		GainBudget{Self, Static(900)},
	)

	// --- Incidents ---

	// #104 DDoS Attack: 500 damage to 1 frontend
	r.RegisterComposed(104, TriggerActivate,
		IncidentDamage{ByChoiceSel{Zone: model.ZoneFrontend, Owner: Opponent}, Static(500), nil},
		DestroyCheck{Opponent},
	)

	// #105 Data Breach: 600 damage to 1 backend, Budget -300
	r.RegisterComposed(105, TriggerActivate,
		IncidentDamage{ByChoiceSel{Zone: model.ZoneBackend, Owner: Opponent}, Static(600), Static(300)},
		DestroyCheck{Opponent},
	)

	// #106 Config Error: TP → 0 until next turn end
	r.RegisterComposed(106, TriggerActivate,
		CustomFnTagged{Fn: configErrorDebuff, Categories: []EffectCategory{CatDebuff}, Target: TargetChoice, Zone: model.ZoneFrontend},
	)

	// #107 Data Scraping: Absorb 600 insight (fails if no backend)
	r.RegisterComposed(107, TriggerActivate,
		RequireOpponentBackend{},
		AbsorbInsight{Static(600)},
	)

	// #108 Crawler Bot: Absorb 300 insight
	r.RegisterComposed(108, TriggerActivate, AbsorbInsight{Static(300)})

	// #109 Region Outage: 500 damage to all resources
	r.RegisterComposed(109, TriggerActivate,
		IncidentDamage{AllOpponentSel{"", ""}, Static(500), nil},
		DestroyCheck{Opponent},
	)

	// #110 Crypto Mining: 400 damage to 1 frontend, self Budget +500
	r.RegisterComposed(110, TriggerActivate,
		IncidentDamage{ByChoiceSel{Zone: model.ZoneFrontend, Owner: Opponent}, Static(400), nil},
		DestroyCheck{Opponent},
		GainBudget{Self, Static(500)},
	)

	// #111 Ransomware: Disable 1 Component until next turn end
	r.RegisterComposed(111, TriggerActivate,
		ApplyBuff{ByChoiceSel{Owner: Opponent}, "ransomware", Static(1), "until_next_turn_end", "ransomware"},
	)

	// #112 Compliance Audit: Budget -400 (or -800 without Security Platform)
	r.RegisterComposed(112, TriggerActivate,
		IfCondition{
			Cond: func(octx *OpContext) bool {
				oppField, err := octx.GetField(model.OpponentNum(octx.Ctx.PlayerNum))
				return err == nil && HasSecurityPlatform(oppField, octx.Ctx.CardCache)
			},
			Then: []Op{LoseBudget{Opponent, Static(400)}},
		},
		IfCondition{
			Cond: func(octx *OpContext) bool {
				oppField, err := octx.GetField(model.OpponentNum(octx.Ctx.PlayerNum))
				return err != nil || !HasSecurityPlatform(oppField, octx.Ctx.CardCache)
			},
			Then: []Op{LoseBudget{Opponent, Static(800)}},
		},
	)

	// #113 レートリミット: cannot_operate on Compute/AI_ML with TP ≥ 900 deployed by opponent
	r.RegisterComposed(113, TriggerOnEnemyDeploy,
		CustomFnTagged{Fn: rateLimiterFire, Categories: []EffectCategory{CatDebuff}, Target: TargetAllOpp},
	)

	// #135 スロットリング: destroy opponent's 3rd resource deployed in a turn (once per turn)
	r.RegisterComposed(135, TriggerOnEnemyDeploy,
		CustomFnTagged{Fn: throttlingFire, Categories: []EffectCategory{CatCancelAction}, Target: TargetAllOpp},
	)

	// #115 フェイルオーバー: Deploy same type from hand when frontend destroyed
	r.RegisterComposed(115, TriggerReactive,
		CustomFnTagged{Fn: failoverDeploy, Categories: []EffectCategory{CatDeployFree}},
	)

	// #117 Chaos Engineering: Redirect attack to opponent's frontend
	r.RegisterComposed(117, TriggerReactive,
		CustomFnTagged{Fn: chaosRedirect, Categories: []EffectCategory{CatCancelAction}},
		SetCancelAction{},
	)
}

// --- CustomFn implementations for effects that cannot be fully decomposed ---

// pubSubChainDamage: find another Sugar Compute on own frontend, deal 200 bonus damage to target.
func pubSubChainDamage(octx *OpContext) error {
	if octx.Ctx.Target == nil {
		return fmt.Errorf("no target")
	}

	field, err := octx.GetField(octx.Ctx.PlayerNum)
	if err != nil {
		return err
	}

	for _, res := range field.Frontend {
		if res == nil || res.InstanceID == octx.Ctx.Source.InstanceID {
			continue
		}
		card := octx.Ctx.CardCache.Get(res.CardID)
		if card != nil && card.Faction == model.FactionSugar && model.IsComputeType(card.CardType) {
			octx.Ctx.Target.Damage += 200
			break
		}
	}

	return nil
}

// veloceBatchBuff: double selected frontend Compute's TP this turn via ChoiceInstanceID.
func veloceBatchBuff(octx *OpContext) error {
	var choice ChoiceInstanceID
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse choice: %w", err)
	}

	field, err := octx.GetField(octx.Ctx.PlayerNum)
	if err != nil {
		return err
	}

	target, zone, _ := model.FindResourceByID(field, choice.InstanceID)
	if target == nil || zone != model.ZoneFrontend {
		return fmt.Errorf("target must be a frontend resource")
	}

	card := octx.Ctx.CardCache.Get(target.CardID)
	if card == nil || !model.IsComputeType(card.CardType) {
		return fmt.Errorf("target must be a Compute type")
	}

	if target.CurrentTP != nil {
		target.TemporaryEffects = append(target.TemporaryEffects, model.TemporaryEffect{
			EffectType: "buff_tp",
			Value:      *target.CurrentTP,
			Duration:   "this_turn",
			SourceID:   "veloce_batch",
		})
	}

	return nil
}

// configErrorDebuff: set opponent frontend TP to 0 until next turn end.
func configErrorDebuff(octx *OpContext) error {
	var choice ChoiceInstanceID
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse choice: %w", err)
	}

	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(oppNum)
	if err != nil {
		return err
	}

	target, zone, _ := model.FindResourceByID(field, choice.InstanceID)
	if target == nil || zone != model.ZoneFrontend {
		return fmt.Errorf("target must be opponent's frontend")
	}

	debuffValue := int64(10000)
	if target.CurrentTP != nil {
		debuffValue = *target.CurrentTP + 10000
	}
	target.TemporaryEffects = append(target.TemporaryEffects, model.TemporaryEffect{
		EffectType: "debuff_tp",
		Value:      debuffValue,
		Duration:   "until_next_turn_end",
		SourceID:   "config_error",
	})

	return nil
}

// failoverDeploy: validate choice card type matches destroyed target's type, deploy from hand.
func failoverDeploy(octx *OpContext) error {
	if octx.Ctx.Target == nil {
		return fmt.Errorf("no target")
	}

	var choice ChoiceCardNo
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse choice: %w", err)
	}

	targetCard := octx.Ctx.CardCache.Get(octx.Ctx.Target.CardID)
	choiceCard := octx.Ctx.CardCache.Get(choice.CardNo)
	if targetCard == nil || choiceCard == nil {
		return fmt.Errorf("card not found")
	}
	if choiceCard.CardType != targetCard.CardType {
		return fmt.Errorf("must deploy same type as destroyed card")
	}

	return deployResourceFromHand(octx.Ctx.State, octx.Ctx.PlayerNum, choice.CardNo, octx.Ctx.CardCache)
}

// rateLimiterFire: when opponent deploys a Compute or AI/ML card with TP ≥ 900,
// apply "cannot_operate" to that resource for this turn.
func rateLimiterFire(octx *OpContext) error {
	target := octx.Ctx.Target
	if target == nil {
		return fmt.Errorf("no target")
	}

	targetCard := octx.Ctx.CardCache.Get(target.CardID)
	if targetCard == nil {
		return fmt.Errorf("card not found")
	}

	if !model.IsComputeType(targetCard.CardType) && targetCard.CardType != "AI/ML" {
		return fmt.Errorf("target is not Compute or AI/ML")
	}

	if target.MaxTP == nil || *target.MaxTP < 900 {
		return fmt.Errorf("target TP < 900")
	}

	target.TemporaryEffects = append(target.TemporaryEffects, model.TemporaryEffect{
		EffectType: "cannot_operate",
		Value:      1,
		Duration:   "this_turn",
		SourceID:   "rate_limiter",
	})

	// Write updated deployer field (Target lives on the deployer's field)
	deployerNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(deployerNum)
	if err != nil {
		return err
	}
	for _, r := range field.Frontend {
		if r != nil && r.InstanceID == target.InstanceID {
			r.TemporaryEffects = target.TemporaryEffects
			break
		}
	}
	for _, r := range field.Backend {
		if r != nil && r.InstanceID == target.InstanceID {
			r.TemporaryEffects = target.TemporaryEffects
			break
		}
	}

	return nil
}

// throttlingFire: when opponent deploys their 3rd resource in a turn, cancel (destroy) it.
// Once-per-turn limit enforced via SupSource.EffectUsedThisTurn.
func throttlingFire(octx *OpContext) error {
	if octx.Ctx.SupSource != nil && octx.Ctx.SupSource.EffectUsedThisTurn {
		return fmt.Errorf("already used this turn")
	}

	deployerNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(deployerNum)
	if err != nil {
		return err
	}

	count := model.CountDeployedThisTurn(field, octx.Ctx.State.CurrentTurn)
	if count != 3 {
		return fmt.Errorf("not the 3rd deploy (count=%d)", count)
	}

	if octx.Ctx.SupSource != nil {
		octx.Ctx.SupSource.EffectUsedThisTurn = true
	}

	octx.Result.CancelAction = true
	return nil
}

// chaosRedirect: validate redirect target is opponent's frontend.
func chaosRedirect(octx *OpContext) error {
	var choice ChoiceInstanceID
	if err := json.Unmarshal(octx.Ctx.ChoiceData, &choice); err != nil {
		return fmt.Errorf("parse choice: %w", err)
	}

	oppNum := model.OpponentNum(octx.Ctx.PlayerNum)
	field, err := octx.GetField(oppNum)
	if err != nil {
		return err
	}

	target, zone, _ := model.FindResourceByID(field, choice.InstanceID)
	if target == nil || zone != model.ZoneFrontend {
		return fmt.Errorf("redirect target must be opponent's frontend")
	}

	return nil
}

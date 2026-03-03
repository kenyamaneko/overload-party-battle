package engine

import (
	"encoding/json"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

func TestCalculateEffectiveTP_Base(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Test Compute",
		Faction:  "SD",
		CardType: "Compute",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	instance := &model.ResourceInstance{
		CardID:           1,
		Rank:             model.RankSmall,
		FaceUp:           true,
		CurrentTP:        &tp,
		MaxTP:            &maxTP,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{}
	result := CalculateEffectiveTP(instance, field, cc)
	if result != 700 {
		t.Errorf("TP = %d, want 700", result)
	}
}

func TestCalculateEffectiveTP_WithRank(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(1400)
	maxTP := int64(1400)
	instance := &model.ResourceInstance{
		CardID:           1,
		Rank:             model.RankMedium,
		FaceUp:           true,
		CurrentTP:        &tp,
		MaxTP:            &maxTP,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{}
	result := CalculateEffectiveTP(instance, field, cc)
	if result != 1400 {
		t.Errorf("TP = %d, want 1400 (700 * 2)", result)
	}
}

func TestCalculateEffectiveTP_WithFamilyC(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	familyC := model.FamilyC
	tp := int64(1050)
	maxTP := int64(1050)
	instance := &model.ResourceInstance{
		CardID:           1,
		Rank:             model.RankSmall,
		FaceUp:           true,
		InstanceFamily:   &familyC,
		CurrentTP:        &tp,
		MaxTP:            &maxTP,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{}
	result := CalculateEffectiveTP(instance, field, cc)
	if result != 1050 {
		t.Errorf("TP = %d, want 1050 (700 * 1.5)", result)
	}
}

func TestCalculateEffectiveTP_WithPlatformBonus(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	cc.InjectForTest(13, &model.CardDefinition{
		CardNo:   13,
		CardType: "Platform",
		Faction:  "SD",
		PlatformEffects: []model.PlatformEffect{
			{
				Type: model.PlatformTPBonus,
				Params: map[string]interface{}{
					"target_faction":    "SD",
					"target_card_types": []interface{}{"Compute", "AI/ML", "Orchestrator", "Container", "Serverless"},
					"bonus":             float64(200),
				},
			},
		},
	})

	tp := int64(700)
	maxTP := int64(700)
	instance := &model.ResourceInstance{
		CardID:           1,
		FaceUp:           true,
		Rank:             model.RankSmall,
		CurrentTP:        &tp,
		MaxTP:            &maxTP,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 13},
		},
	}

	result := CalculateEffectiveTP(instance, field, cc)
	if result != 900 {
		t.Errorf("TP = %d, want 900 (700 + 200 platform)", result)
	}
}

func TestCalculateEffectiveTP_WithTemporaryBuff(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(700)
	maxTP := int64(700)
	instance := &model.ResourceInstance{
		CardID:      1,
		FaceUp:      true,
		Rank:        model.RankSmall,
		CurrentTP:   &tp,
		MaxTP:       &maxTP,
		Attachments: []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{
			{EffectType: "buff_tp", Value: 200, Duration: "this_turn", SourceID: "test"},
		},
	}

	field := &model.Field{}
	result := CalculateEffectiveTP(instance, field, cc)
	if result != 900 {
		t.Errorf("TP = %d, want 900 (700 + 200 buff)", result)
	}
}

func TestCalculateEffectiveAV(t *testing.T) {
	instance := &model.ResourceInstance{
		FaceUp: true,
		MaxAV:  1400,
		Damage: 500,
	}
	result := model.CalculateEffectiveAV(instance)
	if result != 900 {
		t.Errorf("AV = %d, want 900 (1400 - 500)", result)
	}
}

func TestCalculateEffectiveYield(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo:   3,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	yieldVal := int64(500)
	yieldMax := int64(500)
	instance := &model.ResourceInstance{
		CardID:           3,
		FaceUp:           true,
		Rank:             model.RankSmall,
		CurrentYield:     &yieldVal,
		MaxYield:         &yieldMax,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{}
	result := CalculateEffectiveYield(instance, field, cc)
	if result != 500 {
		t.Errorf("Yield = %d, want 500", result)
	}
}

func TestRankMultiplier(t *testing.T) {
	tests := []struct {
		rank     string
		expected int64
	}{
		{model.RankSmall, 1},
		{model.RankMedium, 2},
		{model.RankLarge, 3},
		{"unknown", 1},
	}

	for _, tt := range tests {
		got := RankMultiplier(tt.rank)
		if got != tt.expected {
			t.Errorf("RankMultiplier(%s) = %d, want %d", tt.rank, got, tt.expected)
		}
	}
}

func TestCalculateEffectiveTP_WithFamilyR(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	familyR := model.FamilyR
	tp := int64(525)
	maxTP := int64(525)
	instance := &model.ResourceInstance{
		CardID:           1,
		FaceUp:           true,
		Rank:             model.RankSmall,
		InstanceFamily:   &familyR,
		CurrentTP:        &tp,
		MaxTP:            &maxTP,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{}
	result := CalculateEffectiveTP(instance, field, cc)
	if result != 525 {
		t.Errorf("TP = %d, want 525 (700 * 0.75)", result)
	}
}

func TestCalculateMaxAV_WithFamilyR(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	familyR := model.FamilyR
	instance := &model.ResourceInstance{
		CardID:         1,
		FaceUp:         true,
		Rank:           model.RankSmall,
		InstanceFamily: &familyR,
		MaxAV:          2100,
	}

	result := CalculateMaxAV(instance, cc)
	if result != 2100 {
		t.Errorf("MaxAV = %d, want 2100 (1400 * 1.5)", result)
	}
}

func TestCalculateEffectiveTP_MediumFamilyR(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	familyR := model.FamilyR
	tp := int64(1050)
	maxTP := int64(1050)
	instance := &model.ResourceInstance{
		CardID:           1,
		FaceUp:           true,
		Rank:             model.RankMedium,
		InstanceFamily:   &familyR,
		CurrentTP:        &tp,
		MaxTP:            &maxTP,
		Attachments:      []model.AttachmentRef{},
		TemporaryEffects: []model.TemporaryEffect{},
	}

	field := &model.Field{}
	result := CalculateEffectiveTP(instance, field, cc)
	if result != 1050 {
		t.Errorf("TP = %d, want 1050 (700 * 2 * 0.75)", result)
	}
}

func TestInstanceFamilyMultiplier(t *testing.T) {
	tests := []struct {
		name       string
		family     *string
		wantTPMult float64
		wantAVMult float64
	}{
		{"nil", nil, 1.0, 1.0},
		{"M", strPtr(model.FamilyM), 1.0, 1.0},
		{"C", strPtr(model.FamilyC), 1.5, 0.75},
		{"R", strPtr(model.FamilyR), 0.75, 1.5},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			tpMult, avMult := InstanceFamilyMultiplier(tt.family)
			if tpMult != tt.wantTPMult {
				t.Errorf("tpMult = %f, want %f", tpMult, tt.wantTPMult)
			}
			if avMult != tt.wantAVMult {
				t.Errorf("avMult = %f, want %f", avMult, tt.wantAVMult)
			}
		})
	}
}

func strPtr(s string) *string {
	return &s
}

// --- RecalculateMaxYield ---

func TestRecalculateMaxYield_Base(t *testing.T) {
	resource := &model.ResourceInstance{FaceUp: true, Rank: model.RankSmall}
	card := &model.CardDefinition{
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	}
	got := RecalculateMaxYield(resource, card)
	if got != 500 {
		t.Errorf("RecalculateMaxYield = %d, want 500", got)
	}
}

func TestRecalculateMaxYield_WithRankAndFamily(t *testing.T) {
	familyC := model.FamilyC
	resource := &model.ResourceInstance{FaceUp: true, Rank: model.RankMedium, InstanceFamily: &familyC}
	card := &model.CardDefinition{
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	}
	// 500 * 2 (medium) * 1.5 (C) = 1500
	got := RecalculateMaxYield(resource, card)
	if got != 1500 {
		t.Errorf("RecalculateMaxYield = %d, want 1500 (500*2*1.5)", got)
	}
}

func TestRecalculateMaxYield_WithYieldMax(t *testing.T) {
	resource := &model.ResourceInstance{FaceUp: true, Rank: model.RankMedium}
	card := &model.CardDefinition{
		Stats: json.RawMessage(`{"yield": 500, "yield_max": 800, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	}
	// yield_max (800) * 2 (medium) = 1600
	got := RecalculateMaxYield(resource, card)
	if got != 1600 {
		t.Errorf("RecalculateMaxYield = %d, want 1600 (800*2)", got)
	}
}

// --- CalculatePassiveTPBonus ---

func TestCalculatePassiveTPBonus_PerBackendDB(t *testing.T) {
	cc := cache.NewCardCache()

	// The card with the passive: TP per backend DB of faction SD
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Compute",
		Faction:  "SD",
		PassiveEffects: []model.PassiveEffect{
			{
				Type: model.PassiveTPPerBackendDB,
				Params: map[string]interface{}{
					"faction":        "SD",
					"bonus_per_card": float64(200),
				},
			},
		},
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Two SD Database cards in backend
	cc.InjectForTest(201, &model.CardDefinition{
		CardNo:   201,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})
	cc.InjectForTest(202, &model.CardDefinition{
		CardNo:   202,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "inst-201", CardID: 201},
			{FaceUp: true, InstanceID: "inst-202", CardID: 202},
		},
	}

	bonus := CalculatePassiveTPBonus(instance, field, cc)
	if bonus != 400 {
		t.Errorf("TP bonus = %d, want 400 (2 DBs × 200)", bonus)
	}
}

func TestCalculatePassiveTPBonus_PerBackendData(t *testing.T) {
	cc := cache.NewCardCache()

	// Card with passive: TP per backend data resource (DB + ObjectStorage)
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Compute",
		Faction:  "Tenki",
		PassiveEffects: []model.PassiveEffect{
			{
				Type: model.PassiveTPPerBackendData,
				Params: map[string]interface{}{
					"faction":        "Tenki",
					"bonus_per_card": float64(150),
				},
			},
		},
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// One Tenki Database and one Tenki ObjectStorage in backend
	cc.InjectForTest(201, &model.CardDefinition{
		CardNo:   201,
		CardType: "Database",
		Faction:  "Tenki",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})
	cc.InjectForTest(202, &model.CardDefinition{
		CardNo:   202,
		CardType: "ObjectStorage",
		Faction:  "Tenki",
		Stats:    json.RawMessage(`{"yield": 300, "availability": 1000, "deploy_cost": 300, "sla_penalty": 300}`),
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "inst-201", CardID: 201},
			{FaceUp: true, InstanceID: "inst-202", CardID: 202},
		},
	}

	bonus := CalculatePassiveTPBonus(instance, field, cc)
	if bonus != 300 {
		t.Errorf("TP bonus = %d, want 300 (1 DB + 1 ObjectStorage = 2 × 150)", bonus)
	}
}

func TestCalculatePassiveTPBonus_IfCardTypeOnField(t *testing.T) {
	cc := cache.NewCardCache()

	// Card with passive: TP bonus if a Database card exists on field
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Serverless",
		Faction:  "SD",
		PassiveEffects: []model.PassiveEffect{
			{
				Type: model.PassiveTPIfCardTypeOnField,
				Params: map[string]interface{}{
					"faction":    "SD",
					"card_types": []interface{}{"Database"},
					"flat_bonus": float64(300),
				},
			},
		},
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// An SD Database card on the field
	cc.InjectForTest(201, &model.CardDefinition{
		CardNo:   201,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	// With a matching Database on the backend
	fieldWithDB := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "inst-201", CardID: 201},
		},
	}

	bonus := CalculatePassiveTPBonus(instance, fieldWithDB, cc)
	if bonus != 300 {
		t.Errorf("TP bonus = %d, want 300 (Database on field)", bonus)
	}

	// Without any matching card on the field
	fieldEmpty := &model.Field{}

	bonus = CalculatePassiveTPBonus(instance, fieldEmpty, cc)
	if bonus != 0 {
		t.Errorf("TP bonus = %d, want 0 (no Database on field)", bonus)
	}
}

func TestCalculatePassiveTPBonus_NoPassive(t *testing.T) {
	cc := cache.NewCardCache()

	// Card with no passive effects
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "inst-201", CardID: 201},
		},
	}

	bonus := CalculatePassiveTPBonus(instance, field, cc)
	if bonus != 0 {
		t.Errorf("TP bonus = %d, want 0 (no passive effects)", bonus)
	}
}

// --- CalculatePassiveYieldBonus ---

func TestCalculatePassiveYieldBonus_PerOtherDB(t *testing.T) {
	cc := cache.NewCardCache()

	// The card with passive: Yield per other DB of same faction (like #8 SD DistributedDB)
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Database",
		Faction:  "SD",
		PassiveEffects: []model.PassiveEffect{
			{
				Type: model.PassiveYieldPerOtherDB,
				Params: map[string]interface{}{
					"faction":        "SD",
					"bonus_per_card": float64(100),
				},
			},
		},
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	// Two other SD DB cards in backend
	cc.InjectForTest(201, &model.CardDefinition{
		CardNo:   201,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})
	cc.InjectForTest(202, &model.CardDefinition{
		CardNo:   202,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			instance,
			{FaceUp: true, InstanceID: "inst-201", CardID: 201},
			{FaceUp: true, InstanceID: "inst-202", CardID: 202},
		},
	}

	bonus := CalculatePassiveYieldBonus(instance, field, cc)
	// Self (inst-100) is excluded by calculateYieldPerOtherDB, so count = 2
	if bonus != 200 {
		t.Errorf("Yield bonus = %d, want 200 (2 other DBs × 100, self excluded)", bonus)
	}
}

func TestCalculatePassiveYieldBonus_IfCardOnField(t *testing.T) {
	cc := cache.NewCardCache()

	// Card with passive: Yield bonus if specific card_no exists on field (like #124 SD Cache)
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "CacheDB",
		Faction:  "SD",
		PassiveEffects: []model.PassiveEffect{
			{
				Type: model.PassiveYieldIfCardOnField,
				Params: map[string]interface{}{
					"specific_card_nos": []interface{}{float64(201)},
					"flat_bonus":        float64(400),
				},
			},
		},
		Stats: json.RawMessage(`{"yield": 300, "availability": 1000, "deploy_cost": 300, "sla_penalty": 300}`),
	})

	// The specific card that triggers the bonus
	cc.InjectForTest(201, &model.CardDefinition{
		CardNo:   201,
		CardType: "Database",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	// With matching card on field
	fieldWith := &model.Field{
		Backend: [3]*model.ResourceInstance{
			instance,
			{FaceUp: true, InstanceID: "inst-201", CardID: 201},
		},
	}

	bonus := CalculatePassiveYieldBonus(instance, fieldWith, cc)
	if bonus != 400 {
		t.Errorf("Yield bonus = %d, want 400 (specific card on field)", bonus)
	}

	// Without the specific card
	fieldWithout := &model.Field{
		Backend: [3]*model.ResourceInstance{
			instance,
		},
	}

	bonus = CalculatePassiveYieldBonus(instance, fieldWithout, cc)
	if bonus != 0 {
		t.Errorf("Yield bonus = %d, want 0 (specific card not on field)", bonus)
	}
}

// --- CalculateAttachmentBonus ---

func TestCalculateAttachmentBonus_TPBonus(t *testing.T) {
	cc := cache.NewCardCache()

	// Attachment card that gives TP +200
	cc.InjectForTest(300, &model.CardDefinition{
		CardNo:   300,
		CardType: "Attachment",
		AttachmentEffects: []model.AttachmentEffect{
			{
				Type: model.AttachmentStatBonus,
				Params: map[string]interface{}{
					"stat_type": "tp",
					"bonus":     float64(200),
				},
			},
		},
	})

	instance := &model.ResourceInstance{
		InstanceID: "inst-100",
		CardID:     100,
		FaceUp:     true,
		Attachments: []model.AttachmentRef{
			{InstanceID: "att-300", CardID: 300},
		},
	}

	bonus := CalculateAttachmentBonus(instance, "tp", cc)
	if bonus != 200 {
		t.Errorf("Attachment TP bonus = %d, want 200", bonus)
	}
}

func TestCalculateAttachmentBonus_YieldBonus(t *testing.T) {
	cc := cache.NewCardCache()

	// Attachment card that gives Yield +400
	cc.InjectForTest(300, &model.CardDefinition{
		CardNo:   300,
		CardType: "Attachment",
		AttachmentEffects: []model.AttachmentEffect{
			{
				Type: model.AttachmentStatBonus,
				Params: map[string]interface{}{
					"stat_type": "yield",
					"bonus":     float64(400),
				},
			},
		},
	})

	instance := &model.ResourceInstance{
		InstanceID: "inst-100",
		CardID:     100,
		FaceUp:     true,
		Attachments: []model.AttachmentRef{
			{InstanceID: "att-300", CardID: 300},
		},
	}

	bonus := CalculateAttachmentBonus(instance, "yield", cc)
	if bonus != 400 {
		t.Errorf("Attachment Yield bonus = %d, want 400", bonus)
	}
}

func TestCalculateAttachmentBonus_NoAttachments(t *testing.T) {
	cc := cache.NewCardCache()

	instance := &model.ResourceInstance{
		InstanceID:  "inst-100",
		CardID:      100,
		FaceUp:      true,
		Attachments: []model.AttachmentRef{},
	}

	bonus := CalculateAttachmentBonus(instance, "tp", cc)
	if bonus != 0 {
		t.Errorf("Attachment bonus = %d, want 0 (no attachments)", bonus)
	}
}

// --- CalculatePlatformBonus (Yield / AV) ---

func TestCalculatePlatformBonus_Yield(t *testing.T) {
	cc := cache.NewCardCache()

	// Target card on field
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Database",
		Faction:  "Tenki",
		Stats:    json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	// Platform card with Yield bonus
	cc.InjectForTest(400, &model.CardDefinition{
		CardNo:   400,
		CardType: "Platform",
		Faction:  "Tenki",
		PlatformEffects: []model.PlatformEffect{
			{
				Type: model.PlatformYieldBonus,
				Params: map[string]interface{}{
					"target_faction":    "Tenki",
					"target_card_types": []interface{}{"Database", "CacheDB"},
					"bonus":             float64(300),
				},
			},
		},
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat-400", CardID: 400},
		},
	}

	bonus := CalculatePlatformBonus(instance, field, "yield", cc)
	if bonus != 300 {
		t.Errorf("Platform Yield bonus = %d, want 300", bonus)
	}
}

func TestCalculatePlatformBonus_AV(t *testing.T) {
	cc := cache.NewCardCache()

	// Target card on field
	cc.InjectForTest(100, &model.CardDefinition{
		CardNo:   100,
		CardType: "Compute",
		Faction:  "Sugar",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	// Platform card with AV bonus
	cc.InjectForTest(400, &model.CardDefinition{
		CardNo:   400,
		CardType: "Platform",
		Faction:  "Sugar",
		PlatformEffects: []model.PlatformEffect{
			{
				Type: model.PlatformAVBonus,
				Params: map[string]interface{}{
					"target_faction": "Sugar",
					"bonus":          float64(500),
				},
			},
		},
	})

	instance := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "inst-100",
		CardID:     100,
	}

	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat-400", CardID: 400},
		},
	}

	bonus := CalculatePlatformBonus(instance, field, "av", cc)
	if bonus != 500 {
		t.Errorf("Platform AV bonus = %d, want 500", bonus)
	}
}

// --- EffectiveTP / EffectiveYield with ElasticBonus ---

func TestEffectiveTP_WithElasticBonus(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Elastic Container",
		Faction:  "SD",
		CardType: "Container",
		Elastic:  true,
		Stats:    json.RawMessage(`{"throughput": 600, "availability": 1300, "maintenance_cost": 50, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	tp := int64(600)
	maxTP := int64(1200)
	instance := &model.ResourceInstance{
		FaceUp:       true,
		InstanceID:   "c1",
		CardID:       1,
		Rank:         model.RankSmall,
		CurrentTP:    &tp,
		MaxTP:        &maxTP,
		CurrentAV:    1300,
		MaxAV:        1300,
		ElasticBonus: 200,
		Attachments:  []model.AttachmentRef{},
	}

	field := &model.Field{}

	// EffectiveTP = base(600) + ElasticBonus(200) = 800
	got := CalculateEffectiveTP(instance, field, cc)
	if got != 800 {
		t.Errorf("CalculateEffectiveTP = %d, want 800 (base 600 + elastic 200)", got)
	}
}

func TestEffectiveYield_WithElasticBonus(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Elastic DB",
		Faction:  "SD",
		CardType: "Database",
		Elastic:  true,
		Stats:    json.RawMessage(`{"yield": 300, "availability": 1400, "deploy_cost": 300, "sla_penalty": 400}`),
	})

	yield := int64(300)
	instance := &model.ResourceInstance{
		FaceUp:       true,
		InstanceID:   "db1",
		CardID:       1,
		Rank:         model.RankSmall,
		CurrentYield: &yield,
		CurrentAV:    1400,
		MaxAV:        1400,
		ElasticBonus: 100,
		Attachments:  []model.AttachmentRef{},
	}

	field := &model.Field{}

	// EffectiveYield = base(300) + ElasticBonus(100) = 400
	got := CalculateEffectiveYield(instance, field, cc)
	if got != 400 {
		t.Errorf("CalculateEffectiveYield = %d, want 400 (base 300 + elastic 100)", got)
	}
}

func TestEffectiveTP_WithElasticBonus_LnDiminishing(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Elastic Container",
		Faction:  "SD",
		CardType: "Container",
		Elastic:  true,
		FreeTier: 500,
		Stats:    json.RawMessage(`{"throughput": 500, "availability": 1300, "maintenance_cost": 50, "sla_penalty": 400}`),
	})

	tp := int64(500)
	instance := &model.ResourceInstance{
		FaceUp:       true,
		InstanceID:   "c1",
		CardID:       1,
		Rank:         model.RankSmall,
		CurrentTP:    &tp,
		CurrentAV:    1300,
		MaxAV:        1300,
		ElasticBonus: 500,
		Attachments:  []model.AttachmentRef{},
	}

	field := &model.Field{}

	// EffectiveTP = 500 + effectiveEB(500, 500) = 500 + int64(500*ln(2)) = 500 + 346 = 846
	got := CalculateEffectiveTP(instance, field, cc)
	if got != 846 {
		t.Errorf("CalculateEffectiveTP = %d, want 846 (500 base + 346 effective elastic)", got)
	}
}

func TestEffectiveYield_WithElasticBonus_LnDiminishing(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardName: "Elastic DB",
		Faction:  "SD",
		CardType: "Database",
		Elastic:  true,
		FreeTier: 300,
		Stats:    json.RawMessage(`{"yield": 300, "availability": 1400, "sla_penalty": 400}`),
	})

	yield := int64(300)
	instance := &model.ResourceInstance{
		FaceUp:       true,
		InstanceID:   "db1",
		CardID:       1,
		Rank:         model.RankSmall,
		CurrentYield: &yield,
		CurrentAV:    1400,
		MaxAV:        1400,
		ElasticBonus: 300,
		Attachments:  []model.AttachmentRef{},
	}

	field := &model.Field{}

	// EffectiveYield = 300 + effectiveEB(300, 300) = 300 + int64(300*ln(2)) = 300 + 207 = 507
	got := CalculateEffectiveYield(instance, field, cc)
	if got != 507 {
		t.Errorf("CalculateEffectiveYield = %d, want 507 (300 base + 207 effective elastic)", got)
	}
}

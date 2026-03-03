package effect

import (
	"encoding/json"
	"fmt"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

func newEffectTestState() *model.GameState {
	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{1, 2, 3, 4, 5})
	emptyTrash, _ := json.Marshal([]int64{})

	return &model.GameState{
		GameID:            "effect-test",
		Version:           1,
		CurrentTurn:       2,
		CurrentPhase:      model.PhaseMain,
		ActivePlayer:      1,
		Player1Budget:     5000,
		Player1InsightPool:     500,
		Player1Field:      emptyField,
		Player1Hand:       emptyHand,
		Player1Repository: emptyRepo,
		Player1Trash:      emptyTrash,
		Player1TimeBank:   480,
		Player2Budget:     5000,
		Player2InsightPool:     500,
		Player2Field:      emptyField,
		Player2Hand:       emptyHand,
		Player2Repository: emptyRepo,
		Player2Trash:      emptyTrash,
		Player2TimeBank:   480,
	}
}

func newEffectTestGame() *model.Game {
	return &model.Game{
		GameID:    "effect-test",
		Player1ID: "player1",
		Player2ID: "player2",
		Status:    model.GameStatusPlaying,
	}
}

// testHandler returns an EffectHandler from the registry by card number and trigger type.
func testHandler(t *testing.T, cardNo int64, trigger TriggerType) EffectHandler {
	t.Helper()
	reg := NewEffectRegistry()
	RegisterAllEffects(reg)
	registration, ok := reg.Get(cardNo, trigger)
	if !ok {
		t.Fatalf("card %d with trigger %s not registered", cardNo, trigger)
	}
	return registration.Handler
}

// --- Op: GainBudget ---

func TestBudgetGainHandler(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	handler := Compose(GainBudget{Self, Static(400)})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	result, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}

	if state.GetBudget(1) != 5400 {
		t.Errorf("budget = %d, want 5400", state.GetBudget(1))
	}
}

// --- Op: AbsorbInsight ---

func TestInsightAbsorbHandler(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	handler := Compose(AbsorbInsight{Static(300)})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	result, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}

	if state.GetInsightPool(1) != 800 { // 500 + 300
		t.Errorf("player 1 insight = %d, want 800", state.GetInsightPool(1))
	}
	if state.GetInsightPool(2) != 200 { // 500 - 300
		t.Errorf("player 2 insight = %d, want 200", state.GetInsightPool(2))
	}
}

func TestInsightAbsorbHandler_PartialAbsorb(t *testing.T) {
	state := newEffectTestState()
	state.Player2InsightPool = 100 // Less than absorb amount
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	handler := Compose(AbsorbInsight{Static(300)})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetInsightPool(1) != 600 { // 500 + 100 (only 100 available)
		t.Errorf("player 1 insight = %d, want 600", state.GetInsightPool(1))
	}
	if state.GetInsightPool(2) != 0 {
		t.Errorf("player 2 insight = %d, want 0", state.GetInsightPool(2))
	}
}

// --- Op: SearchRepo ---

func TestSearchHandler(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	handler := Compose(SearchRepo{"SD"})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 1})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Card should be moved from repo to hand
	repo, _ := state.GetRepository(1)
	for _, c := range repo {
		if c == 1 {
			t.Error("card 1 should be removed from repository")
		}
	}

	hand, _ := state.GetHand(1)
	found := false
	for _, c := range hand {
		if c.CardID == 1 {
			found = true
			break
		}
	}
	if !found {
		t.Error("card 1 should be in hand")
	}
}

func TestSearchHandler_WrongFaction(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "Tenki", // Wrong faction
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	handler := Compose(SearchRepo{"SD"})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 1})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := handler(ctx)
	if err == nil {
		t.Fatal("expected error for wrong faction")
	}
}

// --- Op: SurviveDestruction ---

func TestSurviveDestructionHandler(t *testing.T) {
	handler := Compose(SurviveDestruction{200})

	target := &model.ResourceInstance{
		FaceUp: true,
		InstanceID: "target1",
		CurrentAV:  1000,
		MaxAV:      1000,
		Damage:     1200, // Would be destroyed (AV = -200)
	}

	ctx := &EffectContext{
		Target: target,
	}

	result, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if !result.CancelAction {
		t.Error("should cancel action (destruction)")
	}

	// Damage should be reset so that AV = 200
	expectedDamage := int64(1000 - 200) // maxAV - surviveAV
	if target.Damage != expectedDamage {
		t.Errorf("damage = %d, want %d", target.Damage, expectedDamage)
	}
}

// --- Incident Damage Reduction ---

func TestApplyIncidentDamageReduction_NoReduction(t *testing.T) {
	cc := cache.NewCardCache()
	target := &model.ResourceInstance{
		FaceUp: true,
		InstanceID:  "t1",
		Attachments: []model.AttachmentRef{},
	}
	field := &model.Field{}

	result := applyIncidentDamageReduction(900, target, field, cc)
	if result != 900 {
		t.Errorf("damage = %d, want 900", result)
	}
}

func TestApplyIncidentDamageReduction_WithSecurityGroup(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(16, &model.CardDefinition{
		CardNo:   16,
		CardType: "Attachment",
	})

	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "t1",
		Attachments: []model.AttachmentRef{
			{InstanceID: "att1", CardID: 16},
		},
	}
	field := &model.Field{}

	result := applyIncidentDamageReduction(900, target, field, cc)
	if result != 700 { // 900 - 200
		t.Errorf("damage = %d, want 700", result)
	}
}

func TestApplyIncidentDamageReduction_WithISMSCert(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(96, &model.CardDefinition{
		CardNo:   96,
		CardType: "Platform",
	})

	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID:  "t1",
		Attachments: []model.AttachmentRef{},
	}
	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 96},
		},
	}

	result := applyIncidentDamageReduction(900, target, field, cc)
	if result != 700 { // 900 - 200
		t.Errorf("damage = %d, want 700", result)
	}
}

func TestApplyIncidentDamageReduction_MultipleReductions(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(16, &model.CardDefinition{CardNo: 16, CardType: "Attachment"})
	cc.InjectForTest(96, &model.CardDefinition{CardNo: 96, CardType: "Platform"})

	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "t1",
		Attachments: []model.AttachmentRef{
			{InstanceID: "att1", CardID: 16},
		},
	}
	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 96},
		},
	}

	result := applyIncidentDamageReduction(900, target, field, cc)
	if result != 500 { // 900 - 200 (SG) - 200 (ISMS) = 500
		t.Errorf("damage = %d, want 500", result)
	}
}

func TestApplyIncidentDamageReduction_FloorAtZero(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(16, &model.CardDefinition{CardNo: 16, CardType: "Attachment"})
	cc.InjectForTest(96, &model.CardDefinition{CardNo: 96, CardType: "Platform"})

	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "t1",
		Attachments: []model.AttachmentRef{
			{InstanceID: "att1", CardID: 16},
		},
	}
	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 96},
		},
	}

	result := applyIncidentDamageReduction(200, target, field, cc)
	if result != 0 { // 200 - 200 - 200 = -200, floored to 0
		t.Errorf("damage = %d, want 0", result)
	}
}

// --- Neutral Effect Tests ---

func TestNeutralCloudFunding(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	result, err := testHandler(t, 102, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if result == nil {
		t.Fatal("result should not be nil")
	}

	if state.GetBudget(1) != 6000 { // +1000
		t.Errorf("player 1 budget = %d, want 6000", state.GetBudget(1))
	}
	if state.GetBudget(2) != 5000 { // unchanged
		t.Errorf("player 2 budget = %d, want 5000", state.GetBudget(2))
	}
}

func TestNeutralVentureCapital_Allowed(t *testing.T) {
	state := newEffectTestState()
	state.Player1Budget = 800 // ≤ 1000
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 120, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetBudget(1) != 1700 { // 800 + 900
		t.Errorf("budget = %d, want 1700", state.GetBudget(1))
	}
}

func TestNeutralVentureCapital_Blocked(t *testing.T) {
	state := newEffectTestState()
	state.Player1Budget = 1500 // > 1000
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 120, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for budget > 1000")
	}
}

func TestNeutralRateLimiter(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(1100)
	target := &model.ResourceInstance{
		InstanceID: "target-ai",
		CardID:     5,
		FaceUp:     true,
		MaxTP:      &tp,
	}
	cc.InjectForTest(5, &model.CardDefinition{
		CardNo:   5,
		CardType: "AI/ML",
		Faction:  "SD",
	})

	// PlayerNum=1 (reactive owner), deployer = opponent = player2
	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Target:    target,
		CardCache: cc,
	}

	result, err := testHandler(t, 113, TriggerOnEnemyDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if result.CancelAction {
		t.Error("rate limiter should not cancel the deploy action")
	}
	if !model.HasTemporaryEffect(target, "cannot_operate") {
		t.Error("rate limiter should apply cannot_operate to target")
	}
}

func TestNeutralRateLimiter_LowTP(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(700)
	target := &model.ResourceInstance{
		InstanceID: "target-compute",
		CardID:     1,
		FaceUp:     true,
		MaxTP:      &tp,
	}
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
	})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Target:    target,
		CardCache: cc,
	}

	_, err := testHandler(t, 113, TriggerOnEnemyDeploy)(ctx)
	if err == nil {
		t.Error("rate limiter should return error (guard) for TP < 900")
	}
}

func TestNeutralCloudEngineer_DrawNonTuners(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "SD",
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 98, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Card should go to hand (not Tuners)
	hand, _ := state.GetHand(1)
	if len(hand) != 1 {
		t.Errorf("hand size = %d, want 1", len(hand))
	}

	// Repo should have 4 cards
	repo, _ := state.GetRepository(1)
	if len(repo) != 4 {
		t.Errorf("repo size = %d, want 4", len(repo))
	}
}

func TestNeutralCloudEngineer_DrawTuners(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	cc.InjectForTest(1, &model.CardDefinition{
		CardNo:   1,
		CardType: "Compute",
		Faction:  "Tuners", // Tuners card
		Stats:    json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 98, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Card should go to trash (Tuners)
	hand, _ := state.GetHand(1)
	if len(hand) != 0 {
		t.Errorf("hand size = %d, want 0 (Tuners card goes to trash)", len(hand))
	}

	trash, _ := state.GetTrash(1)
	if len(trash) != 1 {
		t.Errorf("trash size = %d, want 1", len(trash))
	}
}

// --- Compliance Audit Tests ---

func TestNeutralComplianceAudit_WithSecurity(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	cc.InjectForTest(15, &model.CardDefinition{
		CardNo:   15,
		CardType: "Platform",
		Faction:  "SD",
	})

	// Opponent has a security platform (Smile Firewall #15)
	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 15},
		},
	}
	_ = state.SetField(2, oppField)

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 112, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Standard penalty: 400
	if state.GetBudget(2) != 4600 { // 5000 - 400
		t.Errorf("opponent budget = %d, want 4600", state.GetBudget(2))
	}
}

func TestNeutralComplianceAudit_WithoutSecurity(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// No security platform
	_ = state.SetField(2, &model.Field{})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 112, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Enhanced penalty: 800 (400 base + 400 additional)
	if state.GetBudget(2) != 4200 { // 5000 - 800
		t.Errorf("opponent budget = %d, want 4200", state.GetBudget(2))
	}
}

// --- Field Scanning Helper Tests ---

func TestCountFactionCards(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "SD"})
	cc.InjectForTest(3, &model.CardDefinition{CardNo: 3, CardType: "Compute", Faction: "Tenki"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp},
			{FaceUp: true, InstanceID: "f2", CardID: 3, CurrentTP: &tp},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2},
		},
	}

	count := CountFactionCards(field, "SD", cc)
	if count != 2 {
		t.Errorf("SD count = %d, want 2", count)
	}

	count = CountFactionCards(field, "Tenki", cc)
	if count != 1 {
		t.Errorf("Tenki count = %d, want 1", count)
	}
}

func TestHasCardOnField(t *testing.T) {
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3},
		},
	}

	if !HasCardOnField(field, 1) {
		t.Error("should find card 1 on field")
	}
	if !HasCardOnField(field, 3) {
		t.Error("should find card 3 on field")
	}
	if HasCardOnField(field, 99) {
		t.Error("should not find card 99 on field")
	}
}

// --- Effect Registry Tests ---

func TestEffectRegistryRegisterAndGet(t *testing.T) {
	reg := NewEffectRegistry()

	called := false
	reg.Register(999, TriggerActivate, func(ctx *EffectContext) (*EffectResult, error) {
		called = true
		return &EffectResult{}, nil
	})

	registration, ok := reg.Get(999, TriggerActivate)
	if !ok {
		t.Fatal("effect should be registered")
	}

	_, err := registration.Handler(&EffectContext{})
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if !called {
		t.Error("handler should have been called")
	}
}

func TestEffectRegistryGetByTrigger(t *testing.T) {
	reg := NewEffectRegistry()
	reg.Register(999, TriggerDeploy, func(ctx *EffectContext) (*EffectResult, error) {
		return &EffectResult{}, nil
	})

	// Correct trigger
	_, ok := reg.Get(999, TriggerDeploy)
	if !ok {
		t.Error("should find effect with matching trigger")
	}

	// Wrong trigger
	_, ok = reg.Get(999, TriggerReactive)
	if ok {
		t.Error("should not find effect with wrong trigger")
	}

	// Non-existent
	_, ok = reg.Get(9999, TriggerDeploy)
	if ok {
		t.Error("should not find non-existent effect")
	}
}

func TestRegisterAllEffects(t *testing.T) {
	reg := NewEffectRegistry()
	RegisterAllEffects(reg)

	// Spot check some key effects exist
	checks := []struct {
		cardNo      int64
		triggerType TriggerType
	}{
		{19, TriggerActivate},
		{101, TriggerActivate},
		{42, TriggerActivate},
		{65, TriggerActivate},
		{98, TriggerActivate},
		{104, TriggerActivate},
		{113, TriggerOnEnemyDeploy},
		{135, TriggerOnEnemyDeploy},
	}

	for _, check := range checks {
		registration, ok := reg.Get(check.cardNo, check.triggerType)
		if !ok {
			t.Errorf("card %d with trigger %s not registered", check.cardNo, check.triggerType)
		}
		if registration == nil {
			t.Errorf("registration for card %d is nil", check.cardNo)
		}
	}
}

// ====================================================================
// Pattern-Based Tests: Budget/DV Manipulation
// ====================================================================

// --- SD Reserved Instance: deploy cost refund ---

func TestSDReservedInstance_UseRefund(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	source := &model.ResourceInstance{
		InstanceID:       "src1",
		CardID:           7,
		FaceUp:           true,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	choiceData, _ := json.Marshal(ChoiceOption{Option: "use"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Source:     source,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 7, TriggerDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetBudget(1) != 5200 { // 5000 + 200 refund
		t.Errorf("budget = %d, want 5200", state.GetBudget(1))
	}

	// Check permanent restriction was applied
	found := false
	for _, eff := range source.TemporaryEffects {
		if eff.EffectType == "reserved_instance" && eff.Duration == "permanent" {
			found = true
		}
	}
	if !found {
		t.Error("reserved_instance restriction not applied")
	}
}

func TestSDReservedInstance_Skip(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	source := &model.ResourceInstance{
		InstanceID:       "src1",
		CardID:           7,
		FaceUp:           true,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	choiceData, _ := json.Marshal(ChoiceOption{Option: "skip"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Source:     source,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 7, TriggerDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetBudget(1) != 5000 { // No change
		t.Errorf("budget = %d, want 5000", state.GetBudget(1))
	}
	if len(source.TemporaryEffects) != 0 {
		t.Error("no restriction should be applied on skip")
	}
}

// --- SD On-Demand: pay 400, double yield gen ---

func TestSDOnDemand_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	yieldVal := int64(500)
	source := &model.ResourceInstance{
		InstanceID:       "src1",
		CardID:           10,
		FaceUp:           true,
		CurrentYield:     &yieldVal,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Source:    source,
		CardCache: cc,
	}

	_, err := testHandler(t, 10, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetBudget(1) != 4600 { // 5000 - 400
		t.Errorf("budget = %d, want 4600", state.GetBudget(1))
	}

	// Check buff_yield was added
	found := false
	for _, eff := range source.TemporaryEffects {
		if eff.EffectType == "buff_yield" && eff.Value == 500 && eff.Duration == "this_turn" {
			found = true
		}
	}
	if !found {
		t.Error("buff_yield not applied")
	}
}

func TestSDOnDemand_InsufficientBudget(t *testing.T) {
	state := newEffectTestState()
	state.Player1Budget = 200 // < 400
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 10, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for insufficient budget")
	}
}

// --- SD Cache Engine: Memcached vs Redis ---

func TestSDCacheEngine_Memcached(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	yieldVal := int64(500)
	source := &model.ResourceInstance{
		FaceUp: true,
		InstanceID:       "src1",
		CurrentYield:     &yieldVal,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	choiceData, _ := json.Marshal(ChoiceOption{Option: "memcached"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Source:     source,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 11, TriggerDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetBudget(1) != 5400 { // +400
		t.Errorf("budget = %d, want 5400", state.GetBudget(1))
	}
}

func TestSDCacheEngine_Redis(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	yieldVal := int64(500)
	source := &model.ResourceInstance{
		FaceUp: true,
		InstanceID:       "src1",
		CurrentYield:     &yieldVal,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	choiceData, _ := json.Marshal(ChoiceOption{Option: "redis"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Source:     source,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 11, TriggerDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	found := false
	for _, eff := range source.TemporaryEffects {
		if eff.EffectType == "buff_yield" && eff.Value == 200 && eff.Duration == "permanent" {
			found = true
		}
	}
	if !found {
		t.Error("permanent buff_yield not applied for redis")
	}
}

func TestSDCacheEngine_InvalidChoice(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	yieldVal := int64(500)
	source := &model.ResourceInstance{
		FaceUp: true,
		InstanceID:       "src1",
		CurrentYield:     &yieldVal,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	choiceData, _ := json.Marshal(ChoiceOption{Option: "invalid"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Source:     source,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 11, TriggerDeploy)(ctx)
	if err == nil {
		t.Fatal("expected error for invalid choice")
	}
}

// --- Insight Absorb: Table-driven test across all absorb amounts ---

func TestInsightAbsorbAmounts(t *testing.T) {
	tests := []struct {
		name           string
		stealAmount    int64
		oppInsight     int64
		wantMyInsight  int64
		wantOppInsight int64
	}{
		{"steal_300_full", 300, 500, 800, 200},
		{"steal_400_full", 400, 500, 900, 100},
		{"steal_600_full", 600, 800, 1100, 200},
		{"steal_300_partial", 300, 100, 600, 0},
		{"steal_600_partial", 600, 200, 700, 0},
		{"steal_from_zero", 400, 0, 500, 0},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			state := newEffectTestState()
			state.Player2InsightPool = tt.oppInsight
			game := newEffectTestGame()
			cc := cache.NewCardCache()

			handler := Compose(AbsorbInsight{Static(tt.stealAmount)})
			ctx := &EffectContext{
				State:     state,
				Game:      game,
				PlayerNum: 1,
				CardCache: cc,
			}

			_, err := handler(ctx)
			if err != nil {
				t.Fatalf("handler failed: %v", err)
			}

			if state.GetInsightPool(1) != tt.wantMyInsight {
				t.Errorf("my Insight = %d, want %d", state.GetInsightPool(1), tt.wantMyInsight)
			}
			if state.GetInsightPool(2) != tt.wantOppInsight {
				t.Errorf("opp Insight = %d, want %d", state.GetInsightPool(2), tt.wantOppInsight)
			}
		})
	}
}

// --- Sugar Realtime Sync / Streaming Insert: +200 Insight to pool ---

func TestInsightPoolGain(t *testing.T) {
	tests := []struct {
		name    string
		handler EffectHandler
	}{
		{"RealtimeSync", Compose(GainInsight{Static(200)})},
		{"StreamingInsert", Compose(GainInsight{Static(200)})},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			state := newEffectTestState()
			game := newEffectTestGame()
			cc := cache.NewCardCache()

			ctx := &EffectContext{
				State:     state,
				Game:      game,
				PlayerNum: 1,
				CardCache: cc,
			}

			_, err := tt.handler(ctx)
			if err != nil {
				t.Fatalf("handler failed: %v", err)
			}

			if state.GetInsightPool(1) != 700 { // 500 + 200
				t.Errorf("Insight pool = %d, want 700", state.GetInsightPool(1))
			}
		})
	}
}

// --- Sugar Knowledge: Steal 400 + 200 per opponent backend (max +600) ---

func TestSugarKnowledge(t *testing.T) {
	tests := []struct {
		name            string
		oppBackendCount int
		wantSteal       int64
	}{
		{"no_backend", 0, 400},
		{"one_backend", 1, 600},
		{"two_backends", 2, 800},
		{"three_backends_capped", 3, 1000}, // 400 + 600 cap
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			state := newEffectTestState()
			state.Player1InsightPool = 0
			state.Player2InsightPool = 2000 // Plenty to steal
			game := newEffectTestGame()
			cc := cache.NewCardCache()

			// Set up opponent field with backends
			oppField := &model.Field{}
			for i := 0; i < tt.oppBackendCount && i < 3; i++ {
				oppField.Backend[i] = &model.ResourceInstance{
					FaceUp:    true,
					InstanceID: fmt.Sprintf("b%d", i),
					CardID:     int64(100 + i),
				}
			}
			state.SetField(2, oppField)

			ctx := &EffectContext{
				State:     state,
				Game:      game,
				PlayerNum: 1,
				CardCache: cc,
			}

			_, err := testHandler(t, 67, TriggerActivate)(ctx)
			if err != nil {
				t.Fatalf("handler failed: %v", err)
			}

			if state.GetInsightPool(1) != tt.wantSteal {
				t.Errorf("stolen Insight = %d, want %d", state.GetInsightPool(1), tt.wantSteal)
			}
		})
	}
}

// ====================================================================
// Pattern-Based Tests: Deploy-on-Destroy / Reactive
// ====================================================================

// --- SD Versioning: return SD backend to hand on destroy ---

func TestSDVersioning_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(9, &model.CardDefinition{
		CardNo: 9, CardType: "ObjectStorage", Faction: "SD",
	})

	target := &model.ResourceInstance{FaceUp: true, InstanceID: "t1", CardID: 9}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Target:    target,
		CardCache: cc,
	}

	result, err := testHandler(t, 9, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if !result.CancelAction {
		t.Error("should cancel destruction")
	}

	hand, _ := state.GetHand(1)
	if len(hand) != 1 || hand[0].CardID != 9 {
		t.Error("card should be returned to hand")
	}
}

func TestSDVersioning_NonSD(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(99, &model.CardDefinition{
		CardNo: 99, CardType: "Database", Faction: "Tenki",
	})

	target := &model.ResourceInstance{FaceUp: true, InstanceID: "t1", CardID: 99}
	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Target:    target,
		CardCache: cc,
	}

	_, err := testHandler(t, 9, TriggerOnDestroy)(ctx)
	if err == nil {
		t.Fatal("expected error for non-SD target")
	}
}

// --- SD Recovery: if AV ≤ 400, heal 500 ---

func TestSDRecovery_LowAV(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(22, &model.CardDefinition{
		CardNo: 22, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	target := &model.ResourceInstance{
		InstanceID: "t1", CardID: 22,
		FaceUp:                   true,
		MaxAV: 1400, Damage: 1100, // AV = 300 ≤ 400
	}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Target:    target,
		CardCache: cc,
	}

	result, err := testHandler(t, 22, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if !result.CancelAction {
		t.Error("should cancel")
	}

	if target.Damage != 600 { // 1100 - 500
		t.Errorf("damage = %d, want 600", target.Damage)
	}
}

func TestSDRecovery_HighAV(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(22, &model.CardDefinition{
		CardNo: 22, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	target := &model.ResourceInstance{
		InstanceID: "t1", CardID: 22,
		FaceUp:                   true,
		MaxAV: 1400, Damage: 500, // AV = 900 > 400
	}

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, Target: target, CardCache: cc,
	}

	_, err := testHandler(t, 22, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error for AV > 400")
	}
}

func TestSDRecovery_DamageFloorAtZero(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(22, &model.CardDefinition{
		CardNo: 22, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	target := &model.ResourceInstance{
		InstanceID: "t1", CardID: 22,
		FaceUp:                   true,
		MaxAV: 1400, Damage: 1200, // AV = 200 ≤ 400
	}

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, Target: target, CardCache: cc,
	}

	_, err := testHandler(t, 22, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if target.Damage != 700 { // 1200 - 500
		t.Errorf("damage = %d, want 700", target.Damage)
	}
}

// --- Tenki Site Recovery: deploy from repo with AV 200 ---

func TestTenkiSiteRecovery(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(41, &model.CardDefinition{
		CardNo: 41, CardType: "Database", Faction: "Tenki",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	// Put card 41 in repository
	repo := []int64{41, 42, 43}
	state.SetRepository(1, repo)

	target := &model.ResourceInstance{FaceUp: true, InstanceID: "destroyed1", CardID: 41}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		Target:    target,
		CardCache: cc,
	}

	_, err := testHandler(t, 41, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Card should be deployed to field with AV 200
	field, _ := state.GetField(1)
	found := false
	for _, res := range field.Backend {
		if res != nil && res.CardID == 41 {
			if res.MaxAV != 200 {
				t.Errorf("deployed MaxAV = %d, want 200", res.MaxAV)
			}
			found = true
		}
	}
	if !found {
		t.Error("card 41 should be deployed on backend")
	}

	// Card should be removed from repo
	newRepo, _ := state.GetRepository(1)
	for _, c := range newRepo {
		if c == 41 {
			t.Error("card 41 should be removed from repository")
		}
	}
}

// --- Tenki Backup: mark for revival ---

func TestTenkiBackup(t *testing.T) {
	target := &model.ResourceInstance{
		InstanceID:       "t1",
		CardID:           38,
		FaceUp:           true,
		MaxAV:            1300,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	ctx := &EffectContext{Target: target}

	_, err := testHandler(t, 38, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	found := false
	for _, eff := range target.TemporaryEffects {
		if eff.EffectType == "pending_revival" && eff.Duration == "next_turn" {
			// 1300 / 2 = 650, rounded up to nearest 200 = 800
			if eff.Value != 800 {
				t.Errorf("revival AV = %d, want 800", eff.Value)
			}
			found = true
		}
	}
	if !found {
		t.Error("pending_revival not applied")
	}
}

func TestTenkiBackup_EvenAV(t *testing.T) {
	target := &model.ResourceInstance{
		InstanceID:       "t1",
		CardID:           38,
		FaceUp:           true,
		MaxAV:            1400,
		TemporaryEffects: []model.TemporaryEffect{},
	}

	ctx := &EffectContext{Target: target}

	_, err := testHandler(t, 38, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	for _, eff := range target.TemporaryEffects {
		if eff.EffectType == "pending_revival" {
			// 1400 / 2 = 700, already divisible by 200? 700 % 200 = 100 → round up to 800
			if eff.Value != 800 {
				t.Errorf("revival AV = %d, want 800", eff.Value)
			}
		}
	}
}

// --- Tenki Migration: return from trash to hand ---

func TestTenkiMigration_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(43, &model.CardDefinition{
		CardNo: 43, CardType: "Compute", Faction: "Tenki",
	})

	// Put card 43 in trash
	state.SetTrash(1, []int64{43, 99})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 43})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 43, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Card should be in hand
	hand, _ := state.GetHand(1)
	found := false
	for _, c := range hand {
		if c.CardID == 43 {
			found = true
		}
	}
	if !found {
		t.Error("card 43 should be in hand")
	}

	// Card should be removed from trash
	trash, _ := state.GetTrash(1)
	for _, c := range trash {
		if c == 43 {
			t.Error("card 43 should be removed from trash")
		}
	}
}

func TestTenkiMigration_NotInTrash(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(43, &model.CardDefinition{CardNo: 43, CardType: "Compute", Faction: "Tenki"})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 43})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 43, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error when card not in trash")
	}
}

func TestTenkiMigration_NonResource(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(43, &model.CardDefinition{CardNo: 43, CardType: "Strategy", Faction: "Tenki"})

	state.SetTrash(1, []int64{43})
	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 43})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 43, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for non-component card")
	}
}

// --- Tuners Data Guard: deploy Tuners DB from repo ---

func TestTunersDataGuard_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(85, &model.CardDefinition{
		CardNo: 85, CardType: "Database", Faction: "Tuners",
		Stats: json.RawMessage(`{"yield": 600, "availability": 1500, "deploy_cost": 500, "sla_penalty": 600}`),
	})

	// Put miracle DB in repo
	state.SetRepository(1, []int64{85, 10, 20})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 85, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	field, _ := state.GetField(1)
	found := false
	for _, res := range field.Backend {
		if res != nil && res.CardID == 85 {
			if res.MaxAV != 200 {
				t.Errorf("deployed MaxAV = %d, want 200", res.MaxAV)
			}
			found = true
		}
	}
	if !found {
		t.Error("Tuners DB should be deployed")
	}
}

func TestTunersDataGuard_NoTunersDBInRepo(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	// Repo has no Tuners DB
	state.SetRepository(1, []int64{1, 2, 3})

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	// Should not error, just fizzle
	_, err := testHandler(t, 85, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler should not error on fizzle: %v", err)
	}
}

// --- Tuners Failback: deploy Tuners DB from hand ---

func TestTunersFailback_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(90, &model.CardDefinition{
		CardNo: 90, CardType: "Database", Faction: "Tuners",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	// Put miracle DB in hand
	state.SetHand(1, []model.HandCard{{InstanceID: "h1", CardID: 90}})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 90})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 90, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	field, _ := state.GetField(1)
	found := false
	for _, res := range field.Backend {
		if res != nil && res.CardID == 90 {
			found = true
		}
	}
	if !found {
		t.Error("Tuners DB should be deployed from hand")
	}

	hand, _ := state.GetHand(1)
	if len(hand) != 0 {
		t.Error("hand should be empty after deploy")
	}
}

func TestTunersFailback_NonTunersDB(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 1})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 90, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error for non-Tuners DB")
	}
}

// --- Tuners License: full AV restore ---

func TestTunersLicense_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(89, &model.CardDefinition{
		CardNo: 89, CardType: "Database", Faction: "Tuners",
		Stats: json.RawMessage(`{"yield": 600, "availability": 1500, "deploy_cost": 500, "sla_penalty": 600}`),
	})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 89, MaxAV: 1500, Damage: 800},
		},
	}
	state.SetField(1, field)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "b1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 89, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)
	if updatedField.Backend[0].Damage != 0 {
		t.Errorf("damage = %d, want 0", updatedField.Backend[0].Damage)
	}
}

func TestTunersLicense_NonTunersDB(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo: 1, CardType: "Database", Faction: "SD",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 1, MaxAV: 1300, Damage: 800},
		},
	}
	state.SetField(1, field)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "b1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 89, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for non-Tuners DB target")
	}
}

func TestTunersLicense_FrontendTarget(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(89, &model.CardDefinition{
		CardNo: 89, CardType: "Compute", Faction: "Tuners",
	})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 89, CurrentTP: &tp},
		},
	}
	state.SetField(1, field)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 89, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for frontend target")
	}
}

// ====================================================================
// Pattern-Based Tests: Damage / Buff / Debuff
// ====================================================================

// --- Tuners Bare Metal: self-damage on attack ---

func TestTunersBareMetalOnAttack(t *testing.T) {
	source := &model.ResourceInstance{
		InstanceID: "src1",
		CardID:     72,
		FaceUp:     true,
		MaxAV:      2000,
		Damage:     0,
	}

	ctx := &EffectContext{Source: source}
	_, err := testHandler(t, 72, TriggerOnAttack)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if source.Damage != 300 {
		t.Errorf("damage = %d, want 300", source.Damage)
	}
}

func TestTunersBareMetalOnAttack_Accumulates(t *testing.T) {
	source := &model.ResourceInstance{
		InstanceID: "src1",
		CardID:     72,
		FaceUp:     true,
		MaxAV:      2000,
		Damage:     500,
	}

	ctx := &EffectContext{Source: source}
	testHandler(t, 72, TriggerOnAttack)(ctx)

	if source.Damage != 800 { // 500 + 300
		t.Errorf("damage = %d, want 800", source.Damage)
	}
}

// --- Sugar Cascade Failure: 400 damage to own backends ---

func TestSugarCascadeFailure(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(52, &model.CardDefinition{
		CardNo: 52, CardType: "Database", Faction: "Sugar",
		Stats: json.RawMessage(`{"yield": 600, "availability": 1500, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 52, MaxAV: 1500, Damage: 0},
			{FaceUp: true, InstanceID: "b2", CardID: 52, MaxAV: 1500, Damage: 0},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 52, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)
	for i, res := range updatedField.Backend {
		if res != nil && res.Damage != 400 {
			t.Errorf("backend[%d] damage = %d, want 400", i, res.Damage)
		}
	}
}

func TestSugarCascadeFailure_DestroyWeakBackend(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(52, &model.CardDefinition{
		CardNo: 52, CardType: "Database", Faction: "Sugar",
		Stats: json.RawMessage(`{"yield": 600, "availability": 1500, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 52, MaxAV: 1500, Damage: 1200}, // AV = 300, +400 → 0
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	testHandler(t, 52, TriggerOnDestroy)(ctx)

	updatedField, _ := state.GetField(1)
	if updatedField.Backend[0] != nil {
		t.Error("backend[0] should be destroyed (AV ≤ 0)")
	}

	trash, _ := state.GetTrash(1)
	found := false
	for _, c := range trash {
		if c == 52 {
			found = true
		}
	}
	if !found {
		t.Error("destroyed card should be in trash")
	}
}

// --- Tenki Windy Update: 400 damage to all own Tenki cards ---

func TestTenkiWindyUpdate(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "Tenki"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "Tenki"})
	cc.InjectForTest(3, &model.CardDefinition{CardNo: 3, CardType: "Compute", Faction: "SD"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, MaxAV: 1400, Damage: 0},
			{FaceUp: true, InstanceID: "f2", CardID: 3, CurrentTP: &tp, MaxAV: 1400, Damage: 0},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2, MaxAV: 1300, Damage: 0},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 123, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)

	// Tenki frontend: +400
	if updatedField.Frontend[0] != nil && updatedField.Frontend[0].Damage != 400 {
		t.Errorf("Tenki frontend damage = %d, want 400", updatedField.Frontend[0].Damage)
	}

	// SD frontend: untouched
	if updatedField.Frontend[1] != nil && updatedField.Frontend[1].Damage != 0 {
		t.Errorf("SD frontend damage = %d, want 0", updatedField.Frontend[1].Damage)
	}

	// Tenki backend: +400
	if updatedField.Backend[0] != nil && updatedField.Backend[0].Damage != 400 {
		t.Errorf("Tenki backend damage = %d, want 400", updatedField.Backend[0].Damage)
	}
}

// --- Sugar Veloce Batch: double TP ---

func TestSugarVeloceBatch(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(66, &model.CardDefinition{
		CardNo: 66, CardType: "Compute", Faction: "Sugar",
		Stats: json.RawMessage(`{"throughput": 800, "availability": 1600, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	tp := int64(800)
	maxTP := int64(800)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 66, CurrentTP: &tp, MaxTP: &maxTP, MaxAV: 1600, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(1, field)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 66, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)
	found := false
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "buff_tp" && eff.Value == 800 && eff.Duration == "this_turn" {
			found = true
		}
	}
	if !found {
		t.Error("buff_tp (doubling) not applied")
	}
}

func TestSugarVeloceBatch_NonComputeTarget(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(66, &model.CardDefinition{
		CardNo: 66, CardType: "Database", Faction: "Sugar",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 66, MaxAV: 1300},
		},
	}
	state.SetField(1, field)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "b1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 66, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for non-frontend target")
	}
}

// --- Tenki Failer: Tenki frontends cannot attack ---

func TestTenkiFailer(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "Tenki"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Compute", Faction: "SD"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
			{FaceUp: true, InstanceID: "f2", CardID: 2, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 122, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)

	// Tenki frontend should have "cannot_attack"
	aozoraHasBuff := false
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "cannot_attack" {
			aozoraHasBuff = true
		}
	}
	if !aozoraHasBuff {
		t.Error("Tenki frontend should have cannot_attack debuff")
	}

	// SD frontend should NOT have "cannot_attack"
	for _, eff := range updatedField.Frontend[1].TemporaryEffects {
		if eff.EffectType == "cannot_attack" {
			t.Error("SD frontend should not have cannot_attack debuff")
		}
	}
}

// --- Neutral Config Error: TP → 0 ---

func TestNeutralConfigError(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, MaxAV: 1400, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 106, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	found := false
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "debuff_tp" && eff.Duration == "until_next_turn_end" {
			found = true
		}
	}
	if !found {
		t.Error("debuff_tp not applied")
	}
}

func TestNeutralConfigError_BackendTarget(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3, MaxAV: 1300},
		},
	}
	state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "b1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 106, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for backend target")
	}
}

// --- Neutral Ransomware ---

func TestNeutralRansomware(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 111, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	found := false
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "ransomware" && eff.Duration == "until_next_turn_end" {
			found = true
		}
	}
	if !found {
		t.Error("ransomware effect not applied")
	}
}

// ====================================================================
// Pattern-Based Tests: Conditional Activation (3+ Faction)
// ====================================================================

// --- SD Marketplace: 3+ SD → +600 ---

func TestSDMarketplace_Enough(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "SD"})
	cc.InjectForTest(3, &model.CardDefinition{CardNo: 3, CardType: "Compute", Faction: "SD"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp},
			{FaceUp: true, InstanceID: "f2", CardID: 3, CurrentTP: &tp},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 20, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetBudget(1) != 5600 { // +600
		t.Errorf("budget = %d, want 5600", state.GetBudget(1))
	}
}

func TestSDMarketplace_NotEnough(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "Tenki"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 20, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for < 3 SD")
	}
}

// --- SD Ecosystem: 3+ SD → SD frontend TP +200 ---

func TestSDEcosystem_Enough(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "SD"})
	cc.InjectForTest(3, &model.CardDefinition{CardNo: 3, CardType: "Compute", Faction: "SD"})
	cc.InjectForTest(4, &model.CardDefinition{CardNo: 4, CardType: "Compute", Faction: "Tenki"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
			{FaceUp: true, InstanceID: "f2", CardID: 4, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}}, // Tenki
			{FaceUp: true, InstanceID: "f3", CardID: 3, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 118, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)

	// SD frontends should get buff
	for _, idx := range []int{0, 2} {
		found := false
		for _, eff := range updatedField.Frontend[idx].TemporaryEffects {
			if eff.EffectType == "buff_tp" && eff.Value == 200 {
				found = true
			}
		}
		if !found {
			t.Errorf("SD frontend[%d] should have buff_tp +200", idx)
		}
	}

	// Tenki frontend should NOT get buff
	for _, eff := range updatedField.Frontend[1].TemporaryEffects {
		if eff.EffectType == "buff_tp" {
			t.Error("Tenki frontend should not get SD ecosystem buff")
		}
	}
}

func TestSDEcosystem_NotEnough(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 118, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for < 3 SD")
	}
}

// --- Tuners Guard: reveal trap + conditional incident reduction ---

func TestTunersGuard_WithEnoughTuners(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "Tuners"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "Tuners"})
	cc.InjectForTest(3, &model.CardDefinition{CardNo: 3, CardType: "Compute", Faction: "Tuners"})

	tp := int64(700)
	// Own field with 3 Tuners
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
			{FaceUp: true, InstanceID: "f2", CardID: 3, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(1, field)

	// Opponent has a face-down trap
	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 99, FaceDown: true},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 83, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Trap should be revealed
	updatedOppField, _ := state.GetField(2)
	if updatedOppField.Support[0].FaceDown {
		t.Error("trap should be revealed (FaceDown = false)")
	}

	// Incident reduction should be applied to own resources
	updatedField, _ := state.GetField(1)
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "incident_reduction" && eff.Value == 200 {
			return // pass
		}
	}
	t.Error("incident_reduction not applied with 3+ Tuners")
}

func TestTunersGuard_WithoutEnoughTuners(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "Tuners"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(1, field)

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 99, FaceDown: true},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 83, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Trap should still be revealed
	updatedOppField, _ := state.GetField(2)
	if updatedOppField.Support[0].FaceDown {
		t.Error("trap should still be revealed")
	}

	// But NO incident reduction
	updatedField, _ := state.GetField(1)
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "incident_reduction" {
			t.Error("incident_reduction should not be applied with < 3 Tuners")
		}
	}
}

// ====================================================================
// Pattern-Based Tests: Trap Reveal
// ====================================================================

func TestRevealTrapEffect(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 10, FaceDown: true},
			{InstanceID: "s2", CardID: 11, FaceDown: true},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := Compose(RevealTrap{})(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	// First face-down trap should be revealed
	if updatedField.Support[0].FaceDown {
		t.Error("first trap should be revealed")
	}
	// Second should remain face-down
	if !updatedField.Support[1].FaceDown {
		t.Error("second trap should remain face-down")
	}
}

func TestRevealTrapEffect_NoFaceDown(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 10, FaceDown: false},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	// Should not error even if no face-down traps
	_, err := Compose(RevealTrap{})(ctx)
	if err != nil {
		t.Fatalf("handler should not fail: %v", err)
	}
}

// --- SD Guard: reveal trap (delegates to revealTrapEffect) ---

func TestSDGuard(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 10, FaceDown: true},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 14, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	if updatedField.Support[0].FaceDown {
		t.Error("trap should be revealed")
	}
}

// --- Tenki Sentinel: reveal trap + incident -300 ---

func TestTenkiSentinel(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(1, field)

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 99, FaceDown: true},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 36, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Trap revealed
	updatedOppField, _ := state.GetField(2)
	if updatedOppField.Support[0].FaceDown {
		t.Error("trap should be revealed")
	}

	// Incident reduction applied to own resources
	updatedField, _ := state.GetField(1)
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "incident_reduction" && eff.Value == 300 {
			return // pass
		}
	}
	t.Error("incident_reduction -300 not applied to own frontend")
}

// ====================================================================
// Pattern-Based Tests: Cancel / Nullify
// ====================================================================

func TestCancelActionHandlers(t *testing.T) {
	tests := []struct {
		name    string
		handler EffectHandler
	}{
		{"RateLimiter", Compose(SetCancelAction{})},
		{"Defender", Compose(SetCancelAction{})},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			ctx := &EffectContext{}
			result, err := tt.handler(ctx)
			if err != nil {
				t.Fatalf("handler failed: %v", err)
			}
			if !result.CancelAction {
				t.Error("should cancel action")
			}
		})
	}
}

// --- Sugar Error Budget: survive destruction (Sugar only) ---

func TestSugarErrorBudget_Success(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(68, &model.CardDefinition{CardNo: 68, CardType: "Compute", Faction: "Sugar"})

	target := &model.ResourceInstance{
		InstanceID: "t1", CardID: 68,
		FaceUp:                   true,
		MaxAV: 1600, Damage: 2000,
	}

	ctx := &EffectContext{Target: target, CardCache: cc}
	result, err := testHandler(t, 68, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}
	if !result.CancelAction {
		t.Error("should cancel destruction")
	}

	// Damage should be reset to MaxAV - 200 = 1400
	if target.Damage != 1400 {
		t.Errorf("damage = %d, want 1400", target.Damage)
	}
}

func TestSugarErrorBudget_NonSugar(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	target := &model.ResourceInstance{
		InstanceID: "t1", CardID: 1,
		FaceUp:                   true,
		MaxAV: 1400, Damage: 2000,
	}

	ctx := &EffectContext{Target: target, CardCache: cc}
	_, err := testHandler(t, 68, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error for non-Sugar target")
	}
}

// ====================================================================
// Pattern-Based Tests: Incident Handlers (parameterized)
// ====================================================================

func TestIncidentSingleTargetDamage(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, MaxAV: 1400, Damage: 0, Attachments: []model.AttachmentRef{}},
		},
	}
	state.SetField(2, oppField)

	handler := testHandler(t, 104, TriggerActivate)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	if updatedField.Frontend[0].Damage != 500 {
		t.Errorf("damage = %d, want 500", updatedField.Frontend[0].Damage)
	}
}

func TestIncidentSingleTargetDamage_WithBudgetPenalty(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3, MaxAV: 1300, Damage: 0, Attachments: []model.AttachmentRef{}},
		},
	}
	state.SetField(2, oppField)

	handler := testHandler(t, 105, TriggerActivate)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "b1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	if updatedField.Backend[0].Damage != 600 {
		t.Errorf("damage = %d, want 600", updatedField.Backend[0].Damage)
	}
	if state.GetBudget(2) != 4700 { // 5000 - 300
		t.Errorf("opponent budget = %d, want 4700", state.GetBudget(2))
	}
}

func TestIncidentSingleTargetDamage_Destroy(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, MaxAV: 400, Damage: 0, Attachments: []model.AttachmentRef{}},
		},
	}
	state.SetField(2, oppField)

	handler := testHandler(t, 104, TriggerActivate)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	handler(ctx)

	updatedField, _ := state.GetField(2)
	if updatedField.Frontend[0] != nil {
		t.Error("target should be destroyed (AV ≤ 0)")
	}
}

func TestIncidentSingleTargetDamage_WrongZone(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3, MaxAV: 1300},
		},
	}
	state.SetField(2, oppField)

	handler := testHandler(t, 104, TriggerActivate)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "b1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := handler(ctx)
	if err == nil {
		t.Fatal("expected error for wrong zone")
	}
}

// --- Incident All Field Damage ---

func TestIncidentAllFieldDamage(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, MaxAV: 1400, Damage: 0, Attachments: []model.AttachmentRef{}},
			{FaceUp: true, InstanceID: "f2", CardID: 2, MaxAV: 1400, Damage: 0, Attachments: []model.AttachmentRef{}},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3, MaxAV: 1300, Damage: 0, Attachments: []model.AttachmentRef{}},
		},
	}
	state.SetField(2, oppField)

	handler := testHandler(t, 109, TriggerActivate)
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := handler(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	for i, res := range updatedField.Frontend {
		if res != nil && res.Damage != 500 {
			t.Errorf("frontend[%d] damage = %d, want 500", i, res.Damage)
		}
	}
	if updatedField.Backend[0] != nil && updatedField.Backend[0].Damage != 500 {
		t.Errorf("backend[0] damage = %d, want 500", updatedField.Backend[0].Damage)
	}
}

// ====================================================================
// Pattern-Based Tests: Tenki-Specific
// ====================================================================

// --- Tenki Policy: block opponent incidents ---

func TestTenkiPolicy(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(700)
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 44, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	found := false
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "incident_block" && eff.Duration == "this_turn" {
			found = true
		}
	}
	if !found {
		t.Error("incident_block not applied to opponent")
	}
}

// --- Tenki Traffic: deploy Tenki Compute from hand on frontend destroy ---

func TestTenkiTraffic_Success(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(46, &model.CardDefinition{
		CardNo: 46, CardType: "Compute", Faction: "Tenki",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	state.SetHand(1, []model.HandCard{{InstanceID: "h1", CardID: 46}})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 46})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 46, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	field, _ := state.GetField(1)
	found := false
	for _, res := range field.Frontend {
		if res != nil && res.CardID == 46 {
			found = true
		}
	}
	if !found {
		t.Error("Tenki Compute should be deployed to frontend")
	}
}

func TestTenkiTraffic_NonTenki(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 1})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 46, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error for non-Tenki Compute")
	}
}

// --- Tenki Failover Group: DV +400 when other Tenki DB destroyed ---

func TestTenkiFailoverGroup_Success(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(30, &model.CardDefinition{CardNo: 30, CardType: "Database", Faction: "Tenki"})

	source := &model.ResourceInstance{
		InstanceID:       "src1",
		CardID:           30,
		FaceUp:           true,
		TemporaryEffects: []model.TemporaryEffect{},
	}
	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "destroyed1",
		CardID:     30,
	}

	ctx := &EffectContext{
		Source:    source,
		Target:    target,
		CardCache: cc,
	}

	_, err := testHandler(t, 30, TriggerOnDestroy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	found := false
	for _, eff := range source.TemporaryEffects {
		if eff.EffectType == "buff_yield" && eff.Value == 400 && eff.Duration == "until_next_own_turn_end" {
			found = true
		}
	}
	if !found {
		t.Error("buff_yield not applied")
	}
}

func TestTenkiFailoverGroup_SelfDestroyed(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(30, &model.CardDefinition{CardNo: 30, CardType: "Database", Faction: "Tenki"})

	source := &model.ResourceInstance{FaceUp: true, InstanceID: "src1", CardID: 30}
	target := &model.ResourceInstance{FaceUp: true, InstanceID: "src1", CardID: 30} // Same as source

	ctx := &EffectContext{Source: source, Target: target, CardCache: cc}

	_, err := testHandler(t, 30, TriggerOnDestroy)(ctx)
	if err == nil {
		t.Fatal("expected error for self-trigger")
	}
}

func TestTenkiFailoverGroup_NonTenkiDB(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	source := &model.ResourceInstance{FaceUp: true, InstanceID: "src1", CardID: 30}
	target := &model.ResourceInstance{FaceUp: true, InstanceID: "t1", CardID: 1}

	ctx := &EffectContext{Source: source, Target: target, CardCache: cc}

	_, err := testHandler(t, 30, TriggerOnDestroy)(ctx)
	if err == nil {
		t.Fatal("expected error for non-Tenki DB")
	}
}

// ====================================================================
// Pattern-Based Tests: Sugar-Specific
// ====================================================================

// --- Kindergarten Deploy: auto scale to medium ---

func TestSugarKindergartenDeploy(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(50, &model.CardDefinition{
		CardNo: 50, CardType: "Orchestrator", Faction: "Sugar",
		Stats: json.RawMessage(`{"throughput": 500, "availability": 1000, "maintenance_cost": 100, "deploy_cost": 300, "sla_penalty": 300}`),
	})

	tp := int64(500)
	source := &model.ResourceInstance{
		InstanceID: "src1",
		CardID:     50,
		FaceUp:     true,
		Rank:       model.RankSmall,
		CurrentTP:  &tp,
		MaxAV:      1000,
	}

	ctx := &EffectContext{
		Source:    source,
		CardCache: cc,
	}

	_, err := testHandler(t, 50, TriggerDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if source.Rank != model.RankMedium {
		t.Errorf("rank = %s, want medium", source.Rank)
	}
	if source.CurrentTP == nil || *source.CurrentTP != 1000 { // 500 * 2
		t.Errorf("currentTP = %v, want 1000", source.CurrentTP)
	}
	if source.MaxAV != 2000 { // 1000 * 2
		t.Errorf("maxAV = %d, want 2000", source.MaxAV)
	}
}

// --- Sugar Pub/Sub: another frontend deals 200 extra damage ---

func TestSugarPubSub_WithOtherSugar(t *testing.T) {
	state := newEffectTestState()
	cc := cache.NewCardCache()
	cc.InjectForTest(63, &model.CardDefinition{CardNo: 63, CardType: "Compute", Faction: "Sugar"})
	cc.InjectForTest(50, &model.CardDefinition{CardNo: 50, CardType: "Orchestrator", Faction: "Sugar"})

	tp := int64(700)
	source := &model.ResourceInstance{FaceUp: true, InstanceID: "attacker", CardID: 63, CurrentTP: &tp}
	target := &model.ResourceInstance{FaceUp: true, InstanceID: "target1", CardID: 1, MaxAV: 1400, Damage: 0}

	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			source,
			{FaceUp: true, InstanceID: "other_guruguru", CardID: 50, CurrentTP: &tp},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State:     state,
		PlayerNum: 1,
		Source:    source,
		Target:    target,
		CardCache: cc,
	}

	_, err := testHandler(t, 63, TriggerOnAttack)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if target.Damage != 200 {
		t.Errorf("target damage = %d, want 200", target.Damage)
	}
}

func TestSugarPubSub_NoOtherSugar(t *testing.T) {
	state := newEffectTestState()
	cc := cache.NewCardCache()
	cc.InjectForTest(63, &model.CardDefinition{CardNo: 63, CardType: "Compute", Faction: "Sugar"})
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})

	tp := int64(700)
	source := &model.ResourceInstance{FaceUp: true, InstanceID: "attacker", CardID: 63, CurrentTP: &tp}
	target := &model.ResourceInstance{FaceUp: true, InstanceID: "target1", CardID: 99, MaxAV: 1400, Damage: 0}

	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			source,
			{FaceUp: true, InstanceID: "sws_unit", CardID: 1, CurrentTP: &tp}, // Not Sugar
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State:     state,
		PlayerNum: 1,
		Source:    source,
		Target:    target,
		CardCache: cc,
	}

	testHandler(t, 63, TriggerOnAttack)(ctx)

	if target.Damage != 0 {
		t.Errorf("target damage = %d, want 0 (no other Sugar)", target.Damage)
	}
}

// ====================================================================
// Pattern-Based Tests: Neutral Effects (additional)
// ====================================================================

// --- Neutral Open Source Migration: destroy opponent platform ---

func TestNeutralOpenSourceMigration(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(10, &model.CardDefinition{CardNo: 10, CardType: "Platform", Faction: "SD"})

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "plat1", CardID: 10},
		},
	}
	state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "plat1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 103, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	if updatedField.Support[0] != nil {
		t.Error("platform should be destroyed")
	}

	trash, _ := state.GetTrash(2)
	found := false
	for _, c := range trash {
		if c == 10 {
			found = true
		}
	}
	if !found {
		t.Error("destroyed platform should be in trash")
	}
}

func TestNeutralOpenSourceMigration_NonPlatform(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(10, &model.CardDefinition{CardNo: 10, CardType: "Reactive", Faction: "SD"})

	oppField := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 10},
		},
	}
	state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "s1"})
	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc, ChoiceData: choiceData,
	}

	_, err := testHandler(t, 103, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for non-Platform target")
	}
}

// --- Neutral Data Scraping: steal 600 Insight if opponent has backend ---

func TestNeutralDataScraping_HasBackend(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3},
		},
	}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 107, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetInsightPool(1) != 1000 { // 500 + 500 (only 500 available)
		t.Errorf("my Insight = %d, want 1000", state.GetInsightPool(1))
	}
}

func TestNeutralDataScraping_NoBackend(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{}
	state.SetField(2, oppField)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 107, TriggerActivate)(ctx)
	if err == nil {
		t.Fatal("expected error for no opponent backend")
	}
}

// --- Neutral Crypto Mining: 400 damage + self budget +500 ---

func TestNeutralCryptoMining(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, MaxAV: 1400, Damage: 0, Attachments: []model.AttachmentRef{}},
		},
	}
	state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "f1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 110, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(2)
	if updatedField.Frontend[0].Damage != 400 {
		t.Errorf("damage = %d, want 400", updatedField.Frontend[0].Damage)
	}

	if state.GetBudget(1) != 5500 { // 5000 + 500
		t.Errorf("budget = %d, want 5500", state.GetBudget(1))
	}
}

// --- SD Cost Explorer: deploy discount ---

func TestSDCostExplorer(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp, TemporaryEffects: []model.TemporaryEffect{}},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 3, TemporaryEffects: []model.TemporaryEffect{}},
		},
	}
	state.SetField(1, field)

	ctx := &EffectContext{
		State: state, Game: game, PlayerNum: 1, CardCache: cc,
	}

	_, err := testHandler(t, 21, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	updatedField, _ := state.GetField(1)

	// Frontend should have deploy_discount
	for _, eff := range updatedField.Frontend[0].TemporaryEffects {
		if eff.EffectType == "deploy_discount" && eff.Value == 200 {
			goto checkBackend
		}
	}
	t.Error("deploy_discount not applied to frontend")
checkBackend:

	// Backend should also have deploy_discount
	for _, eff := range updatedField.Backend[0].TemporaryEffects {
		if eff.EffectType == "deploy_discount" && eff.Value == 200 {
			return
		}
	}
	t.Error("deploy_discount not applied to backend")
}

// --- SD Prime Delivery: deploy from hand at cost 0 ---

func TestSDPrimeDelivery(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()
	cc.InjectForTest(121, &model.CardDefinition{
		CardNo: 121, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	state.SetHand(1, []model.HandCard{{InstanceID: "h1", CardID: 121}})

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 121})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 121, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	field, _ := state.GetField(1)
	found := false
	for _, res := range field.Frontend {
		if res != nil && res.CardID == 121 {
			found = true
		}
	}
	if !found {
		t.Error("card should be deployed to frontend")
	}

	hand, _ := state.GetHand(1)
	if len(hand) != 0 {
		t.Error("hand should be empty after deploy")
	}
}

// ====================================================================
// Helper Function Tests
// ====================================================================

func TestCountBackendDBs(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Database", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "Tenki"})
	cc.InjectForTest(3, &model.CardDefinition{CardNo: 3, CardType: "ObjectStorage", Faction: "SD"}) // Not a DB
	cc.InjectForTest(4, &model.CardDefinition{CardNo: 4, CardType: "Database", Faction: "Sugar"})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 1},
			{FaceUp: true, InstanceID: "b2", CardID: 2},
			{FaceUp: true, InstanceID: "b3", CardID: 4},
		},
	}

	// All DBs (excluding storage)
	if got := CountBackendDBs(field, "", cc); got != 3 {
		t.Errorf("all DBs = %d, want 3", got)
	}

	// SD DBs only
	if got := CountBackendDBs(field, "SD", cc); got != 1 {
		t.Errorf("SD DBs = %d, want 1", got)
	}
}

func TestHasStorageOnField(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Database", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "ObjectStorage", Faction: "SD"})

	field := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 1},
		},
	}
	if HasStorageOnField(field, cc) {
		t.Error("should not have storage")
	}

	field.Backend[1] = &model.ResourceInstance{FaceUp: true, InstanceID: "b2", CardID: 2}
	if !HasStorageOnField(field, cc) {
		t.Error("should have storage")
	}
}

func TestHasSecurityPlatform(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(15, &model.CardDefinition{CardNo: 15, CardType: "Platform"})
	cc.InjectForTest(11, &model.CardDefinition{CardNo: 11, CardType: "Platform"})

	field := &model.Field{
		Support: [3]*model.SupportInstance{
			{InstanceID: "s1", CardID: 11},
		},
	}
	if HasSecurityPlatform(field, cc) {
		t.Error("should not have security platform")
	}

	field.Support[1] = &model.SupportInstance{InstanceID: "s2", CardID: 15}
	if !HasSecurityPlatform(field, cc) {
		t.Error("should have security platform")
	}
}

func TestHasCardTypeOnField(t *testing.T) {
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{CardNo: 1, CardType: "Compute", Faction: "SD"})
	cc.InjectForTest(2, &model.CardDefinition{CardNo: 2, CardType: "Database", Faction: "Tenki"})

	tp := int64(700)
	field := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, CurrentTP: &tp},
		},
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 2},
		},
	}

	if !HasCardTypeOnField(field, "Compute", "SD", cc) {
		t.Error("should find SD Compute")
	}
	if HasCardTypeOnField(field, "Compute", "Tenki", cc) {
		t.Error("should not find Tenki Compute")
	}
	if !HasCardTypeOnField(field, "Database", "", cc) {
		t.Error("should find Database (any faction)")
	}
}

func TestCountOpponentBackend(t *testing.T) {
	state := newEffectTestState()
	_ = cache.NewCardCache() // not needed for this helper

	oppField := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "b1", CardID: 1},
			nil,
			{FaceUp: true, InstanceID: "b3", CardID: 3},
		},
	}
	state.SetField(2, oppField)

	// Player 1's opponent is player 2
	count := CountOpponentBackend(state, 1)
	if count != 2 {
		t.Errorf("opponent backend count = %d, want 2", count)
	}
}

// ====================================================================
// Pattern-Based Tests: SLA Penalty
// ====================================================================

func TestApplySLAPenalty_Compute(t *testing.T) {
	state := newEffectTestState()
	cc := cache.NewCardCache()
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo: 1, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	res := &model.ResourceInstance{FaceUp: true, CardID: 1}
	ApplySLAPenalty(state, 1, res, cc)

	if state.GetBudget(1) != 4600 { // 5000 - 400
		t.Errorf("budget = %d, want 4600", state.GetBudget(1))
	}
}

func TestApplySLAPenalty_Data(t *testing.T) {
	state := newEffectTestState()
	cc := cache.NewCardCache()
	cc.InjectForTest(3, &model.CardDefinition{
		CardNo: 3, CardType: "Database", Faction: "SD",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	res := &model.ResourceInstance{FaceUp: true, CardID: 3}
	ApplySLAPenalty(state, 1, res, cc)

	if state.GetBudget(1) != 4500 { // 5000 - 500
		t.Errorf("budget = %d, want 4500", state.GetBudget(1))
	}
}

func TestApplySLAPenalty_UnknownCard(t *testing.T) {
	state := newEffectTestState()
	cc := cache.NewCardCache()

	res := &model.ResourceInstance{FaceUp: true, CardID: 999} // Not in cache
	ApplySLAPenalty(state, 1, res, cc)

	// No penalty deducted
	if state.GetBudget(1) != 5000 {
		t.Errorf("budget = %d, want 5000", state.GetBudget(1))
	}
}

// ====================================================================
// HIGH Priority Card Effect Tests
// ====================================================================

// --- #61 Sugar Analytics: Absorb 300 Insight per Yield phase (TriggerPassive) ---

func TestSugarAnalytics_AbsorbInsight(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 61, TriggerPassive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetInsightPool(1) != 800 { // 500 + 300
		t.Errorf("player 1 Insight = %d, want 800", state.GetInsightPool(1))
	}
	if state.GetInsightPool(2) != 200 { // 500 - 300
		t.Errorf("player 2 Insight = %d, want 200", state.GetInsightPool(2))
	}
}

func TestSugarAnalytics_PartialAbsorb(t *testing.T) {
	state := newEffectTestState()
	state.Player2InsightPool = 100 // Less than absorb amount of 300
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 61, TriggerPassive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if state.GetInsightPool(1) != 600 { // 500 + 100 (only 100 available)
		t.Errorf("player 1 Insight = %d, want 600", state.GetInsightPool(1))
	}
	if state.GetInsightPool(2) != 0 {
		t.Errorf("player 2 Insight = %d, want 0", state.GetInsightPool(2))
	}
}

func TestSugarAnalytics_OpponentZeroInsight(t *testing.T) {
	state := newEffectTestState()
	state.Player2InsightPool = 0
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 61, TriggerPassive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// No Insight to absorb
	if state.GetInsightPool(1) != 500 {
		t.Errorf("player 1 Insight = %d, want 500", state.GetInsightPool(1))
	}
	if state.GetInsightPool(2) != 0 {
		t.Errorf("player 2 Insight = %d, want 0", state.GetInsightPool(2))
	}
}

// --- #99 Cloud Architect: Draw 2, keep 1, Tuners → trash (TriggerActivate) ---

func TestNeutralCloudArchitect_DrawTwoKeepOne(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Both cards are non-Tuners
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo: 1, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo: 2, CardType: "Database", Faction: "SD",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	// Player chooses to discard card 2 (keep card 1)
	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 2})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 99, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// 1 card kept in hand (card 1), 1 discarded (card 2)
	hand, _ := state.GetHand(1)
	if len(hand) != 1 {
		t.Errorf("hand size = %d, want 1", len(hand))
	}
	if len(hand) > 0 && hand[0].CardID != 1 {
		t.Errorf("kept card = %d, want 1", hand[0].CardID)
	}

	// Discarded card goes to trash
	trash, _ := state.GetTrash(1)
	if len(trash) != 1 {
		t.Errorf("trash size = %d, want 1", len(trash))
	}

	// Repo should have 3 cards (started with 5, drew 2)
	repo, _ := state.GetRepository(1)
	if len(repo) != 3 {
		t.Errorf("repo size = %d, want 3", len(repo))
	}
}

func TestNeutralCloudArchitect_TunersGoToTrash(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Card 1 is Tuners (goes to trash automatically), Card 2 is SD (kept)
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo: 1, CardType: "Compute", Faction: "Tuners",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo: 2, CardType: "Database", Faction: "SD",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 99, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Only 1 non-Tuners card kept, so KeepOne does not trigger discard
	hand, _ := state.GetHand(1)
	if len(hand) != 1 {
		t.Errorf("hand size = %d, want 1", len(hand))
	}
	if len(hand) > 0 && hand[0].CardID != 2 {
		t.Errorf("kept card = %d, want 2 (SD card)", hand[0].CardID)
	}

	// Tuners card should be in trash
	trash, _ := state.GetTrash(1)
	if len(trash) != 1 {
		t.Errorf("trash size = %d, want 1", len(trash))
	}
	if len(trash) > 0 && trash[0] != 1 {
		t.Errorf("trashed card = %d, want 1 (Tuners card)", trash[0])
	}
}

func TestNeutralCloudArchitect_BothTuners(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Both cards are Tuners — both go to trash
	cc.InjectForTest(1, &model.CardDefinition{
		CardNo: 1, CardType: "Compute", Faction: "Tuners",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	cc.InjectForTest(2, &model.CardDefinition{
		CardNo: 2, CardType: "Database", Faction: "Tuners",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		CardCache: cc,
	}

	_, err := testHandler(t, 99, TriggerActivate)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// No cards in hand (both Tuners went to trash)
	hand, _ := state.GetHand(1)
	if len(hand) != 0 {
		t.Errorf("hand size = %d, want 0", len(hand))
	}

	// Both cards in trash
	trash, _ := state.GetTrash(1)
	if len(trash) != 2 {
		t.Errorf("trash size = %d, want 2", len(trash))
	}
}

// --- #115 Failover: Deploy same type from hand when resource destroyed (TriggerReactive) ---

func TestNeutralFailover_DeploySameType(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Target (destroyed resource) is a Compute type
	cc.InjectForTest(50, &model.CardDefinition{
		CardNo: 50, CardType: "Compute", Faction: "Sugar",
		Stats: json.RawMessage(`{"throughput": 800, "availability": 1600, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	// Replacement card in hand is also Compute
	cc.InjectForTest(60, &model.CardDefinition{
		CardNo: 60, CardType: "Compute", Faction: "SD",
		Stats: json.RawMessage(`{"throughput": 700, "availability": 1400, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})

	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "destroyed-1",
		CardID:     50,
	}

	// Put card 60 in player's hand
	hand := []model.HandCard{{InstanceID: "h1", CardID: 60}}
	_ = state.SetHand(1, hand)

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 60})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Target:     target,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 115, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Card 60 should be removed from hand
	handAfter, _ := state.GetHand(1)
	if len(handAfter) != 0 {
		t.Errorf("hand size = %d, want 0 (card deployed from hand)", len(handAfter))
	}

	// Card 60 should be on the field
	field, _ := state.GetField(1)
	found := false
	for _, res := range field.Frontend {
		if res != nil && res.CardID == 60 {
			found = true
			break
		}
	}
	if !found {
		t.Error("card 60 should be deployed on field")
	}
}

func TestNeutralFailover_WrongType(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Target is Compute type
	cc.InjectForTest(50, &model.CardDefinition{
		CardNo: 50, CardType: "Compute", Faction: "Sugar",
		Stats: json.RawMessage(`{"throughput": 800, "availability": 1600, "maintenance_cost": 200, "deploy_cost": 400, "sla_penalty": 400}`),
	})
	// Choice card is Database type (mismatch)
	cc.InjectForTest(70, &model.CardDefinition{
		CardNo: 70, CardType: "Database", Faction: "SD",
		Stats: json.RawMessage(`{"yield": 500, "availability": 1300, "deploy_cost": 400, "sla_penalty": 500}`),
	})

	target := &model.ResourceInstance{
		FaceUp:    true,
		InstanceID: "destroyed-1",
		CardID:     50,
	}

	hand := []model.HandCard{{InstanceID: "h1", CardID: 70}}
	_ = state.SetHand(1, hand)

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 70})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Target:     target,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 115, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error for wrong card type")
	}
}

func TestNeutralFailover_NoTarget(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	choiceData, _ := json.Marshal(ChoiceCardNo{CardNo: 60})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		Target:     nil, // No target
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 115, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error when no target provided")
	}
}

// --- #117 Chaos Engineering: Redirect attack to opponent's frontend (TriggerReactive) ---

func TestNeutralChaos_RedirectToFrontend(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Set up opponent field with a frontend resource
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "opp-f1", CardID: 1, CurrentAV: 1000, MaxAV: 1000},
		},
	}
	_ = state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "opp-f1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	result, err := testHandler(t, 117, TriggerReactive)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	// Should cancel the original action (redirect)
	if !result.CancelAction {
		t.Error("chaos engineering should set CancelAction")
	}
}

func TestNeutralChaos_BackendTarget(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Opponent has a backend resource but no frontend
	oppField := &model.Field{
		Backend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "opp-b1", CardID: 2, CurrentAV: 1000, MaxAV: 1000},
		},
	}
	_ = state.SetField(2, oppField)

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "opp-b1"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 117, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error: redirect target must be opponent's frontend")
	}
}

func TestNeutralChaos_NoFrontend(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Opponent has empty field
	_ = state.SetField(2, &model.Field{})

	choiceData, _ := json.Marshal(ChoiceInstanceID{InstanceID: "nonexistent"})
	ctx := &EffectContext{
		State:      state,
		Game:       game,
		PlayerNum:  1,
		CardCache:  cc,
		ChoiceData: choiceData,
	}

	_, err := testHandler(t, 117, TriggerReactive)(ctx)
	if err == nil {
		t.Fatal("expected error when redirect target not found")
	}
}

// --- #135 Throttling: Destroy opponent's 3rd deployed resource (TriggerOnEnemyDeploy) ---

func TestNeutralThrottling_ThirdDeploy(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Opponent has 3 resources deployed this turn
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, DeployedOnTurn: state.CurrentTurn},
			{FaceUp: true, InstanceID: "f2", CardID: 2, DeployedOnTurn: state.CurrentTurn},
			{FaceUp: true, InstanceID: "f3", CardID: 3, DeployedOnTurn: state.CurrentTurn},
		},
	}
	_ = state.SetField(2, oppField)

	supSource := &model.SupportInstance{
		InstanceID:         "sup1",
		CardID:             135,
		EffectUsedThisTurn: false,
	}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		SupSource: supSource,
		CardCache: cc,
	}

	result, err := testHandler(t, 135, TriggerOnEnemyDeploy)(ctx)
	if err != nil {
		t.Fatalf("handler failed: %v", err)
	}

	if !result.CancelAction {
		t.Error("throttling should cancel the 3rd deploy")
	}

	// EffectUsedThisTurn should be marked
	if !supSource.EffectUsedThisTurn {
		t.Error("EffectUsedThisTurn should be true after firing")
	}
}

func TestNeutralThrottling_OncePerTurn(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Opponent has 3 resources deployed this turn
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, DeployedOnTurn: state.CurrentTurn},
			{FaceUp: true, InstanceID: "f2", CardID: 2, DeployedOnTurn: state.CurrentTurn},
			{FaceUp: true, InstanceID: "f3", CardID: 3, DeployedOnTurn: state.CurrentTurn},
		},
	}
	_ = state.SetField(2, oppField)

	supSource := &model.SupportInstance{
		InstanceID:         "sup1",
		CardID:             135,
		EffectUsedThisTurn: true, // Already used this turn
	}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		SupSource: supSource,
		CardCache: cc,
	}

	_, err := testHandler(t, 135, TriggerOnEnemyDeploy)(ctx)
	if err == nil {
		t.Fatal("expected error: throttling already used this turn")
	}
}

func TestNeutralThrottling_NotThirdDeploy(t *testing.T) {
	state := newEffectTestState()
	game := newEffectTestGame()
	cc := cache.NewCardCache()

	// Opponent has only 2 resources deployed this turn
	oppField := &model.Field{
		Frontend: [3]*model.ResourceInstance{
			{FaceUp: true, InstanceID: "f1", CardID: 1, DeployedOnTurn: state.CurrentTurn},
			{FaceUp: true, InstanceID: "f2", CardID: 2, DeployedOnTurn: state.CurrentTurn},
		},
	}
	_ = state.SetField(2, oppField)

	supSource := &model.SupportInstance{
		InstanceID:         "sup1",
		CardID:             135,
		EffectUsedThisTurn: false,
	}

	ctx := &EffectContext{
		State:     state,
		Game:      game,
		PlayerNum: 1,
		SupSource: supSource,
		CardCache: cc,
	}

	_, err := testHandler(t, 135, TriggerOnEnemyDeploy)(ctx)
	if err == nil {
		t.Fatal("expected error: not the 3rd deploy")
	}

	// EffectUsedThisTurn should NOT be marked
	if supSource.EffectUsedThisTurn {
		t.Error("EffectUsedThisTurn should remain false when not triggered")
	}
}

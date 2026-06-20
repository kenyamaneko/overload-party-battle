using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class NpcAiTests
{
    private readonly TestCardCache _cc = new();
    private readonly StubEffectRegistry _effects = new();

    public NpcAiTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, mc: 150));
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", tp: 500, av: 1200, mc: 120));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0003", subtype: "Database", yield: 400, av: 800, mc: 100));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0004", subtype: "Database", yield: 300, av: 600, mc: 80));
        _cc.Add(TestFactory.PlatformCard(cardId: "TST-0005", name: "TestPlatform"));
        _cc.Add(TestFactory.AttachmentCard(cardId: "TST-0006", name: "TestAttachment"));
    }

    private static GD.ClientGameState BuildState(
        string phase = "main",
        long turn = 1,
        long budget = 5000,
        long insightPool = 0,
        long oppBudget = 5000,
        Action<GD.Field>? configureMyField = null,
        Action<GD.OpponentField>? configureOppField = null,
        List<GD.UndeployedCard>? hand = null,
        List<GD.AvailableAction>? available = null,
        GD.PendingSlotSelectView? pendingSlotSelect = null,
        GD.PendingEffectChoiceView? pendingEffectChoice = null)
    {
        var mf = TestFactory.MakeWireField();
        configureMyField?.Invoke(mf);
        var of = TestFactory.MakeWireOpponentField();
        configureOppField?.Invoke(of);
        return TestFactory.MakeClientState(
            myField: mf, oppField: of,
            myHand: hand, myBudget: budget, myInsightPool: insightPool,
            oppBudget: oppBudget,
            turn: turn, phase: phase,
            availableActions: available,
            pendingSlotSelect: pendingSlotSelect,
            pendingEffectChoice: pendingEffectChoice);
    }

    private static AiConfig MakeConfig()
    {
        return AiConfigLoader.LoadFromString("""
            model: test
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities:
                - card_id: TST-0001
                  priority: 80
                - card_type: Compute
                  priority: 50
                - card_type: DataResource
                  priority: 40
                - card_type: Platform
                  priority: 30
              choices:
                TST-0007: use
              zone_preferences:
                Compute: [frontend, backend]
                DataResource: [backend]
                Platform: [support]
            effect_priorities:
              budget_gain:
                priority: 90
                low_priority: 40
                threshold: 1500
              single_damage: 60
              deploy_free: 75
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """);
    }

    private static AiConfig MakeConditionalPriorityConfig()
    {
        return AiConfigLoader.LoadFromString("""
            model: test
            faction: Tenki
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities:
                - card_type: Compute
                  priority: 50
              conditional_priorities:
                - card_id: TST-0002
                  priority: 90
                  condition:
                    selector: { owner: myself }
                    card_id: [TST-0004]
                    min: 1
                  fallback_priority: 30
            effect_priorities: {}
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: R
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """);
    }

    private static AiConfig MakeConditionalFamilyConfig()
    {
        return AiConfigLoader.LoadFromString("""
            model: test
            faction: Tenki
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities: []
            effect_priorities: {}
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: R
              conditional_family:
                - family: M
                  condition:
                    selector: { owner: myself, zone: backend }
                    card_type: DataResource
                    min: 2
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """);
    }

    // ═══════════════════════════════════════════════════════════════
    //  手札調整
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DecideDiscard_CheapestMaintenance_DiscardsCheapest()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_sh1", CardID = "TST-0001" },  // card_id pri 80 → 残す
            new() { InstanceID = "h_tk5", CardID = "TST-0002" },  // compute pri 50 → 捨てる
            new() { InstanceID = "h_nt9", CardID = "TST-0003" },  // data pri 40 → 捨てる
        };
        var state = BuildState(phase: "end", hand: hand);

        var discards = ai.DecideDiscard(state, 2);

        discards.Should().Equal("h_nt9", "h_tk5");
    }

    // ═══════════════════════════════════════════════════════════════
    //  バトルフェーズ
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Battle_WeakestAV_AttacksLowestAV()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = new() { "strong", "weak" } },
        };
        var state = BuildState(
            phase: "battle",
            configureOppField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "strong", maxAV: 2000);
                f.Frontend[1] = TestFactory.MakeWireResource(instanceId: "weak", maxAV: 400);
            },
            available: available);

        var actions = ai.DecideBattlePhaseActions(state);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("weak");
    }

    [Fact]
    public void Battle_StrongestTP_AttacksHighestValue()
    {
        var config = AiConfigLoader.LoadFromString("""
            model: test
            faction: SHE
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: tp_desc
            scale_up:
              instance_family: M
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """);
        var ai = new NpcAi(config, _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = new() { "low_tp", "high_tp" } },
        };
        var state = BuildState(
            phase: "battle",
            configureOppField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "low_tp", currentTP: 200, maxAV: 2000);
                f.Frontend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "high_tp", currentTP: 900, maxAV: 400);
            },
            available: available);

        var actions = ai.DecideBattlePhaseActions(state);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("high_tp");
    }

    [Fact]
    public void Battle_NoAttackActions_OnlyEndPhase()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = BuildState(phase: "battle", available: new List<GD.AvailableAction>());

        var actions = ai.DecideBattlePhaseActions(state);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(ActionTypes.EndPhase);
    }

    [Fact]
    public void Battle_MultipleAttackers_AllAttack()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = new() { "target" } },
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk2", ValidTargets = new() { "target" } },
        };
        var state = BuildState(
            phase: "battle",
            configureOppField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "target", maxAV: 500),
            available: available);

        var actions = ai.DecideBattlePhaseActions(state);
        var attacks = actions.Where(a => a.ActionType == ActionTypes.Attack).ToList();

        attacks.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  スロット選択
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SlotSelect_ReturnsFirstValidZone()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingSlotSelectView
        {
            Resource = TestFactory.MakeWireResource(instanceId: "pending_res"),
            ValidZones = new List<string> { "frontend_0", "backend_1" },
        };
        var state = BuildState(pendingSlotSelect: pending);

        var action = ai.DecideSlotSelect(state);

        action.Should().NotBeNull();
        ((SelectSlotRequest)action!.Data).Zone.Should().Be("frontend");
        ((SelectSlotRequest)action.Data).Index.Should().Be(0);
    }

    [Fact]
    public void SlotSelect_NoPending_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        ai.DecideSlotSelect(BuildState()).Should().BeNull();
    }

    [Fact]
    public void SlotSelect_EmptyValidZones_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingSlotSelectView
        {
            Resource = TestFactory.MakeWireResource(instanceId: "pending_res"),
            ValidZones = new List<string>(),
        };
        var state = BuildState(pendingSlotSelect: pending);

        ai.DecideSlotSelect(state).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    //  効果中のプレイヤー選択
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PendingEffectChoice_PicksFirstCandidate()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = 1,
            EffectCardId = "TST-0002",
            EffectInstanceId = "inst_1",
            ChoiceKind = ChoiceKinds.HandCard,
        };
        // resolve_pending_choice の variant: hand_card 種別は CardID に候補 ID を載せる。
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.ResolvePendingChoice, SourceInstanceID = "inst_1", CardID = "TST-0001" },
            new() { Type = ActionTypes.ResolvePendingChoice, SourceInstanceID = "inst_1", CardID = "TST-0002" },
        };
        var state = BuildState(pendingEffectChoice: pending, available: available);

        var action = ai.DecidePendingEffectChoice(state);

        action.Should().NotBeNull();
        action!.ActionType.Should().Be(ActionTypes.ResolvePendingChoice);
        ((ResolvePendingChoiceRequest)action.Data).ChosenId.Should().Be("TST-0001");
    }

    [Fact]
    public void PendingEffectChoice_WrongChooser_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = 2,
            EffectCardId = "TST-0002",
            EffectInstanceId = "inst_1",
            ChoiceKind = ChoiceKinds.HandCard,
        };
        var state = BuildState(pendingEffectChoice: pending);

        ai.DecidePendingEffectChoice(state).Should().BeNull();
    }

    [Fact]
    public void PendingEffectChoice_NoResolveActions_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = 1,
            EffectCardId = "TST-0002",
            EffectInstanceId = "inst_1",
            ChoiceKind = ChoiceKinds.HandCard,
        };
        var state = BuildState(pendingEffectChoice: pending, available: new List<GD.AvailableAction>());

        ai.DecidePendingEffectChoice(state).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    //  デプロイ: priority 解決
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_CardIdPriority_TakesPrecedenceOverCardType()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_tk5", CardID = "TST-0002" },  // compute, no card_id match → 50
            new() { InstanceID = "h_sh1", CardID = "TST-0001" },  // compute, card_id match → 80
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_tk5", CardID = "TST-0002", ValidZones = new() { "frontend_1" } },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
    }

    [Fact]
    public void Deploy_UnknownCard_Priority0()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "UNKNOWN-001", tp: 100, av: 200, mc: 50));
        _cc.Add(new CardDefinition { CardId = "WEIRD-001", CardName = "Weird", CardType = "WeirdType" });

        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_weird", CardID = "WEIRD-001" },
            new() { InstanceID = "h_sh1", CardID = "TST-0001" },
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_weird", CardID = "WEIRD-001", ValidZones = new() { "frontend_1" } },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
    }

    [Fact]
    public void Deploy_ConditionalPriority_ConditionMet_UsesPrimary()
    {
        var ai = new NpcAi(MakeConditionalPriorityConfig(), _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "hand_sh1", CardID = "TST-0001" },
            new() { InstanceID = "hand_tk5", CardID = "TST-0002" },
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_1" } },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_tk5", CardID = "TST-0002", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(
            hand: hand,
            available: available,
            configureMyField: f =>
                f.Backend[0] = TestFactory.MakeWireResource(cardId: "TST-0004", instanceId: "cosmo_1"));

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("hand_tk5");
    }

    [Fact]
    public void Deploy_ConditionalPriority_ConditionNotMet_UsesFallback()
    {
        var ai = new NpcAi(MakeConditionalPriorityConfig(), _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "hand_sh1", CardID = "TST-0001" },
            new() { InstanceID = "hand_tk5", CardID = "TST-0002" },
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_tk5", CardID = "TST-0002", ValidZones = new() { "frontend_1" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("hand_sh1");
    }

    // ═══════════════════════════════════════════════════════════════
    //  デプロイ: choice 解決
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_Choice_UsesConfigValue()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0007", tp: 400, av: 1000));
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_0006", CardID = "TST-0007" } };
        var available = new List<GD.AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_0006", CardID = "TST-0007",
                ValidZones = new() { "frontend_0" }, ChoiceOptions = new() { "use", "reserve" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);

        var req = (PlayCardRequest)deploy.Data;
        req.ChoiceData.Should().NotBeNull();
        req.ChoiceData!["option"].Should().Be("use");
    }

    [Fact]
    public void Deploy_Choice_NotInConfig_Throws()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "UNKNOWN-C", tp: 300, av: 800));
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_unk", CardID = "UNKNOWN-C" } };
        var available = new List<GD.AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_unk", CardID = "UNKNOWN-C",
                ValidZones = new() { "frontend_0" }, ChoiceOptions = new() { "optionA", "optionB" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var act = () => ai.DecideMainPhaseActions(state);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No deploy choice configured*UNKNOWN-C*");
    }

    // ═══════════════════════════════════════════════════════════════
    //  デプロイ: zone preferences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_ZonePreference_ComputePrefersFrontend()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_sh1", CardID = "TST-0001" } };
        var available = new List<GD.AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-0001",
                ValidZones = new() { "backend_0", "frontend_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("frontend");
    }

    [Fact]
    public void Deploy_ZonePreference_DataPrefersBackend()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_db", CardID = "TST-0003" } };
        var available = new List<GD.AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_db", CardID = "TST-0003",
                ValidZones = new() { "frontend_0", "backend_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("backend");
    }

    // ═══════════════════════════════════════════════════════════════
    //  スケールアップ: conditional family
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScaleUp_ConditionalFamily_ConditionMet_UsesOverride()
    {
        var ai = new NpcAi(MakeConditionalFamilyConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.ScaleUp, SourceInstanceID = "db1", TargetRank = "medium", NeedsFamily = true },
        };
        var state = BuildState(
            available: available,
            configureMyField: f =>
            {
                f.Backend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0003", instanceId: "db1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);
                f.Backend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0004", instanceId: "db2", maxTP: null, currentTP: null, maxYield: 300, currentYield: 300);
            });

        var actions = ai.DecideMainPhaseActions(state);
        var scaleUp = actions.First(a => a.ActionType == ActionTypes.ScaleUp);

        ((ScaleUpRequest)scaleUp.Data).InstanceFamily.Should().Be("M");
    }

    [Fact]
    public void ScaleUp_ConditionalFamily_ConditionNotMet_UsesDefault()
    {
        var ai = new NpcAi(MakeConditionalFamilyConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.ScaleUp, SourceInstanceID = "db1", TargetRank = "medium", NeedsFamily = true },
        };
        var state = BuildState(
            available: available,
            configureMyField: f =>
                f.Backend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0003", instanceId: "db1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400));

        var actions = ai.DecideMainPhaseActions(state);
        var scaleUp = actions.First(a => a.ActionType == ActionTypes.ScaleUp);

        ((ScaleUpRequest)scaleUp.Data).InstanceFamily.Should().Be("R");
    }

    // ═══════════════════════════════════════════════════════════════
    //  収益化
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Monetize_ReserveRatio_LimitsDistribution()
    {
        var yaml = """
            model: test
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities: []
            effect_priorities: {}
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.5
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res1", RemainingCapacity = 800 },
        };
        var state = BuildState(insightPool: 1000, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var monetize = actions.FirstOrDefault(a => a.ActionType == ActionTypes.Monetize);

        monetize.Should().NotBeNull();
        var dists = ((MonetizeRequest)monetize!.Data).Distributions;
        var total = dists.Sum(d => d.Amount);

        total.Should().Be(500);
    }

    [Fact]
    public void Monetize_ZeroInsightPool_NoMonetizeAction()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res1", RemainingCapacity = 500 },
        };
        var state = BuildState(insightPool: 0, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_HighestTP_DistributesToHighestTPFirst()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_low", RemainingCapacity = 500 },
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_high", RemainingCapacity = 500 },
        };
        var state = BuildState(
            insightPool: 1000,
            available: available,
            configureMyField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "res_high", currentTP: 600);
                f.Frontend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "res_low", currentTP: 200);
            });

        var actions = ai.DecideMainPhaseActions(state);
        var monetize = actions.First(a => a.ActionType == ActionTypes.Monetize);
        var dists = ((MonetizeRequest)monetize.Data).Distributions;

        dists[0].InstanceID.Should().Be("res_high");
    }

    [Fact]
    public void Monetize_ReserveRatio_WithMultipleResources()
    {
        var yaml = """
            model: test
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities: []
            effect_priorities: {}
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.3
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_a", RemainingCapacity = 500 },
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_b", RemainingCapacity = 400 },
        };
        var state = BuildState(
            insightPool: 1000,
            available: available,
            configureMyField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "res_a", currentTP: 600);
                f.Frontend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "res_b", currentTP: 400);
            });

        var actions = ai.DecideMainPhaseActions(state);
        var monetize = actions.First(a => a.ActionType == ActionTypes.Monetize);
        var dists = ((MonetizeRequest)monetize.Data).Distributions;
        var total = dists.Sum(d => d.Amount);

        total.Should().Be(700);
    }

    [Fact]
    public void Monetize_NoMonetizeActions_NoAction()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(insightPool: 500, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.Monetize);
    }

    // ═══════════════════════════════════════════════════════════════
    //  即時効果カード
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Immediate_StrategyCard_PlayedWithPriority()
    {
        _cc.Add(new CardDefinition { CardId = "TST-STRAT", CardName = "TestStrategy", CardType = "Strategy" });
        _effects.SetEffectInfo("TST-STRAT", TriggerType.Ignition, new EffectInfo
        {
            TargetType = EffectTargetType.None,
        }.WithCategory(EffectCategory.BudgetGain));

        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_strat", CardID = "TST-STRAT" } };
        var available = new List<GD.AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_strat", CardID = "TST-STRAT",
                ValidZones = new() { "support_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var play = actions.FirstOrDefault(a =>
            a.ActionType == ActionTypes.PlayCard
            && ((PlayCardRequest)a.Data).CardInstanceID == "h_strat");

        play.Should().NotBeNull();
    }

    [Fact]
    public void Immediate_NoEffectInfo_NotPlayed()
    {
        _cc.Add(new CardDefinition { CardId = "TST-NOEFF", CardName = "NoEffect", CardType = "Strategy" });

        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_noeff", CardID = "TST-NOEFF" } };
        var available = new List<GD.AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_noeff", CardID = "TST-NOEFF",
                ValidZones = new() { "support_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a =>
            a.ActionType == ActionTypes.PlayCard
            && ((PlayCardRequest)a.Data).CardInstanceID == "h_noeff");
    }

    // ═══════════════════════════════════════════════════════════════
    //  ゲーム進行フェーズ overlay
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void LateGame_OverridesTargetSelection()
    {
        var yaml = """
            model: test-late
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            game_phases:
              late:
                condition:
                  turn_min: 6
                target_selection:
                  attack:
                    selector: { owner: opponent }
                    order_by: tp_desc
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            effect_priorities: {}
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = new() { "low_tp", "high_tp" } },
        };
        var state = BuildState(
            turn: 8, phase: "battle",
            available: available,
            configureOppField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "low_tp", currentTP: 200, maxAV: 2000);
                f.Frontend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "high_tp", currentTP: 900, maxAV: 400);
            });

        var actions = ai.DecideBattlePhaseActions(state);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("high_tp");
    }

    [Fact]
    public void LateGame_ConditionNotMet_UsesBaseConfig()
    {
        var yaml = """
            model: test-late
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            game_phases:
              late:
                condition:
                  turn_min: 6
                target_selection:
                  attack:
                    selector: { owner: opponent }
                    order_by: tp_desc
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            effect_priorities: {}
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = new() { "low_tp", "high_tp" } },
        };
        var state = BuildState(
            turn: 3, phase: "battle",
            available: available,
            configureOppField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "low_tp", currentTP: 200, maxAV: 400);
                f.Frontend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "high_tp", currentTP: 900, maxAV: 2000);
            });

        var actions = ai.DecideBattlePhaseActions(state);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("low_tp");
    }

    [Fact]
    public void LateGame_WithCountCondition_BothMustBeMet()
    {
        var yaml = """
            model: test-late
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            game_phases:
              late:
                condition:
                  turn_min: 6
                  count:
                    selector: { owner: myself }
                    min: 3
                target_selection:
                  attack:
                    selector: { owner: opponent }
                    order_by: tp_desc
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            effect_priorities: {}
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "own1", ValidTargets = new() { "low_tp", "high_tp" } },
        };
        var state = BuildState(
            turn: 8, phase: "battle",
            available: available,
            configureMyField: f =>
                f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "own1"),
            configureOppField: f =>
            {
                f.Frontend[0] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "low_tp", currentTP: 200, maxAV: 400);
                f.Frontend[1] = TestFactory.MakeWireResource(
                    cardId: "TST-0001", instanceId: "high_tp", currentTP: 900, maxAV: 2000);
            });

        var actions = ai.DecideBattlePhaseActions(state);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("low_tp");
    }

    // ═══════════════════════════════════════════════════════════════
    //  メインフェーズは常に EndPhase で終わる
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MainPhase_EmptyAvailable_StillEndsWithEndPhase()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = BuildState(available: new List<GD.AvailableAction>());

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(ActionTypes.EndPhase);
    }

    // ═══════════════════════════════════════════════════════════════
    //  デプロイ: アタッチメント
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_AttachmentsConfig_AttachmentsSeparatedFromResources()
    {
        var yaml = """
            model: test
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities:
                - card_type: Compute
                  priority: 50
            effect_priorities: {}
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            attachments:
              TST-0006:
                priority: 80
                target:
                  selector: { owner: myself }
                  order_by: tp_desc
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_sh1", CardID = "TST-0001" },
            new() { InstanceID = "h_att", CardID = "TST-0006" },
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att", CardID = "TST-0006",
                    ValidZones = new() { "support_0" }, ValidTargets = new() { "res1" } },
        };
        var state = BuildState(
            hand: hand,
            available: available,
            configureMyField: f =>
                f.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "res1"));

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
        ((PlayCardRequest)deploys[1].Data).CardInstanceID.Should().Be("h_att");
        ((PlayCardRequest)deploys[1].Data).TargetInstanceID.Should().Be("res1");
    }

    [Fact]
    public void Deploy_AttachmentPriority_HigherPriorityFirst()
    {
        _cc.Add(TestFactory.AttachmentCard(cardId: "TST-0008", name: "TestAttachment2"));

        var yaml = """
            model: test
            faction: SHE
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities: []
            effect_priorities: {}
            target_selection:
              attack:
                selector: { owner: opponent }
                order_by: av_asc
              single_damage:
                selector: { owner: opponent }
                order_by: av_asc
              debuff:
                selector: { owner: opponent }
                order_by: tp_desc
              buff:
                selector: { owner: myself }
                order_by: tp_desc
              heal:
                selector: { owner: myself }
                order_by: damage_desc
            scale_up:
              instance_family: M
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            attachments:
              TST-0006:
                priority: 40
              TST-0008:
                priority: 90
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_att1", CardID = "TST-0006" },
            new() { InstanceID = "h_att2", CardID = "TST-0008" },
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att1", CardID = "TST-0006",
                    ValidZones = new() { "support_0" }, ValidTargets = new() { "res1" } },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att2", CardID = "TST-0008",
                    ValidZones = new() { "support_1" }, ValidTargets = new() { "res1" } },
        };
        var state = BuildState(
            hand: hand,
            available: available,
            configureMyField: f =>
                f.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "res1"));

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_att2");
        ((PlayCardRequest)deploys[1].Data).CardInstanceID.Should().Be("h_att1");
    }

    [Fact]
    public void Deploy_NoAttachmentsConfig_AttachmentSkipped()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_att", CardID = "TST-0006" },
        };
        var available = new List<GD.AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att", CardID = "TST-0006",
                    ValidZones = new() { "support_0" }, ValidTargets = new() { "res1" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  テストダブル
    // ═══════════════════════════════════════════════════════════════

    private class StubEffectRegistry : IEffectRegistry
    {
        private readonly Dictionary<(string, TriggerType), EffectInfo> _infos = new();

        public void SetEffectInfo(string cardId, TriggerType trigger, EffectInfo info)
            => _infos[(cardId, trigger)] = info;

        public EffectHandler? Get(string cardId, TriggerType trigger) => null;
        public bool Has(string cardId, TriggerType trigger) => _infos.ContainsKey((cardId, trigger));
        public BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(string cardId, TriggerType trigger) =>
            _infos.GetValueOrDefault((cardId, trigger));
        public List<string>? GetChoiceOptions(string cardId, TriggerType trigger) => null;
        public IEffectOp[]? GetOps(string cardId, TriggerType trigger) => null;
    }
}

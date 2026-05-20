using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Npc;

public class NpcAiTests
{
    private readonly TestCardCache _cc = new();
    private readonly StubEffectRegistry _effects = new();

    public NpcAiTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-2001", tp: 600, av: 1400, mc: 150));
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-4005", tp: 500, av: 1200, mc: 120));
        _cc.Add(TestFactory.DataCard(cardId: "TST-1009", subtype: "Database", yield: 400, av: 800, mc: 100));
        _cc.Add(TestFactory.DataCard(cardId: "TST-4010", subtype: "Database", yield: 300, av: 600, mc: 80));
        _cc.Add(TestFactory.PlatformCard(cardId: "TST-1023", name: "TestPlatform"));
        _cc.Add(TestFactory.AttachmentCard(cardId: "TST-2022", name: "TestAttachment"));
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
                - card_id: TST-2001
                  priority: 80
                - card_type: Compute
                  priority: 50
                - card_type: Data
                  priority: 40
                - card_type: Platform
                  priority: 30
              choices:
                TST-2006: use
              zone_preferences:
                Compute: [frontend, backend]
                Data: [backend]
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
                - card_id: TST-4005
                  priority: 90
                  condition:
                    selector: { owner: myself }
                    card_id: [TST-4010]
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
                    card_type: Data
                    min: 2
              max_maintenance_ratio: 0.6
              order_by: tp_desc
            monetize:
              order_by: tp_desc
              reserve_ratio: 0.0
            """);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Discard
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void DecideDiscard_CheapestMaintenance_DiscardsCheapest()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.End);
        state.Player1Hand =
        [
            new() { InstanceID = "h_sh1", CardID = "TST-2001" },  // card_id pri 80 → kept
            new() { InstanceID = "h_tk5", CardID = "TST-4005" },  // compute pri 50 → discarded
            new() { InstanceID = "h_nt9", CardID = "TST-1009" },  // data pri 40 → discarded
        ];

        var discards = ai.DecideDiscard(state, 1, 2);

        discards.Should().Equal("h_nt9", "h_tk5");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Battle phase
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Battle_WeakestAV_AttacksLowestAV()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "strong", maxAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(instanceId: "weak", maxAV: 400);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = ["strong", "weak"] },
        };

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, available);
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

        var state = TestFactory.MakeGameState(phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "low_tp", currentTP: 200, maxAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "high_tp", currentTP: 900, maxAV: 400);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = ["low_tp", "high_tp"] },
        };

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, available);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("high_tp");
    }

    [Fact]
    public void Battle_NoAttackActions_OnlyEndPhase()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Battle);

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, []);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(ActionTypes.EndPhase);
    }

    [Fact]
    public void Battle_MultipleAttackers_AllAttack()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(instanceId: "target", maxAV: 500);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = ["target"] },
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk2", ValidTargets = ["target"] },
        };

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, available);
        var attacks = actions.Where(a => a.ActionType == ActionTypes.Attack).ToList();

        attacks.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Slot select
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void SlotSelect_ReturnsFirstValidZone()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState();
        state.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            ValidZones = ["frontend_0", "backend_1"],
        });

        var action = ai.DecideSlotSelect(state, 1);

        action.Should().NotBeNull();
        ((SelectSlotRequest)action!.Data).Zone.Should().Be("frontend");
        ((SelectSlotRequest)action.Data).Index.Should().Be(0);
    }

    [Fact]
    public void SlotSelect_NoPending_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        ai.DecideSlotSelect(TestFactory.MakeGameState(), 1).Should().BeNull();
    }

    [Fact]
    public void SlotSelect_WrongPlayer_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState();
        state.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 2,
            ValidZones = ["frontend_0"],
        });

        ai.DecideSlotSelect(state, 1).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deploy: priority resolution
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_CardIdPriority_TakesPrecedenceOverCardType()
    {
        // config: TST-2001 card_id=80, compute card_type=50
        // TST-2001 is compute, should get 80 not 50
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_tk5", CardID = "TST-4005" },  // compute, no card_id match → 50
            new() { InstanceID = "h_sh1", CardID = "TST-2001" },  // compute, card_id match → 80
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_tk5", CardID = "TST-4005", ValidZones = ["frontend_1"] },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-2001", ValidZones = ["frontend_0"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
    }

    [Fact]
    public void Deploy_UnknownCard_Priority0()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "UNKNOWN-001", tp: 100, av: 200, mc: 50));
        // No card_id or card_type match in config priorities for "UNKNOWN-001"
        // but it IS compute type so it matches card_type: compute → 50
        // Let's test with a truly unmatched type
        _cc.Add(new CardDefinition { CardId = "WEIRD-001", CardName = "Weird", CardType = "WeirdType" });

        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_weird", CardID = "WEIRD-001" },
            new() { InstanceID = "h_sh1", CardID = "TST-2001" },
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_weird", CardID = "WEIRD-001", ValidZones = ["frontend_1"] },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-2001", ValidZones = ["frontend_0"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        // TST-2001 (pri=80) should come before WEIRD-001 (pri=0)
        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
    }

    [Fact]
    public void Deploy_ConditionalPriority_ConditionMet_UsesPrimary()
    {
        var ai = new NpcAi(MakeConditionalPriorityConfig(), _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-4010", instanceId: "cosmo_1");
        state.Player1Hand =
        [
            new() { InstanceID = "hand_sh1", CardID = "TST-2001" },
            new() { InstanceID = "hand_tk5", CardID = "TST-4005" },
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_sh1", CardID = "TST-2001", ValidZones = ["frontend_1"] },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_tk5", CardID = "TST-4005", ValidZones = ["frontend_0"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        // TST-4005 (conditional 90) before TST-2001 (50)
        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("hand_tk5");
    }

    [Fact]
    public void Deploy_ConditionalPriority_ConditionNotMet_UsesFallback()
    {
        var ai = new NpcAi(MakeConditionalPriorityConfig(), _cc, _effects);

        // NO TST-4010 on field → fallback_priority=30
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "hand_sh1", CardID = "TST-2001" },
            new() { InstanceID = "hand_tk5", CardID = "TST-4005" },
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_sh1", CardID = "TST-2001", ValidZones = ["frontend_0"] },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "hand_tk5", CardID = "TST-4005", ValidZones = ["frontend_1"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        // TST-2001 (50) before TST-4005 (fallback 30)
        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("hand_sh1");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deploy: choice resolution
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_Choice_UsesConfigValue()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-2006", tp: 400, av: 1000));
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand = [new() { InstanceID = "h_0006", CardID = "TST-2006" }];

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_0006", CardID = "TST-2006",
                ValidZones = ["frontend_0"], ChoiceOptions = ["use", "reserve"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
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
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand = [new() { InstanceID = "h_unk", CardID = "UNKNOWN-C" }];

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_unk", CardID = "UNKNOWN-C",
                ValidZones = ["frontend_0"], ChoiceOptions = ["optionA", "optionB"],
            },
        };

        var act = () => ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No deploy choice configured*UNKNOWN-C*");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deploy: zone preferences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Deploy_ZonePreference_ComputePrefersFrontend()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand = [new() { InstanceID = "h_sh1", CardID = "TST-2001" }];

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-2001",
                ValidZones = ["backend_0", "frontend_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("frontend");
    }

    [Fact]
    public void Deploy_ZonePreference_DataPrefersBackend()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand = [new() { InstanceID = "h_db", CardID = "TST-1009" }];

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_db", CardID = "TST-1009",
                ValidZones = ["frontend_0", "backend_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("backend");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Scale up: conditional family
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScaleUp_ConditionalFamily_ConditionMet_UsesOverride()
    {
        var ai = new NpcAi(MakeConditionalFamilyConfig(), _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        // 2 data cards in backend → condition met
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: "TST-1009", instanceId: "db1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);
        state.Player1Field.Backend[1] = TestFactory.MakeResource(
            cardId: "TST-4010", instanceId: "db2", maxTP: null, currentTP: null, maxYield: 300, currentYield: 300);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.ScaleUp, SourceInstanceID = "db1", TargetRank = "medium", NeedsFamily = true },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var scaleUp = actions.First(a => a.ActionType == ActionTypes.ScaleUp);

        ((ScaleUpRequest)scaleUp.Data).InstanceFamily.Should().Be("M");
    }

    [Fact]
    public void ScaleUp_ConditionalFamily_ConditionNotMet_UsesDefault()
    {
        var ai = new NpcAi(MakeConditionalFamilyConfig(), _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        // Only 1 data card → condition NOT met
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: "TST-1009", instanceId: "db1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.ScaleUp, SourceInstanceID = "db1", TargetRank = "medium", NeedsFamily = true },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var scaleUp = actions.First(a => a.ActionType == ActionTypes.ScaleUp);

        ((ScaleUpRequest)scaleUp.Data).InstanceFamily.Should().Be("R");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Monetize
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

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1InsightPool = 1000;

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res1", RemainingCapacity = 800 },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var monetize = actions.FirstOrDefault(a => a.ActionType == ActionTypes.Monetize);

        monetize.Should().NotBeNull();
        var dists = ((MonetizeRequest)monetize!.Data).Distributions;
        var total = dists.Sum(d => d.Amount);

        // 50% reserve of 1000 = 500 distributable
        total.Should().Be(500);
    }

    [Fact]
    public void Monetize_ZeroInsightPool_NoMonetizeAction()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1InsightPool = 0;

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res1", RemainingCapacity = 500 },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_HighestTP_DistributesToHighestTPFirst()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1InsightPool = 1000;
        // res_high has TP=600, res_low has TP=200
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "res_high", currentTP: 600);
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "res_low", currentTP: 200);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_low", RemainingCapacity = 500 },
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_high", RemainingCapacity = 500 },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var monetize = actions.First(a => a.ActionType == ActionTypes.Monetize);
        var dists = ((MonetizeRequest)monetize.Data).Distributions;

        // highest_tp sorts by TP desc → res_high (TP=600) first
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

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1InsightPool = 1000;
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "res_a", currentTP: 600);
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "res_b", currentTP: 400);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_a", RemainingCapacity = 500 },
            new() { Type = ActionTypes.Monetize, SourceInstanceID = "res_b", RemainingCapacity = 400 },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var monetize = actions.First(a => a.ActionType == ActionTypes.Monetize);
        var dists = ((MonetizeRequest)monetize.Data).Distributions;
        var total = dists.Sum(d => d.Amount);

        // 30% reserve of 1000 = 300 reserved → 700 distributable (capacity 合計 900 > 700)
        total.Should().Be(700);
    }

    [Fact]
    public void Monetize_NoMonetizeActions_NoAction()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1InsightPool = 500;

        // No monetize actions in available
        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-2001", ValidZones = ["frontend_0"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.Monetize);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Immediate cards
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
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand = [new() { InstanceID = "h_strat", CardID = "TST-STRAT" }];

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_strat", CardID = "TST-STRAT",
                ValidZones = ["support_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
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
        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand = [new() { InstanceID = "h_noeff", CardID = "TST-NOEFF" }];

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = ActionTypes.PlayCard, HandInstanceID = "h_noeff", CardID = "TST-NOEFF",
                ValidZones = ["support_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);

        actions.Should().NotContain(a =>
            a.ActionType == ActionTypes.PlayCard
            && ((PlayCardRequest)a.Data).CardInstanceID == "h_noeff");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Game phase overlay
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

        var state = TestFactory.MakeGameState(turn: 8, phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "low_tp", currentTP: 200, maxAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "high_tp", currentTP: 900, maxAV: 400);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = ["low_tp", "high_tp"] },
        };

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, available);
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

        // Turn 3 → late condition NOT met
        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "low_tp", currentTP: 200, maxAV: 400);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "high_tp", currentTP: 900, maxAV: 2000);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "atk1", ValidTargets = ["low_tp", "high_tp"] },
        };

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, available);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        // weakest_av → low_tp has AV=400, high_tp has AV=2000
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

        // Turn 8 but only 1 resource → count condition NOT met → base config
        var state = TestFactory.MakeGameState(turn: 8, phase: Phase.Battle);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(instanceId: "own1");
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "low_tp", currentTP: 200, maxAV: 400);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TST-2001", instanceId: "high_tp", currentTP: 900, maxAV: 2000);

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.Attack, SourceInstanceID = "own1", ValidTargets = ["low_tp", "high_tp"] },
        };

        var actions = ai.DecideBattlePhaseActions(state, new Game { GameID = "t" }, 1, available);
        var attack = actions.First(a => a.ActionType == ActionTypes.Attack);

        // Still weakest_av because count condition not met
        ((AttackRequest)attack.Data).TargetInstanceID.Should().Be("low_tp");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main phase always ends with EndPhase
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MainPhase_EmptyAvailable_StillEndsWithEndPhase()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = TestFactory.MakeGameState(phase: Phase.Main);

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, []);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(ActionTypes.EndPhase);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Deploy: attachments
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
              TST-2022:
                priority: 80
                target:
                  selector: { owner: myself }
                  order_by: tp_desc
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_sh1", CardID = "TST-2001" },
            new() { InstanceID = "h_att", CardID = "TST-2022" },
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_sh1", CardID = "TST-2001", ValidZones = ["frontend_0"] },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att", CardID = "TST-2022",
                    ValidZones = ["support_0"], ValidTargets = ["res1"] },
        };
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-2001", instanceId: "res1");

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        // Both deployed: compute resource first, then attachment with target
        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
        ((PlayCardRequest)deploys[1].Data).CardInstanceID.Should().Be("h_att");
        ((PlayCardRequest)deploys[1].Data).TargetInstanceID.Should().Be("res1");
    }

    [Fact]
    public void Deploy_AttachmentPriority_HigherPriorityFirst()
    {
        _cc.Add(TestFactory.AttachmentCard(cardId: "TST-1003", name: "TestAttachment2"));

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
              TST-2022:
                priority: 40
              TST-1003:
                priority: 90
            """;
        var config = AiConfigLoader.LoadFromString(yaml);
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-2001", instanceId: "res1");
        state.Player1Hand =
        [
            new() { InstanceID = "h_att1", CardID = "TST-2022" },
            new() { InstanceID = "h_att2", CardID = "TST-1003" },
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att1", CardID = "TST-2022",
                    ValidZones = ["support_0"], ValidTargets = ["res1"] },
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att2", CardID = "TST-1003",
                    ValidZones = ["support_1"], ValidTargets = ["res1"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        // TST-1003 (pri=90) before TST-2022 (pri=40)
        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_att2");
        ((PlayCardRequest)deploys[1].Data).CardInstanceID.Should().Be("h_att1");
    }

    [Fact]
    public void Deploy_NoAttachmentsConfig_AttachmentSkipped()
    {
        // MakeConfig() has no attachments section → attachment always skipped
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_att", CardID = "TST-2022" },
        ];

        var available = new List<AvailableAction>
        {
            new() { Type = ActionTypes.PlayCard, HandInstanceID = "h_att", CardID = "TST-2022",
                    ValidZones = ["support_0"], ValidTargets = ["res1"] },
        };

        var actions = ai.DecideMainPhaseActions(state, new Game { GameID = "t" }, 1, available);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Test doubles
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

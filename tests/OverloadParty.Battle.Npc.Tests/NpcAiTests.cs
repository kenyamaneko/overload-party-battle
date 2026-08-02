using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

[Trait("対象", "NPC の意思決定")]
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
              zone_preferences:
                Compute: [frontend, backend]
                DataResource: [backend]
                Platform: [support]
            branch_choices:
              TST-0007: use
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

    [Fact(DisplayName = "手札調整では維持コストが小さいカードから捨てる")]
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

    [Trait("対象", "手札調整のカードタイプ順序")]
    public class DiscardTypeOrdering
    {
        private static AiConfig MakeDiscardOrderingConfig()
        {
            return AiConfigLoader.LoadFromString("""
                model: test
                faction: SHE
                deploy:
                  priorities:
                    - card_id: TST-0001
                      priority: 80
                attachments:
                  TST-0006:
                    priority: 50
                reactive:
                  max_slots: 2
                  priorities:
                    TST-0400: 50
                """);
        }

        [Fact(DisplayName = "手札がアタッチメント・リソース・ストラテジー各 1 枚で 2 枚捨てるとき、アタッチメント→リソースの順に捨ててストラテジーを残す")]
        public void AttachmentThenResource_BeforeStrategy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0006"));
            cc.Add(new CardDefinition { CardId = "TST-0700", CardName = "TestStrategy", CardType = CardTypes.Strategy });
            var ai = new NpcAi(MakeDiscardOrderingConfig(), cc, new StubEffectRegistry());
            var hand = new List<GD.UndeployedCard>
            {
                new() { InstanceID = "h_att", CardID = "TST-0006" },
                new() { InstanceID = "h_res", CardID = "TST-0001" },
                new() { InstanceID = "h_str", CardID = "TST-0700" },
            };
            var state = BuildState(phase: "end", hand: hand);

            var discards = ai.DecideDiscard(state, 2);

            discards.Should().Equal("h_att", "h_res");
        }

        [Fact(DisplayName = "手札がリアクティブとインシデントで 1 枚捨てるとき、リアクティブを捨てる")]
        public void ReactiveBeforeIncident()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            cc.Add(new CardDefinition { CardId = "TST-0800", CardName = "TestIncident", CardType = CardTypes.Incident });
            var ai = new NpcAi(MakeDiscardOrderingConfig(), cc, new StubEffectRegistry());
            var hand = new List<GD.UndeployedCard>
            {
                new() { InstanceID = "h_rea", CardID = "TST-0400" },
                new() { InstanceID = "h_inc", CardID = "TST-0800" },
            };
            var state = BuildState(phase: "end", hand: hand);

            var discards = ai.DecideDiscard(state, 1);

            discards.Should().Equal("h_rea");
        }

        [Fact(DisplayName = "attachments 設定に無いアタッチメントを含む手札を調整すると、InvalidOperationException になる")]
        public void UnconfiguredAttachment_Throws()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0006"));
            var ai = new NpcAi(MakeConfig(), cc, new StubEffectRegistry());
            var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_att", CardID = "TST-0006" } };
            var state = BuildState(phase: "end", hand: hand);

            var act = () => ai.DecideDiscard(state, 1);

            act.Should().Throw<InvalidOperationException>().WithMessage("*TST-0006*");
        }

        [Fact(DisplayName = "reactive 設定に無いリアクティブを含む手札を調整すると、InvalidOperationException になる")]
        public void UnconfiguredReactive_Throws()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var ai = new NpcAi(MakeConfig(), cc, new StubEffectRegistry());
            var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_rea", CardID = "TST-0400" } };
            var state = BuildState(phase: "end", hand: hand);

            var act = () => ai.DecideDiscard(state, 1);

            act.Should().Throw<InvalidOperationException>().WithMessage("*TST-0400*");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  バトルフェーズ
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "av_asc のとき、可用性が最も低い相手リソースを攻撃する")]
    public void Battle_WeakestAV_AttacksLowestAV()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() { "strong", "weak" } },
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

    [Fact(DisplayName = "tp_desc のとき、スループットが最も高い相手リソースを攻撃する")]
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
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() { "low_tp", "high_tp" } },
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

    [Fact(DisplayName = "攻撃アクションが無いとき、エンドフェーズだけを返す")]
    public void Battle_NoAttackActions_OnlyEndPhase()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var state = BuildState(phase: "battle", available: new List<GD.AvailableAction>());

        var actions = ai.DecideBattlePhaseActions(state);

        actions.Should().HaveCount(1);
        actions[0].ActionType.Should().Be(ActionTypes.EndPhase);
    }

    [Fact(DisplayName = "複数の攻撃可能リソースがあるとき、全てで攻撃する")]
    public void Battle_MultipleAttackers_AllAttack()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() { "target" } },
            new GD.AttackAction { SourceInstanceID = "atk2", ValidTargets = new() { "target" } },
        };
        var state = BuildState(
            phase: "battle",
            configureOppField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "target", maxAV: 500),
            available: available);

        var actions = ai.DecideBattlePhaseActions(state);
        var attacks = actions.Where(a => a.ActionType == ActionTypes.Attack).ToList();

        attacks.Select(a => ((AttackRequest)a.Data).AttackerInstanceID)
            .Should().BeEquivalentTo(new[] { "atk1", "atk2" }, "2 体のリソースがそれぞれ攻撃する");
        attacks.Should().OnlyContain(a => ((AttackRequest)a.Data).TargetInstanceID == "target",
            "唯一の相手リソースが両方の攻撃の対象になる");
    }

    [Fact(DisplayName = "攻撃アクションに有効な対象が無いとき、例外になる")]
    public void Battle_NoValidTargets_Throws()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var available = new List<GD.AvailableAction>
        {
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() },
        };
        var state = BuildState(phase: "battle", available: available);

        var act = () => ai.DecideBattlePhaseActions(state);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no valid targets*");
    }

    [Fact(DisplayName = "攻撃対象の選択設定が無いとき、例外になる")]
    public void Battle_NoAttackTargetSelectionConfig_Throws()
    {
        var config = AiConfigLoader.LoadFromString("""
            model: test
            faction: SHE
            """);
        var ai = new NpcAi(config, _cc, _effects);
        var available = new List<GD.AvailableAction>
        {
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() { "target" } },
        };
        var state = BuildState(phase: "battle", available: available);

        var act = () => ai.DecideBattlePhaseActions(state);

        act.Should().Throw<InvalidOperationException>().WithMessage("*No attack target selection configured*");
    }

    // ═══════════════════════════════════════════════════════════════
    //  スロット選択
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "スロット選択待ちのとき、先頭の有効ゾーンを選ぶ")]
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

    [Fact(DisplayName = "スロット選択待ちが無いとき、null を返す")]
    public void SlotSelect_NoPending_ReturnsNull()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        ai.DecideSlotSelect(BuildState()).Should().BeNull();
    }

    [Fact(DisplayName = "有効ゾーンが空のとき、null を返す")]
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

    [Fact(DisplayName = "効果中の選択で、先頭の候補を選ぶ")]
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
        // resolve_pending_choice は 1 件で、選択肢を ChoiceOptions に持つ。
        var available = new List<GD.AvailableAction>
        {
            new GD.ResolvePendingChoiceAction
            {
                EffectCardId = "TST-0002",
                ChoiceKind = ChoiceKinds.HandCard,
                ChoiceOptions =
                [
                    new GD.ChoiceOption { Key = "TST-0001" },
                    new GD.ChoiceOption { Key = "TST-0002" },
                ],
            },
        };
        var state = BuildState(pendingEffectChoice: pending, available: available);

        var action = ai.DecidePendingEffectChoice(state);

        action.Should().NotBeNull();
        action!.ActionType.Should().Be(ActionTypes.ResolvePendingChoice);
        ((ResolvePendingChoiceRequest)action.Data).ChosenId.Should().Be("TST-0001");
    }

    [Fact(DisplayName = "裏向きリアクティブを確認する選択で、先頭の候補を選ぶ")]
    public void PendingEffectChoice_FaceDownReactive_PicksFirstCandidate()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = 1,
            EffectCardId = "initiative:IN-TST",
            EffectInstanceId = "IN-TST",
            ChoiceKind = ChoiceKinds.FaceDownReactive,
        };
        var available = new List<GD.AvailableAction>
        {
            new GD.ResolvePendingChoiceAction
            {
                EffectCardId = "initiative:IN-TST",
                ChoiceKind = ChoiceKinds.FaceDownReactive,
                ChoiceOptions =
                [
                    new GD.ChoiceOption { Key = "sup_1" },
                    new GD.ChoiceOption { Key = "sup_2" },
                ],
            },
        };
        var state = BuildState(pendingEffectChoice: pending, available: available);

        var action = ai.DecidePendingEffectChoice(state);

        action.Should().NotBeNull();
        ((ResolvePendingChoiceRequest)action!.Data).ChosenId.Should().Be("sup_1");
    }

    [Fact(DisplayName = "選択者が自分でないとき、null を返す")]
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

    [Fact(DisplayName = "解決アクションが無いとき、null を返す")]
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

    [Fact(DisplayName = "効果中選択の候補が 0 件のとき、例外になる")]
    public void PendingEffectChoice_NoChoiceOptions_Throws()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = 1,
            EffectCardId = "TST-0002",
            EffectInstanceId = "inst_1",
            ChoiceKind = ChoiceKinds.HandCard,
        };
        var available = new List<GD.AvailableAction>
        {
            new GD.ResolvePendingChoiceAction
            {
                EffectCardId = "TST-0002",
                ChoiceKind = ChoiceKinds.HandCard,
                ChoiceOptions = [],
            },
        };
        var state = BuildState(pendingEffectChoice: pending, available: available);

        var act = () => ai.DecidePendingEffectChoice(state);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no choice options*");
    }

    [Fact(DisplayName = "未知の選択種別のとき、例外になる")]
    public void PendingEffectChoice_UnknownChoiceKind_Throws()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var pending = new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = 1,
            EffectCardId = "TST-0002",
            EffectInstanceId = "inst_1",
            ChoiceKind = "TST-unknown",
        };
        var available = new List<GD.AvailableAction>
        {
            new GD.ResolvePendingChoiceAction
            {
                EffectCardId = "TST-0002",
                ChoiceKind = "TST-unknown",
                ChoiceOptions = [new GD.ChoiceOption { Key = "TST-0001" }],
            },
        };
        var state = BuildState(pendingEffectChoice: pending, available: available);

        var act = () => ai.DecidePendingEffectChoice(state);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown choice kind*");
    }

    // ═══════════════════════════════════════════════════════════════
    //  デプロイ: priority 解決
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "card_id 優先度が card_type 優先度より優先される")]
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
            new GD.PlayCardAction { HandInstanceID = "h_tk5", CardID = "TST-0002", ValidZones = new() { "frontend_1" } },
            new GD.PlayCardAction { HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
    }

    [Fact(DisplayName = "未知のカードは優先度 0 として扱う")]
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
            new GD.PlayCardAction { HandInstanceID = "h_weird", CardID = "WEIRD-001", ValidZones = new() { "frontend_1" } },
            new GD.PlayCardAction { HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().HaveCount(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_sh1");
    }

    [Fact(DisplayName = "条件付き優先度は条件を満たすとき、primary の優先度を使う")]
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
            new GD.PlayCardAction { HandInstanceID = "hand_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_1" } },
            new GD.PlayCardAction { HandInstanceID = "hand_tk5", CardID = "TST-0002", ValidZones = new() { "frontend_0" } },
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

    [Fact(DisplayName = "条件付き優先度は条件を満たさないとき、fallback の優先度を使う")]
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
            new GD.PlayCardAction { HandInstanceID = "hand_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
            new GD.PlayCardAction { HandInstanceID = "hand_tk5", CardID = "TST-0002", ValidZones = new() { "frontend_1" } },
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

    [Trait("対象", "分岐選択の解決")]
    public class BranchChoiceResolution
    {
        [Fact(DisplayName = "設定に分岐回答があるとき、その値で分岐選択を解決する")]
        public void UsesConfiguredChoice()
        {
            var ai = new NpcAi(MakeConfig(), new TestCardCache(), new StubEffectRegistry());
            var pending = new GD.PendingEffectChoiceView
            {
                ChooserPlayerNum = 1,
                EffectCardId = "TST-0007",
                EffectInstanceId = "inst_1",
                ChoiceKind = ChoiceKinds.Branch,
            };
            // config が "use" を指すので、先頭の "reserve" でなく config 値を選ぶ。
            var available = new List<GD.AvailableAction>
            {
                new GD.ResolvePendingChoiceAction
                {
                    EffectCardId = "TST-0007",
                    ChoiceKind = ChoiceKinds.Branch,
                    ChoiceOptions =
                    [
                        new GD.ChoiceOption { Key = "reserve" },
                        new GD.ChoiceOption { Key = "use" },
                    ],
                },
            };
            var state = BuildState(pendingEffectChoice: pending, available: available);

            var action = ai.DecidePendingEffectChoice(state);

            ((ResolvePendingChoiceRequest)action!.Data).ChosenId.Should().Be("use");
        }

        [Fact(DisplayName = "設定に該当カードの分岐回答が無いとき、例外を投げる")]
        public void NotConfigured_Throws()
        {
            var ai = new NpcAi(MakeConfig(), new TestCardCache(), new StubEffectRegistry());
            var pending = new GD.PendingEffectChoiceView
            {
                ChooserPlayerNum = 1,
                EffectCardId = "TST-9999",
                EffectInstanceId = "inst_1",
                ChoiceKind = ChoiceKinds.Branch,
            };
            var available = new List<GD.AvailableAction>
            {
                new GD.ResolvePendingChoiceAction
                {
                    EffectCardId = "TST-9999",
                    ChoiceKind = ChoiceKinds.Branch,
                    ChoiceOptions =
                    [
                        new GD.ChoiceOption { Key = "reserve" },
                        new GD.ChoiceOption { Key = "use" },
                    ],
                },
            };
            var state = BuildState(pendingEffectChoice: pending, available: available);

            var act = () => ai.DecidePendingEffectChoice(state);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*No branch choice configured*TST-9999*");
        }

        [Fact(DisplayName = "設定した分岐回答が候補に無いとき、例外を投げる")]
        public void ConfiguredChoiceNotInOptions_Throws()
        {
            var ai = new NpcAi(MakeConfig(), new TestCardCache(), new StubEffectRegistry());
            var pending = new GD.PendingEffectChoiceView
            {
                ChooserPlayerNum = 1,
                EffectCardId = "TST-0007",
                EffectInstanceId = "inst_1",
                ChoiceKind = ChoiceKinds.Branch,
            };
            // config は "use" を指すが、候補には "keep" しか無い。
            var available = new List<GD.AvailableAction>
            {
                new GD.ResolvePendingChoiceAction
                {
                    EffectCardId = "TST-0007",
                    ChoiceKind = ChoiceKinds.Branch,
                    ChoiceOptions = [new GD.ChoiceOption { Key = "keep" }],
                },
            };
            var state = BuildState(pendingEffectChoice: pending, available: available);

            var act = () => ai.DecidePendingEffectChoice(state);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*use*not an available option*");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  デプロイ: zone preferences
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Compute系リソースはフロントエンドにデプロイする")]
    public void Deploy_ZonePreference_ComputePrefersFrontend()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_sh1", CardID = "TST-0001" } };
        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction
            {
                HandInstanceID = "h_sh1", CardID = "TST-0001",
                ValidZones = new() { "backend_0", "frontend_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("frontend");
    }

    [Fact(DisplayName = "Data系リソースはバックエンドにデプロイする")]
    public void Deploy_ZonePreference_DataPrefersBackend()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_db", CardID = "TST-0003" } };
        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction
            {
                HandInstanceID = "h_db", CardID = "TST-0003",
                ValidZones = new() { "frontend_0", "backend_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("backend");
    }

    [Fact(DisplayName = "オブジェクトストレージは、専用のゾーン設定に従いフロントエンドへデプロイされる")]
    public void Deploy_ZonePreference_ObjectStoragePrefersFrontend()
    {
        _cc.Add(TestFactory.DataCard(cardId: "TST-0500", subtype: "ObjectStorage"));
        var config = AiConfigLoader.LoadFromString("""
            model: test
            faction: SHE
            budget:
              maintenance_limit_ratio: 0.8
            deploy:
              zone_preferences:
                DataResource: [backend]
                ObjectStorage: [frontend]
            scale_up:
              order_by: tp_desc
            """);
        var ai = new NpcAi(config, _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_os", CardID = "TST-0500" } };
        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction
            {
                HandInstanceID = "h_os", CardID = "TST-0500",
                ValidZones = new() { "backend_0", "frontend_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploy = actions.First(a => a.ActionType == ActionTypes.PlayCard);
        var req = (PlayCardRequest)deploy.Data;

        req.Zone.Should().Be("frontend");
    }

    [Fact(DisplayName = "データベースは、Data系のゾーン設定に従いバックエンドへデプロイされる")]
    public void Deploy_ZonePreference_DatabasePrefersBackendAlongsideObjectStorageConfig()
    {
        var config = AiConfigLoader.LoadFromString("""
            model: test
            faction: SHE
            budget:
              maintenance_limit_ratio: 0.8
            deploy:
              zone_preferences:
                DataResource: [backend]
                ObjectStorage: [frontend]
            scale_up:
              order_by: tp_desc
            """);
        var ai = new NpcAi(config, _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_db", CardID = "TST-0003" } };
        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction
            {
                HandInstanceID = "h_db", CardID = "TST-0003",
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

    [Fact(DisplayName = "条件付きファミリーは条件を満たすとき、上書きの M を使う")]
    public void ScaleUp_ConditionalFamily_ConditionMet_UsesOverride()
    {
        var ai = new NpcAi(MakeConditionalFamilyConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.ScaleUpAction { SourceInstanceID = "db1", TargetRank = "medium", NeedsFamily = true },
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

    [Fact(DisplayName = "条件付きファミリーは条件を満たさないとき、既定の R を使う")]
    public void ScaleUp_ConditionalFamily_ConditionNotMet_UsesDefault()
    {
        var ai = new NpcAi(MakeConditionalFamilyConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.ScaleUpAction { SourceInstanceID = "db1", TargetRank = "medium", NeedsFamily = true },
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

    [Fact(DisplayName = "reserve_ratio 0.5 のとき、収益化の分配合計が 500 になる")]
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
            new GD.MonetizeAction { SourceInstanceID = "res1", RemainingCapacity = 800 },
        };
        var state = BuildState(insightPool: 1000, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var monetize = actions.FirstOrDefault(a => a.ActionType == ActionTypes.Monetize);

        monetize.Should().NotBeNull();
        var dists = ((MonetizeRequest)monetize!.Data).Distributions;
        var total = dists.Sum(d => d.Amount);

        total.Should().Be(500);
    }

    [Fact(DisplayName = "インサイトプールが 0 のとき、収益化アクションを出さない")]
    public void Monetize_ZeroInsightPool_NoMonetizeAction()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.MonetizeAction { SourceInstanceID = "res1", RemainingCapacity = 500 },
        };
        var state = BuildState(insightPool: 0, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.Monetize);
    }

    [Fact(DisplayName = "スループットが最も高いリソースから先に分配する")]
    public void Monetize_HighestTP_DistributesToHighestTPFirst()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.MonetizeAction { SourceInstanceID = "res_low", RemainingCapacity = 500 },
            new GD.MonetizeAction { SourceInstanceID = "res_high", RemainingCapacity = 500 },
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

    [Fact(DisplayName = "reserve_ratio 0.3 のとき、複数リソースへの分配合計が 700 になる")]
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
            new GD.MonetizeAction { SourceInstanceID = "res_a", RemainingCapacity = 500 },
            new GD.MonetizeAction { SourceInstanceID = "res_b", RemainingCapacity = 400 },
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

    [Fact(DisplayName = "収益化アクションが無いとき、収益化を出さない")]
    public void Monetize_NoMonetizeActions_NoAction()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction { HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
        };
        var state = BuildState(insightPool: 500, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.Monetize);
    }

    // ═══════════════════════════════════════════════════════════════
    //  即時効果カード
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "効果を持つストラテジーは優先度に従ってプレイされる")]
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
            new GD.PlayCardAction
            {
                HandInstanceID = "h_strat", CardID = "TST-STRAT",
                ValidZones = new() { "support_0" },
            },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        // 即時ストラテジーは他アクションより先に決定されるため列の先頭に来る
        actions[0].ActionType.Should().Be(ActionTypes.PlayCard);
        ((PlayCardRequest)actions[0].Data).CardInstanceID.Should().Be("h_strat");
    }

    [Fact(DisplayName = "効果情報が無いカードはプレイされない")]
    public void Immediate_NoEffectInfo_NotPlayed()
    {
        _cc.Add(new CardDefinition { CardId = "TST-NOEFF", CardName = "NoEffect", CardType = "Strategy" });

        var ai = new NpcAi(MakeConfig(), _cc, _effects);
        var hand = new List<GD.UndeployedCard> { new() { InstanceID = "h_noeff", CardID = "TST-NOEFF" } };
        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction
            {
                HandInstanceID = "h_noeff", CardID = "TST-NOEFF",
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

    [Fact(DisplayName = "late フェーズ条件を満たすとき、ターゲット選択を上書きする")]
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
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() { "low_tp", "high_tp" } },
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

    [Fact(DisplayName = "late フェーズ条件を満たさないとき、基本設定のターゲット選択を使う")]
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
            new GD.AttackAction { SourceInstanceID = "atk1", ValidTargets = new() { "low_tp", "high_tp" } },
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

    [Fact(DisplayName = "late フェーズは turn_min と count の両方を満たすとき適用される")]
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
            new GD.AttackAction { SourceInstanceID = "own1", ValidTargets = new() { "low_tp", "high_tp" } },
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

    [Fact(DisplayName = "利用可能アクションが空でも、メインフェーズはエンドフェーズで終わる")]
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

    [Fact(DisplayName = "attachments 設定があるとき、アタッチメントをリソースと分けてデプロイする")]
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
            new GD.PlayCardAction { HandInstanceID = "h_sh1", CardID = "TST-0001", ValidZones = new() { "frontend_0" } },
            new GD.PlayCardAction { HandInstanceID = "h_att", CardID = "TST-0006",
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

    [Fact(DisplayName = "アタッチメントは優先度が高いものから先にデプロイする")]
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
            new GD.PlayCardAction { HandInstanceID = "h_att1", CardID = "TST-0006",
                    ValidZones = new() { "support_0" }, ValidTargets = new() { "res1" } },
            new GD.PlayCardAction { HandInstanceID = "h_att2", CardID = "TST-0008",
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

    [Fact(DisplayName = "attachments 設定が無いとき、アタッチメントをデプロイしない")]
    public void Deploy_NoAttachmentsConfig_AttachmentSkipped()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects);

        var hand = new List<GD.UndeployedCard>
        {
            new() { InstanceID = "h_att", CardID = "TST-0006" },
        };
        var available = new List<GD.AvailableAction>
        {
            new GD.PlayCardAction { HandInstanceID = "h_att", CardID = "TST-0006",
                    ValidZones = new() { "support_0" }, ValidTargets = new() { "res1" } },
        };
        var state = BuildState(hand: hand, available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var deploys = actions.Where(a => a.ActionType == ActionTypes.PlayCard).ToList();

        deploys.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    //  施策 (UseInitiative)
    // ═══════════════════════════════════════════════════════════════

    private const string RoutineInitiativeId = "TST-IN-R";
    private const string SpecialInitiativeId = "TST-IN-S";

    private static InitiativeCatalog MakeInitiativeCatalog()
    {
        return new InitiativeCatalog(new List<Initiative>
        {
            new() { InitiativeId = RoutineInitiativeId, Kind = InitiativeKinds.Routine },
            new() { InitiativeId = SpecialInitiativeId, Kind = InitiativeKinds.Special },
        });
    }

    private static AiConfig MakeRoutineSpecialConfig()
    {
        return AiConfigLoader.LoadFromString("""
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
            initiative:
              TST-IN-R:
                priority: 50
                condition:
                  selector: { owner: opponent }
                  min: 1
              TST-IN-S:
                priority: 60
                min_insight: 500
            """);
    }

    private static AiConfig MakeChoiceRoutineConfig()
    {
        return AiConfigLoader.LoadFromString("""
            model: test
            faction: Sugar
            budget:
              low_threshold: 1500
              maintenance_limit_ratio: 0.8
            deploy:
              priorities:
                - card_type: Compute
                  priority: 50
            effect_priorities: {}
            target_selection:
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
            initiative:
              TST-IN-R:
                priority: 50
            """);
    }

    [Fact(DisplayName = "ルーチン施策は条件を満たすとき使用される")]
    public void Initiative_RoutineConditionMet_Emitted()
    {
        var ai = new NpcAi(MakeRoutineSpecialConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
        };
        var state = BuildState(
            configureOppField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "o1"),
            available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().Contain(a =>
            a.ActionType == ActionTypes.UseInitiative
            && ((UseInitiativeRequest)a.Data).Kind == InitiativeKinds.Routine);
    }

    [Fact(DisplayName = "ルーチン施策は条件を満たさないとき使用されない")]
    public void Initiative_RoutineConditionUnmet_NotEmitted()
    {
        var ai = new NpcAi(MakeRoutineSpecialConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
        };
        var state = BuildState(available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.UseInitiative);
    }

    [Fact(DisplayName = "initiative 設定が無いとき、施策を使用しない")]
    public void Initiative_NoInitiativeConfig_NotEmitted()
    {
        var ai = new NpcAi(MakeConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
        };
        var state = BuildState(
            configureOppField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "o1"),
            available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.UseInitiative);
    }

    [Fact(DisplayName = "施策カタログが無いとき、施策を使用しない")]
    public void Initiative_NoCatalog_NotEmitted()
    {
        var ai = new NpcAi(MakeRoutineSpecialConfig(), _cc, _effects);
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
        };
        var state = BuildState(
            configureOppField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "o1"),
            available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.UseInitiative);
    }

    [Theory(DisplayName = "スペシャル施策は min_insight 500 を満たすインサイトプールのときだけ使用される")]
    [InlineData(400, false)]
    [InlineData(600, true)]
    public void Initiative_SpecialMinInsight_GatesUsage(long insightPool, bool expectedUsed)
    {
        var ai = new NpcAi(MakeRoutineSpecialConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Special, CardID = SpecialInitiativeId },
        };
        var state = BuildState(insightPool: insightPool, available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Any(a => a.ActionType == ActionTypes.UseInitiative).Should().Be(expectedUsed);
    }

    [Fact(DisplayName = "選択効果を持つ施策はターゲットを解決して使用される")]
    public void Initiative_ChoiceEffect_ResolvesTarget()
    {
        _effects.SetEffectInfo(
            "initiative:" + RoutineInitiativeId, TriggerType.Ignition,
            new EffectInfo { TargetType = EffectTargetType.Choice }.WithCategory(EffectCategory.Heal));
        var ai = new NpcAi(MakeChoiceRoutineConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
        };
        var state = BuildState(
            configureMyField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "dmg1", damage: 300),
            available: available);

        var actions = ai.DecideMainPhaseActions(state);
        var initiative = actions.First(a => a.ActionType == ActionTypes.UseInitiative);

        ((UseInitiativeRequest)initiative.Data).ChoiceData!["instanceId"].Should().Be("dmg1");
    }

    [Fact(DisplayName = "選択効果の対象が無いとき、施策を使用しない")]
    public void Initiative_ChoiceEffect_NoTarget_NotEmitted()
    {
        _effects.SetEffectInfo(
            "initiative:" + RoutineInitiativeId, TriggerType.Ignition,
            new EffectInfo { TargetType = EffectTargetType.Choice }.WithCategory(EffectCategory.Heal));
        var ai = new NpcAi(MakeChoiceRoutineConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
        };
        var state = BuildState(available: available);

        var actions = ai.DecideMainPhaseActions(state);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.UseInitiative);
    }

    [Fact(DisplayName = "ルーチンとスペシャルの両方があるとき、優先度順でスペシャルが先に並ぶ")]
    public void Initiative_BothKinds_SpecialOrderedFirstByPriority()
    {
        var ai = new NpcAi(MakeRoutineSpecialConfig(), _cc, _effects, MakeInitiativeCatalog());
        var available = new List<GD.AvailableAction>
        {
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Routine, CardID = RoutineInitiativeId },
            new GD.UseInitiativeAction { Kind = InitiativeKinds.Special, CardID = SpecialInitiativeId },
        };
        var state = BuildState(
            insightPool: 600,
            configureOppField: f => f.Frontend[0] = TestFactory.MakeWireResource(instanceId: "o1"),
            available: available);

        var actions = ai.DecideMainPhaseActions(state)
            .Where(a => a.ActionType == ActionTypes.UseInitiative)
            .Select(a => ((UseInitiativeRequest)a.Data).Kind)
            .ToList();

        actions.Should().Equal(InitiativeKinds.Special, InitiativeKinds.Routine);
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
        public void RegisterPassive(string cardId, PassiveEffectDef def) { }
        public IReadOnlyList<PassiveEffectDef> GetPassives(string cardId) => [];
    }
}

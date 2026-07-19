using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 破壊のライフサイクル (SLA ペナルティ減算・on_destroy 発火・アタッチメントの連れトラッシュ・常時効果再計算) が
/// 破壊経路 (効果ダメージ・インシデント・同時破壊・destroy_check・spot_expiry) を問わず一元化されていることを検証する。
/// 攻撃経路の破壊は AttackProcessorTests.cs 側に置く。
/// </summary>
public class DestructionTests
{
    private static UseIgnitionRequest Use(string instanceId, string? targetInstanceId = null) =>
        new() { InstanceID = instanceId, TargetInstanceID = targetInstanceId };

    private static GameEvent DestroyEvent(string eventType, EffectContext ctx) =>
        new() { EventType = eventType, GameID = ctx.Game.GameID };

    [Trait("対象", "効果ダメージによる破壊")]
    public class EffectDamage
    {
        [Fact(DisplayName = "効果ダメージで可用性がちょうど0になったとき、リソースは破壊されトラッシュへ移動し、SLA ペナルティが1回だけ減算される")]
        public void DestroysAndAppliesSlaPenaltyOnce()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition,
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxAV: 300);
            state.Player2Field.Frontend[0] = target;
            long budgetBefore = state.Player2Budget;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player2Field, "tgt").Should().BeNull();
            state.Player2Trash.Should().Contain(c => c.InstanceID == "tgt");
            state.Player2Budget.Should().Be(budgetBefore - 400);
        }

        [Fact(DisplayName = "効果ダメージ適用後に可用性が1残ったとき、リソースは破壊されない")]
        public void DoesNotDestroy_WhenOneAvailabilityRemains()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition,
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(299)));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxAV: 300);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player2Field, "tgt").Should().NotBeNull();
        }

        [Fact(DisplayName = "効果ダメージでの破壊で、破壊されたリソース自身のon_destroy誘発効果が発火する")]
        public void FiresOwnOnDestroy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "Victim"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition,
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)));
            effects.Register("TST-0002", TriggerType.OnDestroy, ctx => new EffectResult
            {
                Events = [DestroyEvent("victim_on_destroy", ctx)]
            });

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0002", instanceId: "tgt", faceUp: true, maxAV: 300);

            var result = UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            result.Events.Should().Contain(e => e.EventType == "victim_on_destroy");
        }

        [Fact(DisplayName = "効果ダメージでの破壊で、味方の伏せた破壊時リアクティブが発火する")]
        public void FiresAllyReactive()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "Victim"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition,
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)));
            effects.Register("TST-0400", TriggerType.OnDestroy, ctx => new EffectResult
            {
                Events = [DestroyEvent("ally_reactive_on_destroy", ctx)]
            });

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0002", instanceId: "tgt", faceUp: true, maxAV: 300);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };

            var result = UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            result.Events.Should().Contain(e => e.EventType == "ally_reactive_on_destroy");
            state.Player2Field.Support[0].Should().BeNull("発動したリアクティブはトラッシュへ送られる");
        }

        [Fact(DisplayName = "効果ダメージでの破壊で、盤面変化を条件とする常時効果が再計算される")]
        public void FiresOnFieldChange()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "Victim"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", name: "Watcher"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition,
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)));
            effects.RegisterPassive("TST-0005", new PassiveEffectDef
            {
                Guards = [new ResourceCountGuard("opponent", null, null, null, null, ["TST-0002"], 1, null)],
                Applications =
                [
                    new PassiveBuffApplication
                    {
                        Selector = SourceSelector.Instance,
                        EffectType = EffectTypes.BuffTP,
                        Amount = new StaticAmount(200),
                        Mode = "",
                    },
                ],
            });

            var game = TestFactory.MakeGame();
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            var watcher = TestFactory.MakeResource(cardId: "TST-0005", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[1] = watcher;
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0002", instanceId: "tgt", faceUp: true, maxAV: 300);
            PassiveRecalculator.Recalculate(state, game, cc, effects);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "Victim が場にいる間はパッシブ効果で+200される");

            UseIgnitionProcessor.Process(state, game, 1, Use("src", "tgt"), cc, effects);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "Victim破壊で発動条件を失い基礎値に戻る");
        }
    }

    [Trait("対象", "攻撃時の自傷ダメージによる破壊")]
    public class SelfDamageOnAttack
    {
        [Fact(DisplayName = "攻撃時の自傷300で攻撃側の可用性が0以下になったとき、攻撃側も破壊される")]
        public void DestroysAttackerFromSelfDamage()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-ATK", tp: 300, av: 300, slaPenalty: 100));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-DEF", tp: 100, av: 5000, name: "Defender"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-ATK", TriggerType.OnAttack,
                new DealDamageOp(SourceSelector.Instance, new StaticAmount(300)));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-ATK", instanceId: "atk_1", faceUp: true, maxAV: 300, maxTP: 300);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-DEF", instanceId: "def_1", faceUp: true, maxAV: 5000, maxTP: 100);

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1,
                new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" }, cc, effects);

            FieldHelpers.FindResourceByID(state.Player1Field, "atk_1").Should().BeNull("自傷で可用性が0以下になり破壊される");
        }
    }

    [Trait("対象", "インシデントのダメージによる破壊")]
    public class IncidentDamageDestruction
    {
        [Fact(DisplayName = "インシデントのダメージで可用性が0以下になったとき、リソースは破壊される")]
        public void DestroysZeroedResource()
        {
            var cc = new TestCardCache();
            var incidentCard = new CardDefinition { CardId = "TST-INC", CardName = "TestIncident", CardType = CardTypes.Incident, DeployTurns = 0 };
            cc.Add(incidentCard);
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-INC", TriggerType.Ignition,
                new IncidentDamageOp(new AllOpponentSelector { Zone = Zones.Frontend }, new StaticAmount(1400)));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_inc", CardID = "TST-INC" });
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true);

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
                new PlayCardRequest { CardInstanceID = "h_inc", Zone = "", Index = 0 }, cc, effects);

            FieldHelpers.FindResourceByID(state.Player2Field, "tgt").Should().BeNull();
        }
    }

    [Trait("対象", "同時破壊の処理順")]
    public class SimultaneousDestructionOrder
    {
        [Fact(DisplayName = "両プレイヤーのリソースが同時に可用性0以下になったとき、ターンプレイヤーのカードから先に破壊処理される")]
        public void TurnPlayerFirst_WhenOwnEffectTriggers()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-P1", name: "P1Dead"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-P2", name: "P2Dead"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition, new DestroyCheckOp());
            effects.Register("TST-P1", TriggerType.OnDestroy, ctx => new EffectResult { Events = [DestroyEvent("p1_on_destroy", ctx)] });
            effects.Register("TST-P2", TriggerType.OnDestroy, ctx => new EffectResult { Events = [DestroyEvent("p2_on_destroy", ctx)] });

            // ターンプレイヤーを P2 にすることで、破壊順が「P1 固定」ではなく ActivePlayer に従うことを識別する。
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "checker", faceUp: true);
            state.Player2Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-P2", instanceId: "p2_dead", maxAV: 1000, damage: 1000, faceUp: true);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-P1", instanceId: "p1_dead", maxAV: 1000, damage: 1000, faceUp: true);

            var result = UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 2, Use("checker"), cc, effects);

            result.Events.Select(e => e.EventType).Should().ContainInOrder("p2_on_destroy", "p1_on_destroy");
        }

        [Fact(DisplayName = "非ターンプレイヤーが所有する効果が両者のリソースを破壊するときも、ターンプレイヤー側が先に処理される")]
        public void TurnPlayerFirst_WhenNonTurnPlayerOwnsEffect()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-P1", name: "P1Dead"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-P2", name: "P2Dead"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared, new DestroyCheckOp());
            effects.Register("TST-P1", TriggerType.OnDestroy, ctx => new EffectResult { Events = [DestroyEvent("p1_on_destroy", ctx)] });
            effects.Register("TST-P2", TriggerType.OnDestroy, ctx => new EffectResult { Events = [DestroyEvent("p2_on_destroy", ctx)] });

            // P1 (ターンプレイヤー) が攻撃を宣言し、P2 (非ターンプレイヤー) 所有のリアクティブが destroy_check を発動する。
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle, activePlayer: 1);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-P1", instanceId: "p1_dead", maxAV: 1000, damage: 1000, faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-P2", instanceId: "p2_dead", maxAV: 1000, damage: 1000, faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0400", FaceUp = false };

            var result = AttackProcessor.Process(state, TestFactory.MakeGame(), 1,
                new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" }, cc, effects);

            result.Events.Select(e => e.EventType).Should().ContainInOrder("p1_on_destroy", "p2_on_destroy");
        }

        [Fact(DisplayName = "破壊対象が1体だけのとき、その1体のみ処理される")]
        public void OnlyDestroysTheSingleZeroedResource()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-P1", name: "P1Dead"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition, new DestroyCheckOp());

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 1);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "checker", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-P1", instanceId: "p1_dead", maxAV: 1000, damage: 1000, faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "p2_alive", faceUp: true);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("checker"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player1Field, "p1_dead").Should().BeNull();
            FieldHelpers.FindResourceByID(state.Player2Field, "p2_alive").Should().NotBeNull();
        }
    }

    [Trait("対象", "アタッチメントの連れトラッシュ")]
    public class AttachmentCascade
    {
        [Fact(DisplayName = "アタッチメント1枚を装備したホストが破壊されたとき、アタッチメントもトラッシュへ移動し、そのアタッチメントのon_destroyが発火する")]
        public void SingleAttachment_TrashedAndFiresOwnOnDestroy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0301"));
            var effects = new EffectRegistry();
            effects.Register("TST-0301", TriggerType.OnDestroy, ctx => new EffectResult
            {
                Events = [DestroyEvent("attachment_on_destroy", ctx)]
            });

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk_1", faceUp: true, maxTP: 1500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "att_1",
                CardID = "TST-0301",
                TargetInstanceID = "def_1",
                FaceUp = true,
            };

            var result = AttackProcessor.Process(state, TestFactory.MakeGame(), 1,
                new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" }, cc, effects);

            state.Player2Trash.Should().Contain(c => c.InstanceID == "att_1");
            result.Events.Should().Contain(e => e.EventType == "attachment_on_destroy");
        }

        [Fact(DisplayName = "アタッチメント2枚を装備したホストが破壊されたとき、2枚ともトラッシュへ移動する")]
        public void TwoAttachments_BothTrashed()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0301"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0302", name: "SecondAttachment"));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk_1", faceUp: true, maxTP: 1500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "att_1",
                CardID = "TST-0301",
                TargetInstanceID = "def_1",
                FaceUp = true,
            };
            state.Player2Field.Support[1] = new DeployedSupport
            {
                InstanceID = "att_2",
                CardID = "TST-0302",
                TargetInstanceID = "def_1",
                FaceUp = true,
            };

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1,
                new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" }, cc, new EffectRegistry());

            state.Player2Trash.Select(c => c.InstanceID).Should().Contain(["att_1", "att_2"]);
        }

        [Fact(DisplayName = "アタッチメントを装備していないホストが破壊されたとき、サポートゾーンは変化しない")]
        public void NoAttachment_SupportZoneUnchanged()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk_1", faceUp: true, maxTP: 1500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            var unrelatedSupport = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-0200", FaceUp = true };
            state.Player2Field.Support[0] = unrelatedSupport;

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1,
                new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" }, cc, new EffectRegistry());

            state.Player2Field.Support[0].Should().Be(unrelatedSupport);
        }
    }

    [Trait("対象", "既に盤上にない対象への破壊の no-op 保証")]
    public class NoOpOnAbsentResource
    {
        [Fact(DisplayName = "既に盤上にないリソースを破壊しようとしたとき、バジェットもトラッシュも盤面も変化しない")]
        public void SecondDestroyCheckIsNoOp()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", slaPenalty: 400));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition, new DestroyCheckOp());

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "checker_1", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "checker_2", faceUp: true);
            state.Player1Field.Frontend[2] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "dead", maxAV: 1000, damage: 1000, faceUp: true);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("checker_1"), cc, effects);
            long budgetAfterFirst = state.Player1Budget;
            int trashCountAfterFirst = state.Player1Trash.Count;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("checker_2"), cc, effects);

            state.Player1Budget.Should().Be(budgetAfterFirst);
            state.Player1Trash.Should().HaveCount(trashCountAfterFirst);
            FieldHelpers.FindResourceByID(state.Player1Field, "dead").Should().BeNull();
        }
    }
}

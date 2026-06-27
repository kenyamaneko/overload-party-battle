using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 起動効果として登録した op / カスタム効果を use_ignition 経由で発動し、効果が state を変えることを検証する。
/// op を直接叩かず、プレイヤーのアクション (起動効果の使用) 越しに振る舞いを確認する。
/// </summary>
public class IgnitionEffectTests
{
    /// <summary>指定カードに op 列を起動効果として登録したキャッシュとレジストリを作る。</summary>
    /// <param name="card">起動効果を持たせるカード定義。</param>
    /// <param name="ops">起動効果として登録する op 列。</param>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, EffectRegistry Effects) Env(CardDefinition card, params IEffectOp[] ops)
    {
        var cc = new TestCardCache();
        cc.Add(card);
        var effects = new EffectRegistry();
        effects.RegisterComposed(card.CardId, TriggerType.Ignition, ops);
        return (cc, effects);
    }

    /// <summary>指定カスタム効果を 1 つの op として包む。</summary>
    /// <param name="name">カスタム効果名。</param>
    /// <returns>カスタム効果を実行する op。</returns>
    private static IEffectOp Custom(string name) =>
        new CustomFnOp(new CustomEffectRegistry().Build(name, null)!);

    /// <summary>起動効果を使用するリクエストを作る。</summary>
    /// <param name="instanceId">発動元インスタンス ID。</param>
    /// <param name="targetInstanceId">任意の対象インスタンス ID。</param>
    /// <param name="choiceData">任意の選択データ。</param>
    /// <returns>起動効果使用リクエスト。</returns>
    private static UseIgnitionRequest Use(
        string instanceId, string? targetInstanceId = null, Dictionary<string, object>? choiceData = null) =>
        new() { InstanceID = instanceId, TargetInstanceID = targetInstanceId, ChoiceData = choiceData };

    /// <summary>deal_damage を起動効果で発動すると対象がダメージを受けることを検証する。</summary>
    public class DealDamage
    {
        [Fact]
        public void Ignition_DealsDamageToTarget()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"),
                new DealDamageOp(TargetSelector.Instance, new StaticAmount(300)));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxAV: 1000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src", "tgt"), cc, effects);

            target.Damage.Should().Be(300);
        }
    }

    /// <summary>gain_insight を起動効果で発動すると自分の インサイトプール が増えることを検証する。</summary>
    public class GainInsight
    {
        [Fact]
        public void Ignition_AddsToOwnPool()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new GainInsightOp(new StaticAmount(300)));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.SetInsightPool(1, 100);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.GetInsightPool(1).Should().Be(400);
        }
    }

    /// <summary>absorb_insight を起動効果で発動すると相手の インサイト を保有量上限で吸収することを検証する。</summary>
    public class AbsorbInsight
    {
        [Theory]
        [InlineData(120, 120, 0)]
        [InlineData(500, 300, 200)]
        public void Ignition_TransfersClampedToOpponentPool(long oppPool, long expectedGained, long expectedOppLeft)
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new AbsorbInsightOp(new StaticAmount(300)));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.SetInsightPool(1, 0);
            state.SetInsightPool(2, oppPool);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.GetInsightPool(1).Should().Be(expectedGained);
            state.GetInsightPool(2).Should().Be(expectedOppLeft);
        }
    }

    /// <summary>destroy_check を起動効果で発動すると実効 可用性 0 以下のリソースが破壊されることを検証する。</summary>
    public class DestroyCheck
    {
        [Fact]
        public void Ignition_DestroysZeroedResource()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new DestroyCheckOp(PlayerRef.Myself));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "dead", maxAV: 1000, damage: 1000, faceUp: true);

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player1Field, "dead").Should().BeNull();
            FieldHelpers.FindResourceByID(state.Player1Field, "src").Should().NotBeNull();
        }
    }

    /// <summary>reveal_reactive を起動効果で発動すると相手の伏せ リアクティブ が表向きに開示されることを検証する。</summary>
    public class RevealReactive
    {
        [Fact]
        public void Ignition_RevealsHiddenReactive()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new RevealReactiveOp());
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.Player2Field.Support[0]!.FaceUp.Should().BeTrue();
        }
    }

    /// <summary>destroy_platform を起動効果で発動すると相手の プラットフォーム が破壊されることを検証する。</summary>
    public class DestroyPlatform
    {
        [Fact]
        public void Ignition_DestroysOpponentPlatform()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new DestroyPlatformOp());
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0200"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-0200", FaceUp = true };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            state.Player2Field.Support.Select(s => s.InstanceID).Should().NotContain("plat_1");
        }
    }

    /// <summary>scale_to_zero を起動効果で発動すると待機中の Elastic リソースに 維持コスト 軽減が付与されることを検証する。</summary>
    public class ScaleToZero
    {
        [Fact]
        public void Ignition_AddsMaintenanceReduction()
        {
            var (cc, effects) = Env(TestFactory.ElasticContainerCard(cardId: "TST-0003"), Custom(CustomEffects.ScaleToZero));
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main);
            var src = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true, elasticBonus: 600);
            src.DeployedOnTurn = 1;
            src.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = src;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            src.TemporaryEffects.Should().Contain(e => e.EffectType == BuffTypes.MaintenanceReduction && e.Value == 60);
        }
    }

    /// <summary>reattach を起動効果で発動するとアタッチメントが別の対象へ付け替わることを検証する。</summary>
    public class Reattach
    {
        [Fact]
        public void Ignition_MovesAttachmentToChosenTarget()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0301"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0301", TriggerType.Ignition, Custom(CustomEffects.Reattach));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "old_host", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "new_host", faceUp: true);
            var att = new DeployedSupport { InstanceID = "att", CardID = "TST-0301", TargetInstanceID = "old_host", FaceUp = true };
            state.Player1Field.Support[0] = att;

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("att", choiceData: new Dictionary<string, object> { ["instanceId"] = "new_host" }), cc, effects);

            att.TargetInstanceID.Should().Be("new_host");
        }
    }

    /// <summary>cloud_shift を起動効果で発動すると手札からデプロイ要求が出て発動元が自壊することを検証する。</summary>
    public class CloudShift
    {
        [Fact]
        public void Ignition_DeploysFromHandAndSelfDestructs()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0201"));
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                """{"faction":"SHE","deploy_discount":300}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0201", TriggerType.Ignition,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.CloudShift, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0201", FaceUp = true };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                Use("sup_1", choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }), cc, effects);

            state.GetBudget(1).Should().Be(5300);
            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Field.Support.Select(s => s.InstanceID).Should().NotContain("sup_1");
        }
    }

    /// <summary>peek_reactive を起動効果で発動すると相手の伏せ リアクティブ を表向きにせず覗き見ることを検証する。</summary>
    public class PeekReactive
    {
        [Fact]
        public void Ignition_PeeksHiddenReactiveWithoutFlipping()
        {
            var (cc, effects) = Env(TestFactory.ComputeCard(cardId: "TST-0001"), new PeekReactiveOp());
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0400", FaceUp = false };

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("src"), cc, effects);

            var sup = state.Player2Field.Support[0]!;
            sup.FaceUp.Should().BeFalse("覗き見はカードを表向きにしない");
            sup.PeekedBy.Should().Contain(1);
        }
    }
}

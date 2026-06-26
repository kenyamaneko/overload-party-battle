using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class CustomEffectTests
{
    /// <summary>カスタム効果が読む source/target/support/選択データを指定して op コンテキストを組み立てる。</summary>
    /// <param name="state">操作対象のゲーム状態。</param>
    /// <param name="cc">カードキャッシュ。</param>
    /// <param name="playerNum">効果オーナーのプレイヤー番号。</param>
    /// <param name="source">効果のソースリソース。</param>
    /// <param name="target">効果の対象リソース。</param>
    /// <param name="supSource">サポートゾーンのソース (アタッチメント / リアクティブ)。</param>
    /// <param name="choiceData">プレイヤーの選択データ。</param>
    /// <param name="eventOwnerNum">イベントを起こしたプレイヤー番号。</param>
    /// <returns>op コンテキスト。</returns>
    private static OpContext MakeOpContext(
        BattleGameState state, TestCardCache cc, long playerNum = 1,
        DeployedResource? source = null, DeployedResource? target = null,
        DeployedSupport? supSource = null,
        Dictionary<string, object>? choiceData = null,
        long? eventOwnerNum = null)
    {
        var ctx = new EffectContext
        {
            State = state,
            Game = TestFactory.MakeGame(),
            PlayerNum = playerNum,
            Source = source,
            Target = target,
            SupSource = supSource,
            ChoiceData = choiceData,
            EventOwnerNum = eventOwnerNum,
            CardCache = cc,
            Effects = new EffectRegistry(),
            Trigger = TriggerType.OnDeploy,
        };
        return new OpContext(ctx);
    }

    /// <summary>カスタム効果の meta ブロックを JSON から組み立てる。</summary>
    /// <param name="json">meta オブジェクトの JSON。</param>
    /// <returns>meta 辞書。</returns>
    private static Dictionary<string, JsonElement> Meta(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    /// <summary>しゅがーらぼ の Compute系リソース が並ぶとき対象に追加 ダメージ を与える効果。</summary>
    public class ChainAttackBonus
    {
        [Fact]
        public void AppliesBonusDamage_WhenSugarComputeAllyPresent()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0010", faction: Factions.Sugar));
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[0] = source;
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0010", instanceId: "ally", faceUp: true);
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxAV: 2000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            var effect = new CustomEffectRegistry().Build(CustomEffects.ChainAttackBonus, Meta("""{"damage":200}"""))!;
            effect(MakeOpContext(state, cc, source: source, target: target));

            target.Damage.Should().Be(200);
        }

        [Fact]
        public void NoBonus_WhenNoSugarComputeAlly()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[0] = source;
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxAV: 2000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            var effect = new CustomEffectRegistry().Build(CustomEffects.ChainAttackBonus, Meta("""{"damage":200}"""))!;
            effect(MakeOpContext(state, cc, source: source, target: target));

            target.Damage.Should().Be(0);
        }
    }

    /// <summary>高 スループット の Compute系リソース が稼働したとき 休止 を付与する効果。</summary>
    public class DisableHighTpDeploy
    {
        // スループット 閾値 900 の境界 (899 は不発 / 900 ちょうどで発動) を確認する。
        [Theory]
        [InlineData(899, false)]
        [InlineData(900, true)]
        [InlineData(1200, true)]
        public void AppliesDormant_OnlyAtOrAboveThreshold(long maxTP, bool expectDormant)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "tgt", faceUp: true, maxTP: maxTP);
            state.Player2Field.Frontend[0] = target;

            var effect = new CustomEffectRegistry().Build(CustomEffects.DisableHighTpDeploy, null)!;
            effect(MakeOpContext(state, cc, playerNum: 1, target: target));

            FieldHelpers.HasTemporaryEffect(target, BuffTypes.Dormant).Should().Be(expectDormant);
        }
    }

    /// <summary>相手の 3 体目のデプロイをキャンセルする効果。</summary>
    public class CancelNthDeploy
    {
        /// <summary>相手フィールドに当該ターンデプロイのリソースを指定数だけ並べる。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        /// <param name="count">並べるリソース数。</param>
        /// <param name="turn">デプロイされたターン。</param>
        private static void DeployN(BattleGameState state, int count, long turn)
        {
            // フロントエンド (3 枠) を埋めてからバックエンドへ溢れさせる。
            int feCount = Math.Min(count, BattleConstants.SlotsPerZone);
            for (int i = 0; i < feCount; i++)
            {
                var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: $"d_{i}", faceUp: true);
                res.DeployedOnTurn = turn;
                state.Player2Field.Frontend[i] = res;
            }
            for (int i = feCount; i < count; i++)
            {
                var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: $"d_{i}", faceUp: true);
                res.DeployedOnTurn = turn;
                state.Player2Field.Backend[i - feCount] = res;
            }
        }

        private static DeployedSupport Watcher() =>
            new() { InstanceID = "watcher", CardID = "TST-0001", FaceUp = false };

        [Fact]
        public void CancelsAndMarksUsed_OnThirdDeploy()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 4);
            DeployN(state, 3, 4);
            var watcher = Watcher();
            state.Player1Field.Support[0] = watcher;

            var opCtx = MakeOpContext(state, cc, playerNum: 1, supSource: watcher);
            new CustomEffectRegistry().Build(CustomEffects.CancelNthDeploy, null)!(opCtx);

            opCtx.Result.ShouldCancelAction.Should().BeTrue();
            watcher.EffectUsedThisTurn.Should().BeTrue();
        }

        // 3 体目以外 (2 体目 / 4 体目) では発動しない。
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void Throws_WhenNotThirdDeploy(int deployCount)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 4);
            DeployN(state, deployCount, 4);
            var watcher = Watcher();
            state.Player1Field.Support[0] = watcher;

            var act = () => new CustomEffectRegistry().Build(CustomEffects.CancelNthDeploy, null)!(
                MakeOpContext(state, cc, playerNum: 1, supSource: watcher));

            act.Should().Throw<GameRuleException>();
        }
    }

    /// <summary>アタッチメント を別の対象リソースへ付け替える効果。</summary>
    public class Reattach
    {
        [Fact]
        public void MovesAttachmentToNewTarget()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "old_host", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "new_host", faceUp: true);
            var att = new DeployedSupport { InstanceID = "att", CardID = "TST-0301", TargetInstanceID = "old_host", FaceUp = true };
            state.Player1Field.Support[0] = att;

            new CustomEffectRegistry().Build(CustomEffects.Reattach, null)!(
                MakeOpContext(state, cc, playerNum: 1, supSource: att,
                    choiceData: new Dictionary<string, object> { ["instanceId"] = "new_host" }));

            att.TargetInstanceID.Should().Be("new_host");
        }
    }

    /// <summary>攻撃も デプロイ もしていない Elastic リソース の 維持コスト を当ターン 0 にする効果。</summary>
    public class ScaleToZero
    {
        [Fact]
        public void AddsMaintenanceReduction_ForIdleElastic()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            var state = TestFactory.MakeGameState(turn: 5);
            var source = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true, elasticBonus: 600);
            source.DeployedOnTurn = 1;
            source.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = source;

            new CustomEffectRegistry().Build(CustomEffects.ScaleToZero, null)!(
                MakeOpContext(state, cc, playerNum: 1, source: source));

            source.TemporaryEffects.Should().Contain(e =>
                e.EffectType == BuffTypes.MaintenanceReduction && e.Value == 60);
        }

        [Fact]
        public void Throws_WhenUsedOnDeployTurn()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            var state = TestFactory.MakeGameState(turn: 5);
            var source = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true);
            source.DeployedOnTurn = 5;
            state.Player1Field.Frontend[0] = source;

            var act = () => new CustomEffectRegistry().Build(CustomEffects.ScaleToZero, null)!(
                MakeOpContext(state, cc, playerNum: 1, source: source));

            act.Should().Throw<GameRuleException>().WithMessage("*deploy turn*");
        }

        [Fact]
        public void Throws_WhenSourceAttackedLastTurn()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            var state = TestFactory.MakeGameState(turn: 5);
            var source = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true, elasticBonus: 600);
            source.DeployedOnTurn = 1;
            source.LastAttackTurn = 4;
            state.Player1Field.Frontend[0] = source;

            var act = () => new CustomEffectRegistry().Build(CustomEffects.ScaleToZero, null)!(
                MakeOpContext(state, cc, playerNum: 1, source: source));

            act.Should().Throw<GameRuleException>().WithMessage("*attacked*");
        }
    }

    /// <summary>デプロイから一定ターン後に自壊する効果。</summary>
    public class SpotExpiry
    {
        [Fact]
        public void DestroysSource_AtOrAfterExpiryTurns()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 3);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            source.DeployedOnTurn = 1; // 経過 2 ターン == turns で自壊する境界
            state.Player1Field.Frontend[0] = source;

            new CustomEffectRegistry().Build(CustomEffects.SpotExpiry, Meta("""{"turns":2}"""))!(
                MakeOpContext(state, cc, playerNum: 1, source: source));

            FieldHelpers.FindResourceByID(state.Player1Field, "src").Should().BeNull();
        }

        [Fact]
        public void Survives_BeforeExpiry()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 2);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            source.DeployedOnTurn = 1; // 経過 1 ターン < turns
            state.Player1Field.Frontend[0] = source;

            new CustomEffectRegistry().Build(CustomEffects.SpotExpiry, Meta("""{"turns":2}"""))!(
                MakeOpContext(state, cc, playerNum: 1, source: source));

            FieldHelpers.FindResourceByID(state.Player1Field, "src").Should().NotBeNull();
        }
    }

    /// <summary>target_shield マーカー (デプロイ時は no-op) と、それを読む保護判定。</summary>
    public class TargetShield
    {
        private static TestCardCache ShieldCache()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var shield = TestFactory.AttachmentCard(cardId: "TST-0301");
            shield.Effects = [new EffectDef { Trigger = TriggerTypes.Passive, Custom = CustomEffects.TargetShield }];
            cc.Add(shield);
            return cc;
        }

        [Fact]
        public void DeployEffect_IsNoOp()
        {
            var cc = new TestCardCache();
            var state = TestFactory.MakeGameState();
            var effect = new CustomEffectRegistry().Build(CustomEffects.TargetShield, null)!;

            var act = () => effect(MakeOpContext(state, cc));

            act.Should().NotThrow();
        }

        [Fact]
        public void IsTargetShielded_True_WithShieldAndAnotherFrontend()
        {
            var cc = ShieldCache();
            var field = TestFactory.MakeField();
            var protectedRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "prot", faceUp: true);
            field.Frontend[0] = protectedRes;
            field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "wall", faceUp: true);
            field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0301", TargetInstanceID = "prot", FaceUp = true };

            FieldHelpers.IsTargetShielded(protectedRes, field, cc).Should().BeTrue();
        }

        [Fact]
        public void IsTargetShielded_False_WithoutAnotherFrontend()
        {
            var cc = ShieldCache();
            var field = TestFactory.MakeField();
            var protectedRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "prot", faceUp: true);
            field.Frontend[0] = protectedRes;
            field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0301", TargetInstanceID = "prot", FaceUp = true };

            FieldHelpers.IsTargetShielded(protectedRes, field, cc).Should().BeFalse();
        }
    }

    /// <summary>破壊された リソース と同タイプのカードを 手札 からデプロイする効果。</summary>
    public class DeploySameTypeFromHand
    {
        [Fact]
        public void EnqueuesSlotSelect_WhenChoiceMatchesType()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", subtype: "VM"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "destroyed");
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];

            new CustomEffectRegistry().Build(CustomEffects.DeploySameTypeFromHand, null)!(
                MakeOpContext(state, cc, playerNum: 1, target: target,
                    choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }));

            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Hand.Should().BeEmpty();
        }

        [Fact]
        public void Throws_WhenChoiceTypeMismatch()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", subtype: "VM"));
            cc.Add(TestFactory.DataCard(cardId: "TST-0100", subtype: "Database"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "destroyed");
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0100" }];

            var act = () => new CustomEffectRegistry().Build(CustomEffects.DeploySameTypeFromHand, null)!(
                MakeOpContext(state, cc, playerNum: 1, target: target,
                    choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0100" }));

            act.Should().Throw<GameRuleException>().WithMessage("*same type*");
        }
    }

    /// <summary>手札 からフィルタ一致カードを割引付きでデプロイし、発動元を自壊させる効果。</summary>
    public class CloudShift
    {
        [Fact]
        public void DeploysFromHand_AppliesDiscount_AndSelfDestructs()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            var sup = new DeployedSupport { InstanceID = "sup_1", CardID = "TST-0201", FaceUp = true };
            state.Player1Field.Support[0] = sup;

            new CustomEffectRegistry().Build(CustomEffects.CloudShift, Meta("""{"faction":"SHE","deploy_discount":300}"""))!(
                MakeOpContext(state, cc, playerNum: 1, supSource: sup,
                    choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }));

            state.GetBudget(1).Should().Be(5300);
            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Field.Support.Select(s => s.InstanceID).Should().NotContain("sup_1");
        }
    }

    /// <summary>再ダメージ先が 相手 の フロントエンド であることを検証する効果。</summary>
    public class RedirectAttack
    {
        [Fact]
        public void Validates_WhenTargetIsOpponentFrontend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe", faceUp: true);

            var act = () => new CustomEffectRegistry().Build(CustomEffects.RedirectAttack, null)!(
                MakeOpContext(state, cc, playerNum: 1,
                    choiceData: new Dictionary<string, object> { ["instanceId"] = "fe" }));

            act.Should().NotThrow();
        }

        [Fact]
        public void Throws_WhenTargetNotOpponentFrontend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be", faceUp: true);

            var act = () => new CustomEffectRegistry().Build(CustomEffects.RedirectAttack, null)!(
                MakeOpContext(state, cc, playerNum: 1,
                    choiceData: new Dictionary<string, object> { ["instanceId"] = "be" }));

            act.Should().Throw<GameRuleException>().WithMessage("*frontend*");
        }
    }
}

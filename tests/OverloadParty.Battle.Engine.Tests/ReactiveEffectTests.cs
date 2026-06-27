using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 攻撃をトリガーとする効果 (リアクティブ / on_attack) を attack 経由で発動し、振る舞いを検証する。
/// op / カスタム効果を直接叩かず、攻撃というアクション越しに確認する。
/// </summary>
public class ReactiveEffectTests
{
    /// <summary>攻撃リクエストを作る。</summary>
    /// <param name="attacker">攻撃側インスタンス ID。</param>
    /// <param name="target">対象インスタンス ID。</param>
    /// <returns>攻撃リクエスト。</returns>
    private static AttackRequest Atk(string attacker, string target) =>
        new() { AttackerInstanceID = attacker, TargetInstanceID = target };

    /// <summary>相手サポートゾーンに伏せたリアクティブを置く。</summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="cardId">リアクティブのカード ID。</param>
    private static void PlaceReactive(BattleGameState state, string cardId) =>
        state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "react", CardID = cardId, FaceUp = false, DeployOrder = 1 };

    /// <summary>cancel_action リアクティブが攻撃宣言をキャンセルすることを検証する。</summary>
    public class CancelAction
    {
        [Fact]
        public void Reactive_CancelsAttack()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared, SetCancelActionOp.Instance);

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk", faceUp: true);
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", faceUp: true);
            state.Player2Field.Frontend[0] = defender;
            PlaceReactive(state, "TST-0400");

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            defender.Damage.Should().Be(0, "リアクティブで攻撃がキャンセルされダメージは入らない");
        }
    }

    /// <summary>survive_destruction リアクティブが破壊を免れさせ surviveAV で残すことを検証する。</summary>
    public class SurviveDestruction
    {
        [Fact]
        public void Reactive_LeavesDefenderAtSurviveAvailability()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, name: "StrongCompute"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared, new SurviveDestructionOp(200));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk", faceUp: true, maxTP: 1500, currentTP: 1500);
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", faceUp: true, maxAV: 1400);
            state.Player2Field.Frontend[0] = defender;
            PlaceReactive(state, "TST-0400");

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player2Field, "def").Should().NotBeNull("破壊を免れる");
            defender.EffectiveAV.Should().Be(200);
        }
    }

    /// <summary>redirect_attack リアクティブが再ダメージ先の選択待ちへ遷移することを検証する。</summary>
    public class RedirectAttack
    {
        [Fact]
        public void Reactive_SuspendsForRedirectTargetChoice()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.RedirectAttack, null)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            // リアクティブ所有者 (防御側 P2) から見た相手 = 攻撃側 P1 のフロントエンドが再ダメージ先候補になる
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_fe", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", faceUp: true);
            PlaceReactive(state, "TST-0400");

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            state.PendingEffectChoice.Should().NotBeNull("再ダメージ先の選択待ちへ遷移する");
            state.PendingEffectChoice!.ChoiceKind.Should().Be(ChoiceKinds.FieldTarget);
            state.PendingEffectChoice.Candidates.Should().Contain("atk");
        }
    }

    /// <summary>chain_attack_bonus が しゅがーらぼ Compute系リソース の並びで追加ダメージを与えることを検証する。</summary>
    public class ChainAttackBonus
    {
        [Fact]
        public void OnAttack_AddsBonusDamage_WhenSugarComputeAllyPresent()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0010", faction: Factions.Sugar));
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"damage":200}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnAttack,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.ChainAttackBonus, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0010", instanceId: "ally", faceUp: true);
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", faceUp: true, maxAV: 2000);
            state.Player2Field.Frontend[0] = defender;

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            defender.Damage.Should().Be(800, "攻撃 600 + チェイン追加 200");
        }

        [Fact]
        public void OnAttack_NoBonus_WhenNoSugarComputeAlly()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var meta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"damage":200}""");
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.OnAttack,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.ChainAttackBonus, meta)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk", faceUp: true);
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", faceUp: true, maxAV: 2000);
            state.Player2Field.Frontend[0] = defender;

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            defender.Damage.Should().Be(600, "しゅがーらぼ Compute系リソース の並びがなければ追加ダメージなし");
        }
    }
}

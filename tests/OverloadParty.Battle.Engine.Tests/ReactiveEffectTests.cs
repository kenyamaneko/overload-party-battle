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

    [Trait("対象", "cancel_action リアクティブ")]
    public class CancelAction
    {
        [Fact(DisplayName = "cancel_action リアクティブが攻撃宣言をキャンセルし対象にダメージが入らない")]
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

    [Trait("対象", "survive_destruction リアクティブ")]
    public class SurviveDestruction
    {
        [Fact(DisplayName = "survive_destruction リアクティブが対象の破壊を免れさせ実効可用性 200 で残す")]
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

    [Trait("対象", "redirect_attack リアクティブ")]
    public class RedirectAttack
    {
        /// <summary>redirect_attack を伏せた防御側 P2 と、攻撃側 P1 のフロントエンドを用意する。</summary>
        /// <param name="attackerFrontendCount">攻撃側フロントエンドに並べるリソースの数。</param>
        /// <returns>カードキャッシュ・効果レジストリ・ゲーム状態。</returns>
        private static (TestCardCache Cc, EffectRegistry Effects, BattleGameState State) Setup(int attackerFrontendCount)
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-0400"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.RedirectAttack, null)!));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            // リアクティブ所有者 (防御側 P2) から見た相手 = 攻撃側 P1 のフロントエンドが再ダメージ先候補になる
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "atk", faceUp: true, maxAV: 2000, currentAV: 2000);
            for (int i = 1; i < attackerFrontendCount; i++)
            {
                state.Player1Field.Frontend[i] = TestFactory.MakeResource(
                    cardId: "TST-0001", instanceId: $"my_fe_{i}", faceUp: true, maxAV: 2000, currentAV: 2000);
            }
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "def", faceUp: true, maxAV: 2000, currentAV: 2000);
            PlaceReactive(state, "TST-0400");

            return (cc, effects, state);
        }

        [Fact(DisplayName = "攻撃を宣言すると、攻撃側が再ダメージ先を選ぶ選択待ちへ遷移する")]
        public void Reactive_SuspendsForRedirectTargetChoice()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.ChoiceKind.Should().Be(ChoiceKinds.FieldTarget);
            state.PendingEffectChoice.ChooserPlayerNum.Should().Be(1);
            state.PendingEffectChoice.Candidates.Should().BeEquivalentTo(["atk", "my_fe_1"]);
        }

        [Fact(DisplayName = "再ダメージ先に攻撃リソース以外を選ぶと、そのリソースに攻撃リソースのスループット分 600 のダメージが入る")]
        public void Resolve_DealsAttackerThroughputToChosenFrontend()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);
            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "my_fe_1" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player1Field, "my_fe_1")!.Damage.Should().Be(600);
            state.PendingEffectChoice.Should().BeNull();
        }

        [Fact(DisplayName = "選択待ちの間は、元の攻撃対象にダメージが入らない")]
        public void Suspended_DoesNotDamageOriginalTargetYet()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            FieldHelpers.FindResourceByID(state.Player2Field, "def")!.Damage.Should().Be(0);
        }

        [Fact(DisplayName = "再ダメージ先を選んで解決すると攻撃は無効になり、元の攻撃対象のダメージは 0 のまま確定する")]
        public void Resolve_CancelsAttack_OriginalTargetTakesNoDamage()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);
            // NT-0024 と同じく、再ダメージ先の選択に続けて攻撃を無効にする
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.RedirectAttack, null)!),
                SetCancelActionOp.Instance);

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "my_fe_1" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player2Field, "def")!.Damage.Should().Be(0);
            FieldHelpers.FindResourceByID(state.Player1Field, "my_fe_1")!.Damage.Should().Be(600);
        }

        [Fact(DisplayName = "攻撃が無効に確定しても、攻撃したリソースの攻撃権は消費される")]
        public void Resolve_CancelledAttack_StillConsumesAttackRight()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);
            effects.RegisterComposed("TST-0400", TriggerType.OnAttackDeclared,
                new CustomFnOp(new CustomEffectRegistry().Build(CustomEffects.RedirectAttack, null)!),
                SetCancelActionOp.Instance);

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "my_fe_1" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player1Field, "atk")!.HasAttacked.Should().BeTrue();
        }

        [Fact(DisplayName = "攻撃側フロントエンドが攻撃リソース 1 枚だけのとき、その攻撃リソース自身に 600 のダメージが入る")]
        public void Resolve_SingleFrontend_DealsDamageToAttackerItself()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 1);
            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            state.PendingEffectChoice!.Candidates.Should().BeEquivalentTo(["atk"]);

            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "atk" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player1Field, "atk")!.Damage.Should().Be(600);
        }

        [Fact(DisplayName = "攻撃が無効にならない選択を解決すると、元の攻撃対象にもスループット分 600 のダメージが入る")]
        public void Resolve_AttackContinues_DamagesOriginalTarget()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);
            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "my_fe_1" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player2Field, "def")!.Damage.Should().Be(600);
        }

        [Fact(DisplayName = "再ダメージで攻撃したリソースが壊れたとき、攻撃は不発になり攻撃対象にダメージが入らない")]
        public void Resolve_AttackerDestroyedByRedirect_LeavesTargetUndamaged()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 1);
            // 移された 600 のダメージで攻撃したリソース自身が壊れる耐久にする
            FieldHelpers.FindResourceByID(state.Player1Field, "atk")!.MaxAV = 600;

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);
            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "atk" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player1Field, "atk").Should().BeNull("再ダメージで壊れる");
            FieldHelpers.FindResourceByID(state.Player2Field, "def")!.Damage.Should().Be(0);
        }

        /// <summary>保存・復元を経た選択待ちを模して、盤面とは別インスタンスのリソースに差し替える。</summary>
        /// <param name="resource">選択待ちに保存されているリソース。</param>
        /// <returns>同じ内容を持つ別インスタンス。</returns>
        private static DeployedResource ReloadedApart(DeployedResource resource) =>
            System.Text.Json.JsonSerializer.Deserialize<DeployedResource>(
                System.Text.Json.JsonSerializer.Serialize(resource))!;

        [Fact(DisplayName = "攻撃宣言と選択解決の間に状態を保存・復元しても、続行した攻撃のダメージが盤面の攻撃対象に入る")]
        public void Resolve_AfterStateReload_DamagesOriginalTargetOnField()
        {
            var (cc, effects, state) = Setup(attackerFrontendCount: 2);
            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk", "def"), cc, effects);

            var pending = state.PendingEffectChoice!;
            pending.Source = ReloadedApart(pending.Source!);
            pending.Target = ReloadedApart(pending.Target!);

            ResolvePendingChoiceProcessor.Process(state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "my_fe_1" }, cc, effects, new FakeClock());

            FieldHelpers.FindResourceByID(state.Player2Field, "def")!.Damage.Should().Be(600);
            FieldHelpers.FindResourceByID(state.Player1Field, "atk")!.HasAttacked.Should().BeTrue();
        }
    }

    [Trait("対象", "chain_attack_bonus 誘発効果")]
    public class ChainAttackBonus
    {
        [Fact(DisplayName = "しゅがーらぼの Compute系リソース が並んでいると攻撃ダメージに 200 加算され合計 800 になる")]
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

        [Fact(DisplayName = "しゅがーらぼの Compute系リソース が並んでいないと追加ダメージは乗らず 600 のままになる")]
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

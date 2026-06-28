using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// gain_budget / lose_budget の op を起動効果として登録し、起動効果の使用 (use_ignition) 越しに
/// バジェットが増減することを検証する。op を直接叩かず、プレイヤーのアクションを起点にする。
/// </summary>
public class BudgetEffectTests
{
    /// <summary>op 列を起動効果として登録したカードキャッシュとレジストリを作る。</summary>
    /// <param name="ops">起動効果として登録する op 列。</param>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, EffectRegistry Effects) Env(params IEffectOp[] ops)
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        var effects = new EffectRegistry();
        effects.RegisterComposed("TST-0001", TriggerType.Ignition, ops);
        return (cc, effects);
    }

    /// <summary>発動元リソースを 1 体置いた状態を作る。</summary>
    /// <param name="p1Budget">自分のバジェット初期値。</param>
    /// <param name="p2Budget">相手のバジェット初期値。</param>
    /// <returns>発動元を配置済みのゲーム状態。</returns>
    private static BattleGameState StateWithSource(long p1Budget = 5000, long p2Budget = 5000)
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: p1Budget, p2Budget: p2Budget);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
        return state;
    }

    [Fact]
    public void Ignition_GainBudget_AddsToOwnBudget()
    {
        var (cc, effects) = Env(new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)));
        var state = StateWithSource(p1Budget: 5000);

        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "src" }, cc, effects);

        state.GetBudget(1).Should().Be(5500);
    }

    [Fact]
    public void Ignition_LoseBudget_SubtractsFromOpponentBudget()
    {
        var (cc, effects) = Env(new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(800)));
        var state = StateWithSource(p2Budget: 3000);

        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "src" }, cc, effects);

        state.GetBudget(2).Should().Be(2200);
    }

    [Fact]
    public void Ignition_LoseBudget_AllowsNegativeOpponentBudget()
    {
        var (cc, effects) = Env(new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(800)));
        var state = StateWithSource(p2Budget: 300);

        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "src" }, cc, effects);

        state.GetBudget(2).Should().Be(-500);
    }

    [Fact]
    public void Ignition_MultipleGainBudget_AccumulatesOnOwnBudget()
    {
        var (cc, effects) = Env(
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(300)),
            new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));
        var state = StateWithSource(p1Budget: 5000);

        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "src" }, cc, effects);

        state.GetBudget(1).Should().Be(5500);
    }
}

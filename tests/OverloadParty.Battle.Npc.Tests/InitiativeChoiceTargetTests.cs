using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;

namespace OverloadParty.Battle.Tests.Npc;

[Trait("対象", "対象を選ぶ施策の NPC 使用")]
public class InitiativeChoiceTargetTests
{
    private const string ResourceCard = "TST-0300";
    private const string InitiativeId = "IN-TST01";
    private const long HealAmount = 300;

    private readonly TestCardCache _cc = new();
    private readonly Initiative _initiative = new()
    {
        InitiativeId = InitiativeId,
        ProductId = "PD-TST",
        Kind = InitiativeKinds.Routine,
        Name = "R",
        InsightCost = 0,
    };
    private readonly InitiativeCatalog _catalog;

    public InitiativeChoiceTargetTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: ResourceCard, resizable: false));
        _catalog = new InitiativeCatalog([_initiative]);
    }

    /// <summary>選択で対象を決める回復をルーチン施策の効果として登録する。</summary>
    /// <param name="zone">対象を限定するゾーン。</param>
    /// <returns>施策の効果を登録した効果レジストリ。</returns>
    private EffectRegistry MakeHealEffects(string zone)
    {
        var effects = new EffectRegistry();
        effects.RegisterComposed(
            _initiative.EffectSourceId, TriggerType.Ignition,
            new HealDamageOp(new ByChoiceSelector { Zone = zone }, new StaticAmount(HealAmount)));
        return effects;
    }

    /// <summary>損傷が最も大きい自分のリソースを回復対象に選ぶ NPC 設定を作る。</summary>
    /// <returns>ルーチン施策を使う NPC 設定。</returns>
    private static AiConfig MakeConfig() => new()
    {
        Model = "test",
        Faction = "SHE",
        TargetSelection = new TargetSelectionConfig
        {
            Heal = new TargetSpec
            {
                Selector = new SelectorDef { Owner = "myself" },
                OrderBy = "damage_desc",
            },
        },
        ScaleUp = new ScaleUpConfig { InstanceFamily = "M", OrderBy = "tp_desc" },
        Initiative = new Dictionary<string, InitiativePolicyConfig>
        {
            [InitiativeId] = new() { Priority = 50 },
        },
    };

    /// <summary>ルーチン施策を使えるメインフェーズの状態を作る。</summary>
    /// <returns>ルーチン施策をセット済みのゲーム状態。</returns>
    private static BattleGameState MakeState()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1RoutineId = InitiativeId;
        state.Player1SpecialUsedThisGame = true;
        return state;
    }

    /// <summary>本番同様に情報秘匿済みのビューを経由して NPC の行動を決める。</summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="effects">効果レジストリ。</param>
    /// <returns>NPC が決めたメインフェーズの行動列。</returns>
    private List<NpcAction> Decide(BattleGameState state, EffectRegistry effects)
    {
        var clientState = GameStateView.Build(state, TestFactory.MakeGame(), 1, _cc, effects, _catalog);
        return new NpcAi(MakeConfig(), _cc, effects, _catalog).DecideMainPhaseActions(clientState);
    }

    [Fact(DisplayName = "バックエンドに限定した回復の施策では、より損傷したフロントエンドではなくバックエンドのリソースを選ぶ")]
    public void ZoneLimitedHeal_SelectsResourceInThatZone()
    {
        var effects = MakeHealEffects(Zones.Backend);
        var state = MakeState();
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: ResourceCard, instanceId: "fe_1", damage: 900);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: ResourceCard, instanceId: "be_1", damage: 200);

        var actions = Decide(state, effects);

        var use = actions.Should().ContainSingle(a => a.ActionType == ActionTypes.UseInitiative).Subject;
        ((UseInitiativeRequest)use.Data).ChoiceData!["instanceId"].Should().Be("be_1");
    }

    [Fact(DisplayName = "バックエンドに限定した回復の施策で、バックエンドにリソースが無いとき施策を使わない")]
    public void ZoneLimitedHeal_NoResourceInZone_SkipsInitiative()
    {
        var effects = MakeHealEffects(Zones.Backend);
        var state = MakeState();
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: ResourceCard, instanceId: "fe_1", damage: 900);

        var actions = Decide(state, effects);

        actions.Should().NotContain(a => a.ActionType == ActionTypes.UseInitiative);
    }

    [Fact(DisplayName = "NPC が対象を選ぶ施策を使うと、選んだリソースの損傷が消える")]
    public void UsingInitiative_HealsSelectedResource()
    {
        var effects = MakeHealEffects(Zones.Backend);
        var state = MakeState();
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: ResourceCard, instanceId: "fe_1", damage: 900);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: ResourceCard, instanceId: "be_1", damage: 200);

        var actions = Decide(state, effects);
        var use = actions.Should().ContainSingle(a => a.ActionType == ActionTypes.UseInitiative).Subject;

        UseInitiativeProcessor.Process(
            state, TestFactory.MakeGame(), 1, (UseInitiativeRequest)use.Data, _cc, effects, _catalog);

        FieldHelpers.FindResourceByID(state.Player1Field, "be_1")!.Damage.Should().Be(0);
        FieldHelpers.FindResourceByID(state.Player1Field, "fe_1")!.Damage.Should().Be(900);
    }
}

using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

[Trait("対象", "利用可能アクションでの盤面の選択候補")]
public class ChoiceTargetActionsTests
{
    private const string ResourceCard = "TST-0300";
    private const string IgnitionCard = "TST-0301";
    private const string SupportCard = "TST-0302";
    private const string InitiativeId = "IN-TST01";
    private const long HealAmount = 300;

    private readonly TestCardCache _cc = new();

    public ChoiceTargetActionsTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: ResourceCard, resizable: false));
        _cc.Add(TestFactory.ComputeCard(cardId: IgnitionCard, resizable: false));
        _cc.Add(TestFactory.PlatformCard(cardId: SupportCard));
    }

    /// <summary>選択で対象を決める回復の op を作る。</summary>
    /// <param name="zone">対象を限定するゾーン。null なら限定しない。</param>
    /// <returns>選択で対象を決める回復の op。</returns>
    private static HealDamageOp ChoiceHeal(string? zone) =>
        new(new ByChoiceSelector { Zone = zone }, new StaticAmount(HealAmount));

    /// <summary>指定の効果をルーチン施策として登録し、その施策を使える状態を作る。</summary>
    /// <param name="op">施策の効果として登録する op。</param>
    /// <returns>ゲーム状態と効果レジストリと施策カタログ。</returns>
    private static (BattleGameState State, EffectRegistry Effects, InitiativeCatalog Catalog) InitiativeEnv(IEffectOp op)
    {
        var initiative = new Initiative
        {
            InitiativeId = InitiativeId,
            ProductId = "PD-TST",
            Kind = InitiativeKinds.Routine,
            Name = "R",
            InsightCost = 0,
        };
        var effects = new EffectRegistry();
        effects.RegisterComposed(initiative.EffectSourceId, TriggerType.Ignition, op);

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1RoutineId = initiative.InitiativeId;
        state.Player1SpecialUsedThisGame = true;

        return (state, effects, new InitiativeCatalog([initiative]));
    }

    /// <summary>手番プレイヤーの実行可能アクションを列挙する。</summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="effects">効果レジストリ。</param>
    /// <param name="catalog">施策カタログ。null なら施策を列挙しない。</param>
    /// <returns>実行可能アクション一覧。</returns>
    private List<AvailableAction> Enumerate(
        BattleGameState state, IEffectRegistry effects, IInitiativeCatalog? catalog = null) =>
        AvailableActions.GetAllAvailableActions(
            state, 1, state.Player1Field, state.Player2Field, [], 5000, 0, _cc, effects, catalog);

    [Fact(DisplayName = "対象を選ぶ施策で選べるリソースが 2 件あるとき、その 2 件が候補として列挙される")]
    public void Initiative_TwoValidTargets_ListsBoth()
    {
        var (state, effects, catalog) = InitiativeEnv(ChoiceHeal(zone: null));
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "fe_1");
        state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "be_1");

        var actions = Enumerate(state, effects, catalog);

        var initiative = actions.Should().ContainSingle(a => a.Type == ActionTypes.UseInitiative).Subject;
        initiative.EffectTargetType.Should().Be("Choice");
        initiative.ValidTargets.Should().BeEquivalentTo(["fe_1", "be_1"]);
    }

    [Fact(DisplayName = "バックエンドに限定した施策では、フロントエンドのリソースが候補に含まれない")]
    public void Initiative_ZoneLimited_ExcludesOtherZone()
    {
        var (state, effects, catalog) = InitiativeEnv(ChoiceHeal(Zones.Backend));
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "fe_1");
        state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "be_1");

        var actions = Enumerate(state, effects, catalog);

        var initiative = actions.Should().ContainSingle(a => a.Type == ActionTypes.UseInitiative).Subject;
        initiative.ValidTargets.Should().Equal("be_1");
    }

    [Fact(DisplayName = "対象を選ぶ施策で選べるリソースが 1 件も無いとき、その施策のアクションが列挙されない")]
    public void Initiative_NoValidTarget_OmitsAction()
    {
        var (state, effects, catalog) = InitiativeEnv(ChoiceHeal(Zones.Backend));
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "fe_1");

        var actions = Enumerate(state, effects, catalog);

        actions.Should().NotContain(a => a.Type == ActionTypes.UseInitiative);
    }

    [Fact(DisplayName = "対象を選ばない施策では候補が付かない")]
    public void Initiative_EffectWithoutChoice_HasNoTargets()
    {
        var (state, effects, catalog) = InitiativeEnv(
            new HealDamageOp(new AllOwnSelector(), new StaticAmount(HealAmount)));
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "fe_1");

        var actions = Enumerate(state, effects, catalog);

        var initiative = actions.Should().ContainSingle(a => a.Type == ActionTypes.UseInitiative).Subject;
        initiative.EffectTargetType.Should().BeNull();
        initiative.ValidTargets.Should().BeNull();
    }

    [Fact(DisplayName = "バックエンドに限定した起動効果を持つリソースでは、バックエンドのリソースだけが候補になる")]
    public void Ignition_ZoneLimited_ListsOnlyThatZone()
    {
        var effects = new EffectRegistry();
        effects.RegisterComposed(IgnitionCard, TriggerType.Ignition, ChoiceHeal(Zones.Backend));

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: IgnitionCard, instanceId: "src_1");
        state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "be_1");

        var actions = Enumerate(state, effects);

        var ignition = actions.Should().ContainSingle(a => a.Type == ActionTypes.UseIgnition).Subject;
        ignition.EffectTargetType.Should().Be("Choice");
        ignition.ValidTargets.Should().Equal("be_1");
    }

    [Fact(DisplayName = "相手のフロントエンドに限定した起動効果では、相手のフロントエンドのリソースだけが候補になる")]
    public void Ignition_TargetingOpponent_ListsOnlyOpponentResourcesInZone()
    {
        var effects = new EffectRegistry();
        effects.RegisterComposed(
            IgnitionCard, TriggerType.Ignition,
            new DealDamageOp(
                new ByChoiceSelector { Owner = "opponent", Zone = Zones.Frontend },
                new StaticAmount(HealAmount)));

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: IgnitionCard, instanceId: "src_1");
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "opp_fe");
        state.Player2Field.Backend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "opp_be");

        var actions = Enumerate(state, effects);

        var ignition = actions.Should().ContainSingle(a => a.Type == ActionTypes.UseIgnition).Subject;
        ignition.ValidTargets.Should().Equal("opp_fe");
    }

    [Fact(DisplayName = "サポートゾーンのカードの起動効果でも、選べるリソースが 1 件も無いとき提示されない")]
    public void Ignition_SupportSource_NoValidTarget_OmitsAction()
    {
        var effects = new EffectRegistry();
        effects.RegisterComposed(SupportCard, TriggerType.Ignition, ChoiceHeal(Zones.Backend));

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = SupportCard,
            FaceUp = true,
        };
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: ResourceCard, instanceId: "fe_1");

        var actions = Enumerate(state, effects);

        actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
    }
}

using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// trash_to_hand の op を起動効果として登録し、起動効果の使用 (use_ignition) 越しに
/// トラッシュのカードが手札へ戻ることを検証する。op を直接叩かず、プレイヤーのアクションを起点にする。
/// </summary>
[Trait("対象", "トラッシュから手札へ戻す起動効果")]
public class TrashToHandOpTests
{
    private const string SourceCard = "TST-0009";

    /// <summary>trash_to_hand の op を起動効果として登録した環境を作る。</summary>
    /// <param name="op">登録する trash_to_hand の op。</param>
    /// <returns>カードキャッシュと効果レジストリ。</returns>
    private static (TestCardCache Cc, EffectRegistry Effects) Env(TrashToHandOp op)
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: SourceCard));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", faction: Factions.Tenki));
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", faction: Factions.Sugar));
        var effects = new EffectRegistry();
        effects.RegisterComposed(SourceCard, TriggerType.Ignition, op);
        return (cc, effects);
    }

    /// <summary>発動元リソースとトラッシュを設定した状態を作る。</summary>
    /// <param name="trash">トラッシュに置くカード列。</param>
    /// <returns>発動元を配置しトラッシュを設定したゲーム状態。</returns>
    private static BattleGameState StateWithTrash(params UndeployedCard[] trash)
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: SourceCard, instanceId: "src", faceUp: true);
        state.Player1Trash = [.. trash];
        state.Player1Hand = [];
        return state;
    }

    /// <summary>起動効果を使用するリクエストを作る。</summary>
    /// <param name="instanceId">手札へ戻すトラッシュのカードのインスタンス ID。任意。</param>
    /// <returns>起動効果使用リクエスト。</returns>
    private static UseIgnitionRequest Use(string? instanceId = null) =>
        new()
        {
            InstanceID = "src",
            ChoiceData = instanceId is null ? null : new Dictionary<string, object> { ["instanceId"] = instanceId },
        };

    [Fact(DisplayName = "カードを選ばずに起動すると例外になる")]
    public void Ignition_NoChoiceData_Throws()
    {
        var (cc, effects) = Env(new TrashToHandOp());
        var state = StateWithTrash(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" });

        var act = () => UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use(), cc, effects);

        act.Should().Throw<GameRuleException>().WithMessage("*No card chosen*");
    }

    [Fact(DisplayName = "トラッシュのカードを選んで起動するとそのカードがトラッシュから手札へ移る")]
    public void Ignition_ValidChoice_MovesCardFromTrashToHand()
    {
        var (cc, effects) = Env(new TrashToHandOp());
        var state = StateWithTrash(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" });

        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("t_1"), cc, effects);

        state.Player1Trash.Should().BeEmpty();
        state.Player1Hand.Should().ContainSingle().Which.CardID.Should().Be("TST-0001");
    }

    [Fact(DisplayName = "フィルタを満たすカードを選ぶと該当カードだけがトラッシュから手札へ移る")]
    public void Ignition_ChoicePassingFilter_MovesOnlyMatchingCard()
    {
        var (cc, effects) = Env(new TrashToHandOp { Filter = c => c.Faction == Factions.Tenki });
        var state = StateWithTrash(
            new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" },
            new UndeployedCard { InstanceID = "t_2", CardID = "TST-0002" });

        UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("t_1"), cc, effects);

        state.Player1Trash.Should().ContainSingle(c => c.InstanceID == "t_2");
        state.Player1Hand.Should().ContainSingle().Which.CardID.Should().Be("TST-0001");
    }

    [Fact(DisplayName = "フィルタを満たさないカードを選ぶと例外になりトラッシュは変わらない")]
    public void Ignition_ChoiceFailingFilter_Throws()
    {
        var (cc, effects) = Env(new TrashToHandOp { Filter = c => c.Faction == Factions.Tenki });
        var state = StateWithTrash(
            new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" },
            new UndeployedCard { InstanceID = "t_2", CardID = "TST-0002" });

        var act = () => UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("t_2"), cc, effects);

        act.Should().Throw<GameRuleException>().WithMessage("*does not match*");
        state.Player1Trash.Should().HaveCount(2);
    }

    [Fact(DisplayName = "トラッシュにないカードを選ぶと例外になる")]
    public void Ignition_ChoiceInstanceNotInTrash_Throws()
    {
        var (cc, effects) = Env(new TrashToHandOp());
        var state = StateWithTrash(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" });

        var act = () => UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, Use("nope"), cc, effects);

        act.Should().Throw<GameRuleException>().WithMessage("*not in trash*");
    }
}

[Trait("対象", "利用可能アクションでのトラッシュ回収候補")]
public class TrashToHandAvailableActionsTests
{
    private readonly TestCardCache _cc = new();

    public TrashToHandAvailableActionsTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "RES-TEN", faction: "Tenki"));
        _cc.Add(TestFactory.ComputeCard(cardId: "RES-SUG", faction: "Sugar"));

        // The card that triggers trash_to_hand — a Strategy (immediate) card.
        _cc.Add(new CardDefinition
        {
            CardId = "TRASH-RET",
            CardName = "TrashReturn",
            CardType = CardTypes.Strategy,
            Faction = "Tenki",
            DeployTurns = 0,
        });
    }

    private static EffectRegistry MakeRegistryWithTrashOp(string cardId, Func<CardDefinition, bool>? filter)
    {
        var registry = new EffectRegistry();
        var op = new TrashToHandOp { Filter = filter };
        registry.RegisterComposed(cardId, TriggerType.Ignition, op);
        return registry;
    }

    [Fact(DisplayName = "フィルタが Tenki のときトラッシュの Tenki カード t_1 と t_3 だけが候補になる")]
    public void PlayCard_TrashToHandWithFilter_EmitsValidTargets()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash =
        [
            new UndeployedCard { InstanceID = "t_1", CardID = "RES-TEN" },
            new UndeployedCard { InstanceID = "t_2", CardID = "RES-SUG" },
            new UndeployedCard { InstanceID = "t_3", CardID = "RES-TEN" },
        ];

        var registry = MakeRegistryWithTrashOp("TRASH-RET", c => c.Faction == "Tenki");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TRASH-RET" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        var playAction = actions.Should().ContainSingle(a => a.Type == ActionTypes.PlayCard).Subject;
        playAction.EffectTargetType.Should().Be("Choice");
        playAction.ValidTargets.Should().BeEquivalentTo(["t_1", "t_3"]);
    }

    [Fact(DisplayName = "フィルタに一致するカードがトラッシュにないときカードプレイアクションが提示されない")]
    public void PlayCard_TrashToHandNoMatchingCard_OmitsAction()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [new UndeployedCard { InstanceID = "t_1", CardID = "RES-SUG" }];

        var registry = MakeRegistryWithTrashOp("TRASH-RET", c => c.Faction == "Tenki");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TRASH-RET" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard);
    }

    [Fact(DisplayName = "トラッシュが空のときカードプレイアクションが提示されない")]
    public void PlayCard_TrashToHandEmptyTrash_OmitsAction()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [];

        var registry = MakeRegistryWithTrashOp("TRASH-RET", c => c.Faction == "Tenki");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TRASH-RET" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard);
    }

    [Fact(DisplayName = "フィルタがないときトラッシュの全カード t_1 と t_2 が候補になる")]
    public void PlayCard_TrashToHandNoFilter_EmitsAllTrash()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash =
        [
            new UndeployedCard { InstanceID = "t_1", CardID = "RES-TEN" },
            new UndeployedCard { InstanceID = "t_2", CardID = "RES-SUG" },
        ];

        var registry = MakeRegistryWithTrashOp("TRASH-RET", filter: null);

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TRASH-RET" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        var playAction = actions.Should().ContainSingle(a => a.Type == ActionTypes.PlayCard).Subject;
        playAction.EffectTargetType.Should().Be("Choice");
        playAction.ValidTargets.Should().BeEquivalentTo(["t_1", "t_2"]);
    }

    [Fact(DisplayName = "トラッシュ回収の起動効果を持つ表向きリソースは、トラッシュに候補があるとき候補一覧付きで起動効果アクションが提示される")]
    public void UseIgnition_ResourceWithTrashOp_CandidatesPresent_ShowsValidTargets()
    {
        const string SourceCard = "RES-IGN";
        _cc.Add(TestFactory.ComputeCard(cardId: SourceCard));
        var registry = MakeRegistryWithTrashOp(SourceCard, c => c.Faction == "Tenki");

        var state = TestFactory.MakeGameState();
        state.Player1Trash =
        [
            new UndeployedCard { InstanceID = "t_1", CardID = "RES-TEN" },
            new UndeployedCard { InstanceID = "t_2", CardID = "RES-SUG" },
        ];
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: SourceCard, instanceId: "src_1", faceUp: true);

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, field, TestFactory.MakeField(), [], 5000, 0, _cc, registry);

        var ignition = actions.Should().ContainSingle(a => a.Type == ActionTypes.UseIgnition).Subject;
        ignition.ValidTargets.Should().Equal("t_1");
    }

    [Fact(DisplayName = "トラッシュに候補が無いとき、その起動効果アクションは提示されない")]
    public void UseIgnition_ResourceWithTrashOp_NoCandidates_OmitsAction()
    {
        const string SourceCard = "RES-IGN";
        _cc.Add(TestFactory.ComputeCard(cardId: SourceCard));
        var registry = MakeRegistryWithTrashOp(SourceCard, filter: null);

        var state = TestFactory.MakeGameState();
        state.Player1Trash = [];
        var field = TestFactory.MakeField();
        field.Frontend[0] = TestFactory.MakeResource(cardId: SourceCard, instanceId: "src_1", faceUp: true);

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, field, TestFactory.MakeField(), [], 5000, 0, _cc, registry);

        actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
    }

    [Fact(DisplayName = "サポートゾーンのカードの起動効果でも、候補が無いと提示されない")]
    public void UseIgnition_SupportWithTrashOp_NoCandidates_OmitsAction()
    {
        const string SourceCard = "SUP-IGN";
        _cc.Add(TestFactory.PlatformCard(cardId: SourceCard));
        var registry = MakeRegistryWithTrashOp(SourceCard, filter: null);

        var state = TestFactory.MakeGameState();
        state.Player1Trash = [];
        var field = TestFactory.MakeField();
        field.Support[0] = new DeployedSupport { InstanceID = "sup_1", CardID = SourceCard, FaceUp = true };

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, field, TestFactory.MakeField(), [], 5000, 0, _cc, registry);

        actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
    }

    [Fact(DisplayName = "トラッシュ回収を含む施策は、候補が無いとき施策アクションが提示されない")]
    public void UseInitiative_TrashOpNoCandidates_OmitsAction()
    {
        var initiative = new Initiative
        {
            InitiativeId = "IN-TRASH",
            ProductId = "PD-TST",
            Kind = InitiativeKinds.Routine,
            Name = "R",
            InsightCost = 100,
        };
        var registry = MakeRegistryWithTrashOp(initiative.EffectSourceId, filter: null);
        var catalog = new InitiativeCatalog([initiative]);

        var state = TestFactory.MakeGameState(turn: 2);
        state.Player1RoutineId = initiative.InitiativeId;
        state.Player1SpecialUsedThisGame = true;
        state.Player1Trash = [];

        var actions = AvailableActions.GetAllAvailableActions(
            state, 1, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, 500, _cc, registry, catalog);

        actions.Should().NotContain(a => a.Type == ActionTypes.UseInitiative);
    }
}

using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class TrashToHandOpTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public TrashToHandOpTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", faction: "Tenki"));
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0002", faction: "Sugar"));
        _cc.Add(TestFactory.DataCard(cardId: "TST-DB01", faction: "Tenki"));
    }

    private EffectContext MakeContext(BattleGameState state, Dictionary<string, object>? choiceData = null)
    {
        return new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            ChoiceData = choiceData,
            Effects = new EffectRegistry(),
        };
    }

    [Fact]
    public void NoChoiceData_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" }];

        var op = new TrashToHandOp();
        var octx = new OpContext(MakeContext(state));

        var act = () => op.Execute(octx);
        act.Should().Throw<GameRuleException>().WithMessage("*No card chosen*");
    }

    [Fact]
    public void ValidChoice_NoFilter_MovesCardToHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" }];
        state.Player1Hand = [];

        var op = new TrashToHandOp();
        var octx = new OpContext(MakeContext(state, new Dictionary<string, object> { ["instanceId"] = "t_1" }));
        op.Execute(octx);

        state.Player1Trash.Should().BeEmpty();
        state.Player1Hand.Should().ContainSingle()
            .Which.CardID.Should().Be("TST-0001");
    }

    [Fact]
    public void ValidChoice_PassingFilter_MovesCardToHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash =
        [
            new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" },
            new UndeployedCard { InstanceID = "t_2", CardID = "TST-0002" },
        ];
        state.Player1Hand = [];

        var op = new TrashToHandOp { Filter = c => c.Faction == "Tenki" };
        var octx = new OpContext(MakeContext(state, new Dictionary<string, object> { ["instanceId"] = "t_1" }));
        op.Execute(octx);

        state.Player1Trash.Should().ContainSingle(c => c.InstanceID == "t_2");
        state.Player1Hand.Should().ContainSingle()
            .Which.CardID.Should().Be("TST-0001");
    }

    [Fact]
    public void ChoiceThatFailsFilter_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash =
        [
            new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" },
            new UndeployedCard { InstanceID = "t_2", CardID = "TST-0002" },
        ];

        var op = new TrashToHandOp { Filter = c => c.Faction == "Tenki" };
        var octx = new OpContext(MakeContext(state, new Dictionary<string, object> { ["instanceId"] = "t_2" }));

        var act = () => op.Execute(octx);
        act.Should().Throw<GameRuleException>().WithMessage("*does not match*");
    }

    [Fact]
    public void ChoiceInstanceNotInTrash_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" }];

        var op = new TrashToHandOp();
        var octx = new OpContext(MakeContext(state, new Dictionary<string, object> { ["instanceId"] = "nope" }));

        var act = () => op.Execute(octx);
        act.Should().Throw<GameRuleException>().WithMessage("*not in trash*");
    }
}

/// <summary>
/// Tests the Choice/ValidTargets emission for trash_to_hand effects in AvailableActions.
/// </summary>
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

    [Fact]
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
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        var playAction = actions.Should().ContainSingle(a => a.Type == ActionTypes.PlayCard).Subject;
        playAction.EffectTargetType.Should().Be("Choice");
        playAction.ValidTargets.Should().BeEquivalentTo(["t_1", "t_3"]);
    }

    [Fact]
    public void PlayCard_TrashToHandNoMatchingCard_OmitsAction()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [new UndeployedCard { InstanceID = "t_1", CardID = "RES-SUG" }];

        var registry = MakeRegistryWithTrashOp("TRASH-RET", c => c.Faction == "Tenki");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TRASH-RET" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard);
    }

    [Fact]
    public void PlayCard_TrashToHandEmptyTrash_OmitsAction()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Trash = [];

        var registry = MakeRegistryWithTrashOp("TRASH-RET", c => c.Faction == "Tenki");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TRASH-RET" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard);
    }

    [Fact]
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
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 100, _cc, registry);

        var playAction = actions.Should().ContainSingle(a => a.Type == ActionTypes.PlayCard).Subject;
        playAction.EffectTargetType.Should().Be("Choice");
        playAction.ValidTargets.Should().BeEquivalentTo(["t_1", "t_2"]);
    }
}

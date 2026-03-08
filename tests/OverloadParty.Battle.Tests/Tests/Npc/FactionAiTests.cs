using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Tests.Npc;

public class FactionAiTests
{
    private readonly TestCardCache _cc = new();
    private readonly NullEffectRegistry _effects = new();
    private readonly Game _game = TestFactory.MakeGame();

    public FactionAiTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardNo: 1, tp: 600, av: 1400, mc: 150, faction: "SD"));
        _cc.Add(TestFactory.DataCard(cardNo: 100, cardType: CardTypes.Database, yield: 400, av: 800, mc: 100));
        // Tenki-specific cards
        _cc.Add(TestFactory.DataCard(cardNo: 27, cardType: CardTypes.Database, yield: 300, av: 600, mc: 80,
            faction: "Tenki", name: "Opener"));
        _cc.Add(TestFactory.DataCard(cardNo: 29, cardType: CardTypes.Database, yield: 200, av: 500, mc: 60,
            faction: "Tenki", name: "TenkiDB29"));
        _cc.Add(TestFactory.DataCard(cardNo: 30, cardType: CardTypes.Database, yield: 250, av: 550, mc: 70,
            faction: "Tenki", name: "TenkiDB30"));
        _cc.Add(TestFactory.DataCard(cardNo: 31, cardType: CardTypes.Database, yield: 200, av: 500, mc: 60,
            faction: "Tenki", name: "TenkiDB31"));
        _cc.Add(TestFactory.DataCard(cardNo: 32, cardType: CardTypes.Database, yield: 350, av: 700, mc: 90,
            faction: "Tenki", name: "Cosmo"));
        _cc.Add(TestFactory.DataCard(cardNo: 33, cardType: CardTypes.Database, yield: 200, av: 500, mc: 60,
            faction: "Tenki", name: "TenkiDB33"));
    }

    // ─── Factory / GetFactionAi ─────────────────────────────────

    [Theory]
    [InlineData("SD")]
    [InlineData("Tenki")]
    [InlineData("Sugar")]
    [InlineData("Tuners")]
    public void GetFactionAi_KnownFaction_ReturnsFactionAi(string faction)
    {
        var ai = FactionAi.GetFactionAi(faction, _cc, _effects);

        ai.Should().BeOfType<FactionAi>();
        ((FactionAi)ai).Faction.Should().Be(faction);
    }

    [Fact]
    public void GetFactionAi_UnknownFaction_ReturnsStandardAi()
    {
        var ai = FactionAi.GetFactionAi("Unknown", _cc, _effects);

        ai.Should().BeOfType<StandardAi>();
    }

    [Theory]
    [InlineData("SD", "M")]
    [InlineData("Tenki", "R")]
    [InlineData("Sugar", "C")]
    [InlineData("Tuners", "M")]
    public void Create_SetsCorrectInstanceFamily(string faction, string expectedFamily)
    {
        var ai = FactionAi.Create(faction, _cc, _effects);

        ai.Faction.Should().Be(faction);

        // Verify via ScaleUp action to expose instance family
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.ScaleUp,
                SourceInstanceID = "res_1",
                TargetRank = "medium",
                NeedsFamily = true,
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);
        var scaleAction = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.ScaleUp);

        scaleAction.Should().NotBeNull();
        scaleAction!.Data["instanceFamily"].Should().Be(expectedFamily);
    }

    // ─── DecideMainPhaseActions ─────────────────────────────────

    [Fact]
    public void DecideMainPhaseActions_AlwaysEndsWithEndPhase()
    {
        var ai = FactionAi.Create("SD", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);

        var actions = ai.DecideMainPhaseActions(state, _game, 2, []);

        actions.Should().NotBeEmpty();
        actions.Last().ActionType.Should().Be(WireActionTypes.EndPhase);
    }

    [Fact]
    public void DecideMainPhaseActions_Tenki_DeploysCardsWithTenkiPriority()
    {
        var ai = FactionAi.Create("Tenki", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2Hand.Add(new HandCard { InstanceID = "h_27", CardID = 27 });
        state.Player2Hand.Add(new HandCard { InstanceID = "h_32", CardID = 32 });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_27",
                CardID = 27,
                ValidZones = ["backend_0"],
            },
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_32",
                CardID = 32,
                ValidZones = ["backend_1"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var playActions = actions.Where(a => a.ActionType == WireActionTypes.PlayCard).ToList();
        playActions.Should().HaveCount(2);

        // Card 32 (Cosmo) should be deployed first due to highest aozora priority (100)
        playActions[0].Data["cardInstanceId"].Should().Be("h_32");
        playActions[1].Data["cardInstanceId"].Should().Be("h_27");
    }

    [Fact]
    public void DecideMainPhaseActions_Tenki_Card27PriorityBoostWhenCard32OnField()
    {
        var ai = FactionAi.Create("Tenki", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);

        // Card 32 already on field
        state.Player2Field.Backend[0] = TestFactory.MakeResource(
            cardId: 32, instanceId: "field_32", maxAV: 700, currentYield: 350, maxYield: 350, currentTP: null);

        state.Player2Hand.Add(new HandCard { InstanceID = "h_27", CardID = 27 });
        state.Player2Hand.Add(new HandCard { InstanceID = "h_29", CardID = 29 });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_27",
                CardID = 27,
                ValidZones = ["backend_1"],
            },
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_29",
                CardID = 29,
                ValidZones = ["backend_2"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var playActions = actions.Where(a => a.ActionType == WireActionTypes.PlayCard).ToList();
        playActions.Should().HaveCount(2);

        // Card 27 gets priority 90 when card 32 is on field, vs card 29 at 65
        playActions[0].Data["cardInstanceId"].Should().Be("h_27");
    }

    [Fact]
    public void DecideMainPhaseActions_NonTenki_UsesStandardDeployOrder()
    {
        var ai = FactionAi.Create("SD", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);

        state.Player2Hand.Add(new HandCard { InstanceID = "h_data", CardID = 100 });
        state.Player2Hand.Add(new HandCard { InstanceID = "h_compute", CardID = 1 });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_data",
                CardID = 100,
                ValidZones = ["backend_0"],
            },
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_compute",
                CardID = 1,
                ValidZones = ["frontend_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var playActions = actions.Where(a => a.ActionType == WireActionTypes.PlayCard).ToList();
        playActions.Should().HaveCount(2);

        // Standard deploy: Compute (pri=0) before Data (pri=1)
        playActions[0].Data["cardInstanceId"].Should().Be("h_compute");
        playActions[1].Data["cardInstanceId"].Should().Be("h_data");
    }

    // ─── Payload key format verification ────────────────────────

    [Fact]
    public void DecideMainPhaseActions_PlayCardPayload_UsesCamelCaseKeys()
    {
        var ai = FactionAi.Create("Tenki", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2Hand.Add(new HandCard { InstanceID = "h_32", CardID = 32 });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_32",
                CardID = 32,
                ValidZones = ["backend_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var playAction = actions.First(a => a.ActionType == WireActionTypes.PlayCard);
        playAction.Data.Keys.Should().Contain("cardInstanceId");
        playAction.Data.Keys.Should().Contain("position");
        playAction.Data["position"].Should().BeOfType<SlotPosition>();
    }

    [Fact]
    public void DecideMainPhaseActions_Monetize_IncludesDistributionsKey()
    {
        var ai = FactionAi.Create("SD", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);
        state.Player2InsightPool = 300;

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.Monetize,
                SourceInstanceID = "be_1",
                RemainingCapacity = 200,
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var monetize = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.Monetize);
        monetize.Should().NotBeNull();
        monetize!.Data.Should().ContainKey("distributions");

        var dists = (List<Dictionary<string, object>>)monetize.Data["distributions"];
        dists.Should().NotBeEmpty();
        dists[0].Should().ContainKey("componentInstanceId");
        dists[0].Should().ContainKey("amount");
    }

    // ─── Tenki card 30 priority depends on DB count ─────────────

    [Fact]
    public void DecideMainPhaseActions_Tenki_Card30BoostWhenTwoDBOnField()
    {
        var ai = FactionAi.Create("Tenki", _cc, _effects);
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, activePlayer: 2);

        // Two Tenki DB cards already on field
        state.Player2Field.Backend[0] = TestFactory.MakeResource(
            cardId: 29, instanceId: "field_29", maxAV: 500, currentYield: 200, maxYield: 200, currentTP: null);
        state.Player2Field.Backend[1] = TestFactory.MakeResource(
            cardId: 31, instanceId: "field_31", maxAV: 500, currentYield: 200, maxYield: 200, currentTP: null);

        state.Player2Hand.Add(new HandCard { InstanceID = "h_30", CardID = 30 });
        state.Player2Hand.Add(new HandCard { InstanceID = "h_1", CardID = 1 });

        var available = new List<AvailableAction>
        {
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_30",
                CardID = 30,
                ValidZones = ["backend_2"],
            },
            new()
            {
                Type = WireActionTypes.PlayCard,
                HandInstanceID = "h_1",
                CardID = 1,
                ValidZones = ["frontend_0"],
            },
        };

        var actions = ai.DecideMainPhaseActions(state, _game, 2, available);

        var playActions = actions.Where(a => a.ActionType == WireActionTypes.PlayCard).ToList();
        playActions.Should().HaveCount(2);

        // Card 30 with 2 DB on field gets priority 85, card 1 (non-Tenki) gets 50
        playActions[0].Data["cardInstanceId"].Should().Be("h_30");
    }

    // ─── Test double ────────────────────────────────────────────

    private class NullEffectRegistry : IEffectRegistry
    {
        public EffectHandler? Get(long cardNo, TriggerType trigger) => null;
        public bool Has(long cardNo, TriggerType trigger) => false;
        public BudgetRequirement? GetBudgetRequirement(long cardNo, TriggerType trigger) => null;
        public EffectInfo? GetEffectInfo(long cardNo, TriggerType trigger) => null;
        public List<string>? GetChoiceOptions(long cardNo, TriggerType trigger) => null;
    }
}

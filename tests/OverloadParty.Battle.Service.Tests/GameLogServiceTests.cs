using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

public class GameLogServiceTests
{
    private readonly FakeGameRepository _repo = new();
    private readonly TestCardCache _cc = new();
    private readonly GameLogService _svc;

    public GameLogServiceTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-2001", name: "えくぼ", mc: 300));
        _cc.Add(TestFactory.DataCard(cardId: "TST-1009", name: "TestDB"));
        _svc = new GameLogService(_repo, _cc);
    }

    private async Task<string> SeedFinishedGame()
    {
        var game = TestFactory.MakeGame();
        game.Npc2Model = "SHE";
        game.CreatedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var state = TestFactory.MakeGameState(turn: 8, p1Budget: 1200, p2Budget: 0);

        await _repo.CreateGame(game, state);

        // Add sample events
        await _repo.AppendEvent(new GameEvent
        {
            GameID = "test-game",
            SequenceNumber = 1,
            EventType = ActionTypes.PlayCard,
            PlayerNum = 1,
            EventData = new PlayCardEventData { CardId = "TST-2001", Zone = "frontend", Index = 0 },
        });
        await _repo.AppendEvent(new GameEvent
        {
            GameID = "test-game",
            SequenceNumber = 2,
            EventType = ActionTypes.Attack,
            PlayerNum = 1,
            EventData = new AttackEventData
            {
                AttackerId = "atk_1",
                TargetId = "def_1",
                Damage = 600,
                Destroyed = true,
                SlaPenalty = 400,
            },
        });
        await _repo.AppendEvent(new GameEvent
        {
            GameID = "test-game",
            SequenceNumber = 3,
            EventType = EventTypes.TurnEnd,
            PlayerNum = null,
            EventData = new TurnEndEventData
            {
                Phase = "battle",
                NextTurn = 2,
                ActivePlayer = 2,
                CurrentPhase = "draw",
            },
        });
        await _repo.AppendEvent(new GameEvent
        {
            GameID = "test-game",
            SequenceNumber = 4,
            EventType = EventTypes.GameOver,
            PlayerNum = null,
            EventData = new GameOverEventData
            {
                WinnerNum = 1,
                WinReason = WinReasons.BudgetZero,
            },
        });

        await _repo.FinishGame("test-game", 1, WinReasons.BudgetZero);
        return "test-game";
    }

    // ─── GetGameLog (JSON) ───────────────────────────────────────

    [Fact]
    public async Task GetGameLog_ReturnsNull_WhenGameNotFound()
    {
        var result = await _svc.GetGameLog("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGameLog_ReturnsCorrectStructure()
    {
        var gameId = await SeedFinishedGame();
        var log = await _svc.GetGameLog(gameId);

        log.Should().NotBeNull();
        log!.GameId.Should().Be(gameId);
        log.Winner.Should().Be("player1");
        log.TotalTurns.Should().Be(8);
        log.FinalBudget.Should().NotBeNull();
        log.FinalBudget!.Player1.Should().Be(1200);
        log.FinalBudget.Player2.Should().Be(0);
        log.Entries.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetGameLog_EntryDescriptions_AreHumanReadable()
    {
        var gameId = await SeedFinishedGame();
        var log = await _svc.GetGameLog(gameId);

        log!.Entries[0].Description.Should().Contain("deployed").And.Contain("えくぼ").And.Contain("Frontend");
        log.Entries[1].Description.Should().Contain("attacked").And.Contain("600 damage").And.Contain("destroyed");
        log.Entries[2].Description.Should().Contain("Turn end").And.Contain("Turn 2").And.Contain("P2");
        log.Entries[3].Description.Should().Contain("Game over").And.Contain("P1 wins");
    }

    // ─── GetGameLogText ──────────────────────────────────────────

    [Fact]
    public async Task GetGameLogText_ReturnsNull_WhenGameNotFound()
    {
        var result = await _svc.GetGameLogText("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetGameLogText_ContainsHeader()
    {
        var gameId = await SeedFinishedGame();
        var text = await _svc.GetGameLogText(gameId);

        text.Should().NotBeNull();
        text.Should().Contain("=== Game test-game ===");
        text.Should().Contain("P1: P1");
        text.Should().Contain("P2: NPC");
        text.Should().Contain("P1=1200");
        text.Should().Contain("P2=0");
    }

    [Fact]
    public async Task GetGameLogText_ContainsEventLines()
    {
        var gameId = await SeedFinishedGame();
        var text = await _svc.GetGameLogText(gameId);

        text.Should().Contain("[1]");
        text.Should().Contain("[4]");
        text.Should().Contain("deployed");
        text.Should().Contain("Game over");
    }

    // ─── Event descriptions ──────────────────────────────────────

    [Theory]
    [InlineData(ActionTypes.ScaleUp, "scaled up")]
    [InlineData(ActionTypes.Monetize, "distributed")]
    [InlineData(ActionTypes.DiscardHand, "discarded")]
    [InlineData(EventTypes.PhaseChange, "ended")]
    public async Task GetGameLog_VariousEventTypes_ProduceDescriptions(string eventType, string expectedSubstring)
    {
        var game = TestFactory.MakeGame();
        var state = TestFactory.MakeGameState();
        await _repo.CreateGame(game, state);

        IEventData eventData = eventType switch
        {
            ActionTypes.ScaleUp => new ScaleUpEventData
            {
                InstanceId = "inst_1",
                TargetRank = "medium",
            },
            ActionTypes.Monetize => new MonetizeEventData { TotalAmount = 300 },
            ActionTypes.DiscardHand => new DiscardHandEventData
            {
                DiscardedCount = 2,
                DiscardedIds = ["i1", "i2"],
            },
            EventTypes.PhaseChange => new PhaseChangeEventData
            {
                PreviousPhase = "main",
                CurrentPhase = "battle",
            },
            _ => throw new InvalidOperationException($"Unhandled event type in test fixture: {eventType}"),
        };

        await _repo.AppendEvent(new GameEvent
        {
            GameID = "test-game",
            SequenceNumber = 1,
            EventType = eventType,
            PlayerNum = 1,
            EventData = eventData,
        });

        var log = await _svc.GetGameLog("test-game");
        log!.Entries[0].Description.Should().Contain(expectedSubstring);
    }

    // ─── JSON serialization ──────────────────────────────────────

    [Fact]
    public async Task SerializeToJson_ProducesValidJson()
    {
        var gameId = await SeedFinishedGame();
        var log = await _svc.GetGameLog(gameId);
        var bytes = _svc.SerializeToJson(log!);

        bytes.Should().NotBeEmpty();
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        json.Should().Contain("\"game_id\"");
        json.Should().Contain("\"entries\"");
    }

}

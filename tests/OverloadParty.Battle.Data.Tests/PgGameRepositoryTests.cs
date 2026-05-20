using Npgsql;
using OverloadParty.Battle.Data.Pg;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Data;

[Collection(PgTestCollection.Name)]
public class PgGameRepositoryTests
{
    private readonly NpgsqlDataSource _ds;

    public PgGameRepositoryTests(PgTestFixture fixture)
    {
        _ds = fixture.DataSource;
    }

    private PgGameRepository CreateRepo() => new(_ds);

    private static string NewGameID() => $"g-{Guid.NewGuid():N}"[..26];

    private static (Game game, BattleGameState state) MakeFixture(string? gameID = null)
    {
        var id = gameID ?? NewGameID();
        var now = DateTime.UtcNow;

        var game = new Game
        {
            GameID = id,
            FirstPlayer = 1,
            Status = GameStatus.Playing,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var state = new BattleGameState
        {
            GameID = id,
            Version = 1,
            CurrentTurn = 1,
            CurrentPhase = Phase.Main,
            ActivePlayer = 1,
            Player1Budget = 5000,
            Player1InsightPool = 0,
            Player1Field = new Field(),
            Player1Hand = [new UndeployedCard { InstanceID = "h1", CardID = "TST-2001" }],
            Player1Repository = [],
            Player1Trash = [],
            Player1TimeBank = 480,
            Player2Budget = 5000,
            Player2InsightPool = 0,
            Player2Field = new Field(),
            Player2Hand = [],
            Player2Repository = [],
            Player2Trash = [],
            Player2TimeBank = 480,
            NextInstanceSeq = 1,
            UpdatedAt = now,
        };

        return (game, state);
    }

    // ─── CreateGame + GetGame ───────────────────────────────

    [Fact]
    public async Task CreateGame_and_GetGame_roundtrip()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();

        await repo.CreateGame(game, state);

        var got = await repo.GetGame(game.GameID);
        got.Should().NotBeNull();
        got!.GameID.Should().Be(game.GameID);
        got.FirstPlayer.Should().Be(game.FirstPlayer);
        got.Status.Should().Be(GameStatus.Playing);
        got.WinningPlayerNum.Should().BeNull();
        got.FinishedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateGame_with_npc_restores_npc_model_via_game_npcs_join()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        game.Npc1Model = "SHE-easy";

        await repo.CreateGame(game, state);

        var got = await repo.GetGame(game.GameID);
        got.Should().NotBeNull();
        got!.Npc1Model.Should().Be("SHE-easy");
        got.Npc2Model.Should().BeNull();
    }

    [Fact]
    public async Task GetGame_returns_null_when_not_found()
    {
        var repo = CreateRepo();

        var got = await repo.GetGame("nonexistent-id");
        got.Should().BeNull();
    }

    // ─── GetGameState ───────────────────────────────────────

    [Fact]
    public async Task GetGameState_roundtrip()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        var got = await repo.GetGameState(game.GameID);
        got.Should().NotBeNull();
        got!.GameID.Should().Be(game.GameID);
        got.Version.Should().Be(1);
        got.CurrentTurn.Should().Be(1);
        got.CurrentPhase.Should().Be(Phase.Main);
        got.ActivePlayer.Should().Be(1);
        got.Player1Budget.Should().Be(5000);
        got.Player2Budget.Should().Be(5000);
        got.Player1Hand.Should().HaveCount(1);
        got.Player1Hand[0].CardID.Should().Be("TST-2001");
        got.Player1TimeBank.Should().Be(480);
        got.NextInstanceSeq.Should().Be(1);
    }

    [Fact]
    public async Task GetGameState_returns_null_when_not_found()
    {
        var repo = CreateRepo();

        var got = await repo.GetGameState("nonexistent-id");
        got.Should().BeNull();
    }

    // ─── UpdateGameState ────────────────────────────────────

    [Fact]
    public async Task UpdateGameState_modifies_state_and_increments_version()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        await repo.UpdateGameState(game.GameID, s =>
        {
            s.CurrentTurn = 2;
            s.CurrentPhase = Phase.Draw;
            s.ActivePlayer = 2;
            s.Player1Budget = 4500;
            s.Player2Budget = 4800;
            return Task.CompletedTask;
        });

        var got = await repo.GetGameState(game.GameID);
        got!.Version.Should().Be(2);
        got.CurrentTurn.Should().Be(2);
        got.CurrentPhase.Should().Be(Phase.Draw);
        got.ActivePlayer.Should().Be(2);
        got.Player1Budget.Should().Be(4500);
        got.Player2Budget.Should().Be(4800);
    }

    [Fact]
    public async Task UpdateGameState_throws_when_game_not_found()
    {
        var repo = CreateRepo();

        var act = () => repo.UpdateGameState("nonexistent", _ => Task.CompletedTask);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ─── AppendEvent / GetEvents / GetEventCount ────────────

    [Fact]
    public async Task AppendEvent_and_GetEvents_roundtrip()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        var evt1 = new GameEvent
        {
            GameID = game.GameID,
            SequenceNumber = 1,
            EventType = EventTypes.PlayCard,
            PlayerNum = 1,
            EventData = new PlayCardEventData
            {
                CardId = "TST-2001",
                Zone = "frontend",
                Index = 0,
            },
            CreatedAt = DateTime.UtcNow,
        };
        var evt2 = new GameEvent
        {
            GameID = game.GameID,
            SequenceNumber = 2,
            EventType = EventTypes.TurnStart,
            PlayerNum = null,
            EventData = new TurnStartInternalEventData { Turn = 1, ActivePlayer = 2 },
            CreatedAt = DateTime.UtcNow,
        };

        await repo.AppendEvent(evt1);
        await repo.AppendEvent(evt2);

        var events = await repo.GetEvents(game.GameID);
        events.Should().HaveCount(2);
        events[0].SequenceNumber.Should().Be(1);
        events[0].EventType.Should().Be(EventTypes.PlayCard);
        events[0].PlayerNum.Should().Be(1);
        events[0].EventData.Should().BeOfType<PlayCardEventData>()
            .Which.CardId.Should().Be("TST-2001");
        events[1].SequenceNumber.Should().Be(2);
        events[1].EventType.Should().Be(EventTypes.TurnStart);
        events[1].PlayerNum.Should().BeNull();
        events[1].EventData.Should().BeOfType<TurnStartInternalEventData>()
            .Which.Turn.Should().Be(1);
    }

    [Fact]
    public async Task GetEventCount_returns_correct_count()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        (await repo.GetEventCount(game.GameID)).Should().Be(0);

        await repo.AppendEvent(new GameEvent
        {
            GameID = game.GameID,
            SequenceNumber = 1,
            EventType = EventTypes.Monetize,
            EventData = new MonetizeEventData { TotalAmount = 0 },
            CreatedAt = DateTime.UtcNow,
        });

        (await repo.GetEventCount(game.GameID)).Should().Be(1);
    }

    // ─── FinishGame ─────────────────────────────────────────

    [Fact]
    public async Task FinishGame_updates_status_and_winner()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        await repo.FinishGame(game.GameID, 1, WinReasons.BudgetZero);

        var got = await repo.GetGame(game.GameID);
        got!.Status.Should().Be(GameStatus.Finished);
        got.WinningPlayerNum.Should().Be(1);
        got.WinReason.Should().Be(WinReasons.BudgetZero);
        got.FinishedAt.Should().NotBeNull();
    }

    // ─── UpdateGameStatus ───────────────────────────────────

    [Fact]
    public async Task UpdateGameStatus_changes_status()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        await repo.UpdateGameStatus(game.GameID, GameStatus.Finished);

        var got = await repo.GetGame(game.GameID);
        got!.Status.Should().Be(GameStatus.Finished);
    }

    // ─── JSONB Field roundtrip ──────────────────────────────

    [Fact]
    public async Task GameState_with_field_resources_roundtrips_through_JSONB()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();

        // Populate field with resources via Zone indexer
        state.Player1Field.Frontend[0] = new DeployedResource
        {
            InstanceID = "inst_0",
            CardID = "TST-2001",
            Rank = Rank.Small,
            FaceUp = true,
            MaxAV = 1400,
            CurrentAV = 1400,
            MaxTP = 600,
            CurrentTP = 600,
        };
        state.Player2Field.Backend[0] = new DeployedResource
        {
            InstanceID = "inst_1",
            CardID = "TST-1009",
            FaceUp = false,
            DeployingTurnsLeft = 1,
            MaxAV = 800,
            CurrentAV = 800,
            MaxYield = 400,
            CurrentYield = 400,
        };

        await repo.CreateGame(game, state);

        var got = await repo.GetGameState(game.GameID);
        var p1Front = got!.Player1Field.Frontend.ToArray();
        p1Front[0].Should().NotBeNull();
        p1Front[0]!.InstanceID.Should().Be("inst_0");
        p1Front[0]!.MaxTP.Should().Be(600);

        var p2Back = got.Player2Field.Backend.ToArray();
        p2Back[0].Should().NotBeNull();
        p2Back[0]!.InstanceID.Should().Be("inst_1");
        p2Back[0]!.DeployingTurnsLeft.Should().Be(1);
    }
}

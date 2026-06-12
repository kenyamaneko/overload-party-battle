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
            Player1Hand = [new UndeployedCard { InstanceID = "h1", CardID = "TST-0001" }],
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
            TurnStartedAt = now,
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
        got.Player1Hand[0].CardID.Should().Be("TST-0001");
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
            return Task.FromResult<IReadOnlyList<GameEvent>>([]);
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

        var act = () => repo.UpdateGameState("nonexistent", _ => Task.FromResult<IReadOnlyList<GameEvent>>([]));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// ターン進行・選択待ちなどエンジンが書き換える進行状態が、リロード後も保持されることを検証する。
    /// </summary>
    [Fact]
    public async Task UpdateGameState_engine_progress_fields_roundtrip()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        var turnStartedAt = new DateTime(2026, 3, 1, 10, 30, 0, DateTimeKind.Utc);
        await repo.UpdateGameState(game.GameID, s =>
        {
            s.Player1IncidentPlayedThisTurn = true;
            s.Player1HasOperated = true;
            s.Player2IncidentPlayedThisTurn = false;
            s.Player2HasOperated = true;
            s.TurnStartedAt = turnStartedAt;
            s.NextDeployOrderSeq = 7;
            s.PendingSlotSelects =
            [
                new AwaitingSlotSelect
                {
                    PlayerNum = 1,
                    Resource = new DeployedResource { InstanceID = "inst_9", CardID = "TST-0001" },
                    ValidZones = ["frontend", "backend"],
                },
            ];
            s.PendingEffectChoice = new PendingEffectChoice
            {
                ChooserPlayerNum = 2,
                OwnerPlayerNum = 1,
                EffectCardId = "TST-0003",
                EffectInstanceId = "inst_3",
                Trigger = TriggerType.OnDestroy,
                ChoiceKey = "instanceId",
                Candidates = ["inst_1", "inst_2"],
                ChoiceKind = "select_resource",
            };
            return Task.FromResult<IReadOnlyList<GameEvent>>([]);
        });

        var got = await repo.GetGameState(game.GameID);
        got!.Player1IncidentPlayedThisTurn.Should().BeTrue();
        got.Player1HasOperated.Should().BeTrue();
        got.Player2IncidentPlayedThisTurn.Should().BeFalse();
        got.Player2HasOperated.Should().BeTrue();
        got.TurnStartedAt.Should().Be(turnStartedAt);
        got.NextDeployOrderSeq.Should().Be(7);

        got.PendingSlotSelects.Should().HaveCount(1);
        got.PendingSlotSelects[0].PlayerNum.Should().Be(1);
        got.PendingSlotSelects[0].Resource.InstanceID.Should().Be("inst_9");
        got.PendingSlotSelects[0].ValidZones.Should().Equal("frontend", "backend");

        got.PendingEffectChoice.Should().NotBeNull();
        got.PendingEffectChoice!.ChooserPlayerNum.Should().Be(2);
        got.PendingEffectChoice.EffectCardId.Should().Be("TST-0003");
        got.PendingEffectChoice.Trigger.Should().Be(TriggerType.OnDestroy);
        got.PendingEffectChoice.Candidates.Should().Equal("inst_1", "inst_2");
    }

    /// <summary>
    /// 選択待ちが解消されたとき、null / 空に戻した状態が保存されることを検証する。
    /// </summary>
    [Fact]
    public async Task UpdateGameState_clears_pending_choice_state()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        state.PendingEffectChoice = new PendingEffectChoice
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            EffectCardId = "TST-0003",
            EffectInstanceId = "inst_3",
            Trigger = TriggerType.OnDeploy,
            ChoiceKey = "cardId",
            Candidates = ["TST-0001"],
            ChoiceKind = "select_card",
        };
        await repo.CreateGame(game, state);

        await repo.UpdateGameState(game.GameID, s =>
        {
            s.PendingEffectChoice = null;
            s.PendingSlotSelects = [];
            return Task.FromResult<IReadOnlyList<GameEvent>>([]);
        });

        var got = await repo.GetGameState(game.GameID);
        got!.PendingEffectChoice.Should().BeNull();
        got.PendingSlotSelects.Should().BeEmpty();
    }

    // ─── イベント永続化 (UpdateGameState 経由) ──────────────

    /// <summary>
    /// fn が返したイベントが state 更新と同一トランザクションで保存され、
    /// sequence_number が DB 採番されてイベントへ書き戻されることを検証する。
    /// </summary>
    [Fact]
    public async Task UpdateGameState_persists_events_and_assigns_sequence_numbers()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        var evt1 = new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.PlayCard,
            PlayerNum = 1,
            EventData = new PlayCardEventData
            {
                CardId = "TST-0001",
                Zone = "frontend",
                Index = 0,
            },
        };
        var evt2 = new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.TurnStart,
            PlayerNum = null,
            EventData = new TurnStartInternalEventData { Turn = 1, ActivePlayer = 2 },
        };

        await repo.UpdateGameState(game.GameID,
            _ => Task.FromResult<IReadOnlyList<GameEvent>>([evt1, evt2]));

        evt1.SequenceNumber.Should().Be(1);
        evt2.SequenceNumber.Should().Be(2);

        var events = await repo.GetEvents(game.GameID);
        events.Should().HaveCount(2);
        events[0].SequenceNumber.Should().Be(1);
        events[0].EventType.Should().Be(EventTypes.PlayCard);
        events[0].PlayerNum.Should().Be(1);
        events[0].EventData.Should().BeOfType<PlayCardEventData>()
            .Which.CardId.Should().Be("TST-0001");
        events[1].SequenceNumber.Should().Be(2);
        events[1].EventType.Should().Be(EventTypes.TurnStart);
        events[1].PlayerNum.Should().BeNull();
        events[1].EventData.Should().BeOfType<TurnStartInternalEventData>()
            .Which.Turn.Should().Be(1);
    }

    /// <summary>
    /// 複数回の UpdateGameState をまたいで sequence_number が連番で継続することを検証する。
    /// </summary>
    [Fact]
    public async Task UpdateGameState_continues_sequence_numbers_across_calls()
    {
        var repo = CreateRepo();
        var (game, state) = MakeFixture();
        await repo.CreateGame(game, state);

        await repo.UpdateGameState(game.GameID,
            _ => Task.FromResult<IReadOnlyList<GameEvent>>([new GameEvent
            {
                GameID = game.GameID,
                EventType = EventTypes.Monetize,
                EventData = new MonetizeEventData { TotalAmount = 0 },
            }]));

        var evtNext = new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.TurnEnd,
            EventData = new TurnEndEventData
            {
                Phase = "battle",
                NextTurn = 2,
                ActivePlayer = 2,
                CurrentPhase = "draw",
            },
        };
        await repo.UpdateGameState(game.GameID,
            _ => Task.FromResult<IReadOnlyList<GameEvent>>([evtNext]));

        evtNext.SequenceNumber.Should().Be(2);
        var events = await repo.GetEvents(game.GameID);
        events.Select(e => e.SequenceNumber).Should().Equal(1, 2);
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
            CardID = "TST-0001",
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
            CardID = "TST-0002",
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

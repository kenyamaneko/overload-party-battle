using OverloadParty.Battle.Models;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

/// <summary>
/// GameLogService がイベントを人間可読な説明文へ変換する際の、イベント種別ごとの分岐と
/// 勝者ラベル・所要時間の表記仕様を検証する。
/// </summary>
public class GameLogServiceDescriptionTests
{
    private const string GameId = "test-game";
    private static readonly DateTime BaseCreatedAt = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>カードキャッシュとフェイク永続化層を備えた GameLogService 一式を返す。</summary>
    /// <returns>シード済み永続化フェイクと GameLogService。</returns>
    private static (FakeGameRepository Repo, GameLogService Svc) MakeService()
    {
        var cardCache = new TestCardCache();
        cardCache.Add(TestFactory.ComputeCard(cardId: "TST-0001", name: "えくぼ", mc: 300));
        cardCache.Add(TestFactory.ComputeCard(cardId: "TST-0002", name: "コスト無し", mc: 0));
        var repo = new FakeGameRepository();
        return (repo, new GameLogService(repo, cardCache));
    }

    /// <summary>単一イベントだけを持つゲームのログを構築する。</summary>
    /// <param name="data">シードするイベントのペイロード。</param>
    /// <param name="playerNum">イベントのプレイヤー番号。システムイベントは null。</param>
    /// <param name="winningPlayerNum">ゲームの勝者番号。</param>
    /// <returns>構築されたゲームログ。</returns>
    private static async Task<GameLogResponse> BuildLogWithEvent(IEventData? data, long? playerNum, int? winningPlayerNum)
    {
        var (repo, svc) = MakeService();
        var game = TestFactory.MakeGame();
        game.WinningPlayerNum = winningPlayerNum;
        var state = TestFactory.MakeGameState();
        await repo.CreateGame(game, state);
        repo.SeedEvent(new GameEvent
        {
            GameID = GameId,
            SequenceNumber = 1,
            PlayerNum = playerNum,
            EventData = data,
        });
        return (await svc.GetGameLog(GameId))!;
    }

    /// <summary>単一イベントを描画した説明文を返す。</summary>
    /// <param name="data">描画対象イベントのペイロード。</param>
    /// <param name="playerNum">イベントのプレイヤー番号。システムイベントは null。</param>
    /// <param name="winningPlayerNum">ゲームの勝者番号。</param>
    /// <returns>イベントの人間可読な説明文。</returns>
    private static async Task<string> RenderDescription(IEventData data, long? playerNum, int? winningPlayerNum = 1)
    {
        var log = await BuildLogWithEvent(data, playerNum, winningPlayerNum);
        return log.Entries[0].Description;
    }

    /// <summary>指定メタデータを持つゲームの構造化ログを構築する。</summary>
    /// <param name="winningPlayerNum">勝者番号。</param>
    /// <param name="createdAt">ゲーム作成時刻。</param>
    /// <param name="finishedAt">ゲーム終了時刻。進行中は null。</param>
    /// <returns>構築されたゲームログ。</returns>
    private static async Task<GameLogResponse> BuildLog(int? winningPlayerNum, DateTime createdAt, DateTime? finishedAt)
    {
        var (repo, svc) = MakeService();
        var game = TestFactory.MakeGame();
        game.WinningPlayerNum = winningPlayerNum;
        game.CreatedAt = createdAt;
        game.FinishedAt = finishedAt;
        var state = TestFactory.MakeGameState();
        await repo.CreateGame(game, state);
        return (await svc.GetGameLog(GameId))!;
    }

    /// <summary>指定メタデータを持つゲームのテキストログを構築する。</summary>
    /// <param name="createdAt">ゲーム作成時刻。</param>
    /// <param name="finishedAt">ゲーム終了時刻。進行中は null。</param>
    /// <returns>テキスト形式のゲームログ。</returns>
    private static async Task<string> BuildText(DateTime createdAt, DateTime? finishedAt)
    {
        var (repo, svc) = MakeService();
        var game = TestFactory.MakeGame();
        game.WinningPlayerNum = 1;
        game.CreatedAt = createdAt;
        game.FinishedAt = finishedAt;
        var state = TestFactory.MakeGameState();
        await repo.CreateGame(game, state);
        return (await svc.GetGameLogText(GameId))!;
    }

    /// <summary>イベント種別ごとの説明文への変換を検証する。</summary>
    public class EventDescriptions
    {
        /// <summary>イベント種別ごとの (ペイロード, プレイヤー番号, 期待説明文) ケースを列挙する。</summary>
        /// <returns>説明文変換のケース列。</returns>
        public static IEnumerable<object[]> DescriptionCases() =>
        [
            new object[] { new AttachCardEventData { CardId = "TST-0001" }, (long?)1, "P1 attached \"えくぼ\"" },
            new object[] { new UseIgnitionEventData { CardId = "TST-0001" }, (long?)2, "P2 activated effect: えくぼ" },
            new object[] { new PlayCardEventData { CardId = "TST-0001", Zone = "frontend" }, (long?)1, "P1 deployed \"えくぼ\" to Frontend [-300 Budget]" },
            new object[] { new PlayCardEventData { CardId = "TST-0002", Zone = "backend" }, (long?)1, "P1 deployed \"コスト無し\" to Backend" },
            new object[] { new PlayCardEventData { CardId = "TST-0001", Cancelled = true }, (long?)1, "P1 deploy of \"えくぼ\" was cancelled" },
            new object[] { new AttackEventData { Cancelled = true }, (long?)1, "P1 attack was cancelled" },
            new object[] { new AttackEventData { Damage = 300, Destroyed = false }, (long?)1, "P1 attacked for 300 damage" },
            new object[] { new AttackEventData { Damage = 300, Destroyed = false, SlaPenalty = 0 }, (long?)1, "P1 attacked for 300 damage" },
            new object[] { new ScaleUpEventData { TargetRank = "large", InstanceFamily = "db" }, (long?)1, "P1 scaled up → Large db" },
            new object[] { new DiscardHandEventData { DiscardedCount = 1 }, (long?)1, "P1 discarded 1 card" },
            new object[] { new PhaseEndEventData { Phase = "main", NeedsDiscard = false }, (long?)1, "Main phase ended" },
            new object[] { new PhaseEndEventData { Phase = "end", NeedsDiscard = true }, (long?)1, "End phase ended (discard required)" },
            new object[] { new TurnStartInternalEventData { Turn = 3, ActivePlayer = 2 }, (long?)null, "Turn start → Turn 3 (P2)" },
            new object[] { new ReactiveRevealedEventData(), (long?)1, "P1 reactive revealed" },
            new object[] { new BattleStartEventData(), (long?)2, "P2 battle start" },
            new object[] { new SelectSlotEventData { CardId = "TST-0001", Zone = "frontend", Index = 2 }, (long?)1, "P1 selected slot for \"えくぼ\" at Frontend[2]" },
        ];

        /// <summary>各イベント種別が期待どおりの説明文へ変換されることを検証する。</summary>
        /// <param name="data">描画対象イベントのペイロード。</param>
        /// <param name="playerNum">イベントのプレイヤー番号。</param>
        /// <param name="expected">期待される説明文。</param>
        [Theory]
        [MemberData(nameof(DescriptionCases))]
        public async Task RendersExpectedDescription(IEventData data, long? playerNum, string expected)
        {
            var description = await RenderDescription(data, playerNum);
            description.Should().Be(expected);
        }
    }

    /// <summary>ゲーム終了イベントが勝者番号から説明文を導くことを検証する。</summary>
    public class GameOverDescriptions
    {
        /// <summary>勝者番号に応じた終了説明文を検証する。</summary>
        /// <param name="winningPlayerNum">ゲームの勝者番号。引き分けは null または 0。</param>
        /// <param name="expected">期待される説明文。</param>
        [Theory]
        [InlineData(null, "Game over: Draw")]
        [InlineData(0, "Game over: Draw")]
        [InlineData(2, "Game over: P2 wins")]
        public async Task RendersFromWinningPlayer(int? winningPlayerNum, string expected)
        {
            var description = await RenderDescription(
                new GameOverEventData(), playerNum: null, winningPlayerNum: winningPlayerNum);
            description.Should().Be(expected);
        }
    }

    /// <summary>不正なイベントデータを握りつぶさず例外にすることを検証する。</summary>
    public class MalformedEventData
    {
        /// <summary>switch のどのアームにも一致しない未知のイベントデータ型。</summary>
        private sealed class UnrecognizedEventData : IEventData { }

        /// <summary>EventData が null のイベントは例外になることを検証する。</summary>
        [Fact]
        public async Task NullEventData_Throws()
        {
            Func<Task> act = () => BuildLogWithEvent(null, playerNum: 1, winningPlayerNum: 1);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        /// <summary>未知のイベントデータ型は例外になることを検証する。</summary>
        [Fact]
        public async Task UnknownEventDataType_Throws()
        {
            Func<Task> act = () => BuildLogWithEvent(new UnrecognizedEventData(), playerNum: 1, winningPlayerNum: 1);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    /// <summary>構造化ログの勝者ラベルが勝者番号から導かれることを検証する。</summary>
    public class WinnerLabel
    {
        /// <summary>勝者番号がラベルへ変換されることを検証する。</summary>
        /// <param name="winningPlayerNum">ゲームの勝者番号。未決着は null または 0。</param>
        /// <param name="expected">期待される勝者ラベル。</param>
        [Theory]
        [InlineData(null, null)]
        [InlineData(0, null)]
        [InlineData(2, "player2")]
        public async Task MapsWinningPlayerNum(int? winningPlayerNum, string? expected)
        {
            var log = await BuildLog(winningPlayerNum, BaseCreatedAt, BaseCreatedAt.AddMinutes(5));
            log.Winner.Should().Be(expected);
        }

        /// <summary>定義外の勝者番号は例外になることを検証する。</summary>
        [Fact]
        public async Task InvalidWinningPlayerNum_Throws()
        {
            Func<Task> act = () => BuildLog(3, BaseCreatedAt, BaseCreatedAt.AddMinutes(5));
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    /// <summary>所要時間が終了状態に応じて算出・表記されることを検証する。</summary>
    public class Duration
    {
        /// <summary>終了済みゲームの所要時間が秒数で算出されることを検証する。</summary>
        [Fact]
        public async Task FinishedGame_ReportsSeconds()
        {
            var log = await BuildLog(1, BaseCreatedAt, BaseCreatedAt.AddSeconds(150));
            log.DurationSeconds.Should().Be(150);
        }

        /// <summary>進行中ゲームの所要時間が null になることを検証する。</summary>
        [Fact]
        public async Task InProgressGame_HasNullSeconds()
        {
            var log = await BuildLog(1, BaseCreatedAt, null);
            log.DurationSeconds.Should().BeNull();
        }

        /// <summary>終了済みゲームのテキスト所要時間が時・分・秒で整形されることを検証する。</summary>
        /// <param name="hours">作成時刻からの経過時。</param>
        /// <param name="minutes">作成時刻からの経過分。</param>
        /// <param name="seconds">作成時刻からの経過秒。</param>
        /// <param name="expected">期待される所要時間表記。</param>
        [Theory]
        [InlineData(0, 2, 30, "2m30s")]
        [InlineData(1, 5, 7, "1h05m07s")]
        public async Task TextFormatsFinishedDuration(int hours, int minutes, int seconds, string expected)
        {
            var finishedAt = BaseCreatedAt.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
            var text = await BuildText(BaseCreatedAt, finishedAt);
            text.Should().Contain(expected);
        }

        /// <summary>進行中ゲームのテキスト所要時間が進行中表記になることを検証する。</summary>
        [Fact]
        public async Task InProgressGame_TextShowsInProgress()
        {
            var text = await BuildText(BaseCreatedAt, null);
            text.Should().Contain("in progress");
        }
    }
}

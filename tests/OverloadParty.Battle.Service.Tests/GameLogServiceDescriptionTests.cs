using OverloadParty.Battle.Models;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

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

    [Trait("対象", "イベント説明文")]
    public class EventDescriptions
    {
        /// <summary>各イベント種別の (ペイロード, プレイヤー番号, 説明文に載るべきデータ) ケースを列挙する。</summary>
        /// <returns>データ伝達検証のケース列。</returns>
        public static IEnumerable<object[]> PayloadDataCases() =>
        [
            new object[] { new AttachCardEventData { CardId = "TST-0001" }, (long?)1, new[] { "p1", "えくぼ" } },
            new object[] { new UseIgnitionEventData { CardId = "TST-0001" }, (long?)2, new[] { "p2", "えくぼ" } },
            new object[] { new PlayCardEventData { CardId = "TST-0001", Zone = "frontend" }, (long?)1, new[] { "p1", "えくぼ", "frontend", "300" } },
            new object[] { new AttackEventData { Damage = 300, Destroyed = false }, (long?)1, new[] { "p1", "300" } },
            new object[] { new AttackEventData { Damage = 600, Destroyed = true, SlaPenalty = 400 }, (long?)1, new[] { "p1", "600", "400" } },
            new object[] { new ScaleUpEventData { TargetRank = "large", InstanceFamily = "db" }, (long?)1, new[] { "p1", "large", "db" } },
            new object[] { new DiscardHandEventData { DiscardedCount = 1 }, (long?)1, new[] { "p1", "1" } },
            new object[] { new PhaseEndEventData { Phase = "main", NeedsDiscard = false }, (long?)1, new[] { "main" } },
            new object[] { new PhaseEndEventData { Phase = "end", NeedsDiscard = true }, (long?)1, new[] { "end" } },
            new object[] { new TurnStartInternalEventData { Turn = 3, ActivePlayer = 2 }, (long?)null, new[] { "3", "p2" } },
            new object[] { new ReactiveRevealedEventData(), (long?)1, new[] { "p1" } },
            new object[] { new BattleStartEventData(), (long?)2, new[] { "p2" } },
            new object[] { new SelectSlotEventData { CardId = "TST-0001", Zone = "frontend", Index = 2 }, (long?)1, new[] { "p1", "えくぼ", "frontend", "2" } },
        ];

        [Theory(DisplayName = "各イベント種別の説明文が、ペイロードの主要データを含む")]
        [MemberData(nameof(PayloadDataCases))]
        public async Task ConveysPayloadData(IEventData data, long? playerNum, string[] expectedTokens)
        {
            var description = await RenderDescription(data, playerNum);
            // 整形 (capitalize) を pin しないよう小文字化し、データの有無だけを確かめる
            description.ToLowerInvariant().Should().ContainAll(expectedTokens);
        }

        [Fact(DisplayName = "維持コスト 0 のデプロイの説明文はバジェット注記を含まない")]
        public async Task ZeroCostDeploy_OmitsBudgetAnnotation()
        {
            var description = await RenderDescription(
                new PlayCardEventData { CardId = "TST-0002", Zone = "backend" }, playerNum: 1);
            description.Should().Contain("コスト無し");
            description.Should().NotContain("Budget");
        }

        [Fact(DisplayName = "取り消されたデプロイの説明文は維持コストを含まない")]
        public async Task CancelledDeploy_OmitsCost()
        {
            var description = await RenderDescription(
                new PlayCardEventData { CardId = "TST-0001", Cancelled = true }, playerNum: 1);
            description.Should().Contain("えくぼ");
            description.Should().NotContain("Budget");
        }

        [Fact(DisplayName = "取り消された攻撃の説明文はダメージ量を含まない")]
        public async Task CancelledAttack_OmitsDamageValue()
        {
            var description = await RenderDescription(
                new AttackEventData { Cancelled = true, Damage = 999 }, playerNum: 1);
            description.Should().NotContain("999");
        }

        [Fact(DisplayName = "SLA ペナルティ 0 の攻撃の説明文はペナルティ注記を含まない")]
        public async Task ZeroSlaAttack_OmitsPenalty()
        {
            var description = await RenderDescription(
                new AttackEventData { Damage = 300, Destroyed = false, SlaPenalty = 0 }, playerNum: 1);
            description.Should().Contain("300");
            description.Should().NotContain("SLA");
        }

        [Fact(DisplayName = "カタログに無いカードのイベント説明文は、Card#ID の代替表記になる")]
        public async Task UncatalogedCard_UsesCardHashIdFallback()
        {
            var description = await RenderDescription(
                new PlayCardEventData { CardId = "TST-9999", Zone = "frontend" }, playerNum: 1);

            description.Should().Contain("Card#TST-9999");
        }

        [Fact(DisplayName = "プレイヤー番号の無いイベントの説明文は、System と表記される")]
        public async Task NoPlayerNum_StartsWithSystem()
        {
            var description = await RenderDescription(
                new PlayCardEventData { CardId = "TST-0001", Zone = "frontend" }, playerNum: null);

            description.Should().StartWith("System");
        }

        [Fact(DisplayName = "手札破棄 1 枚の説明文は、単数形 (1 card) になる")]
        public async Task DiscardOneCard_UsesSingularForm()
        {
            var description = await RenderDescription(
                new DiscardHandEventData { DiscardedCount = 1 }, playerNum: 1);

            description.Should().Contain("1 card");
            description.Should().NotContain("1 cards");
        }

        [Fact(DisplayName = "手札破棄 2 枚の説明文は、複数形 (2 cards) になる")]
        public async Task DiscardTwoCards_UsesPluralForm()
        {
            var description = await RenderDescription(
                new DiscardHandEventData { DiscardedCount = 2 }, playerNum: 1);

            description.Should().Contain("2 cards");
        }
    }

    [Trait("対象", "ゲーム終了の説明文")]
    public class GameOverDescriptions
    {
        [Theory(DisplayName = "ゲーム終了の説明文が勝者番号に応じた勝敗結果を含む")]
        [InlineData(null, "draw")]
        [InlineData(0, "draw")]
        [InlineData(2, "p2")]
        public async Task ConveysWinningResult(int? winningPlayerNum, string expectedResult)
        {
            var description = await RenderDescription(
                new GameOverEventData(), playerNum: null, winningPlayerNum: winningPlayerNum);
            description.ToLowerInvariant().Should().Contain(expectedResult);
        }
    }

    [Trait("対象", "不正なイベントデータ")]
    public class MalformedEventData
    {
        /// <summary>switch のどのアームにも一致しない未知のイベントデータ型。</summary>
        private sealed class UnrecognizedEventData : IEventData { }

        [Fact(DisplayName = "イベントデータが null のイベントは例外になる")]
        public async Task NullEventData_Throws()
        {
            Func<Task> act = () => BuildLogWithEvent(null, playerNum: 1, winningPlayerNum: 1);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact(DisplayName = "未知のイベントデータ型は例外になる")]
        public async Task UnknownEventDataType_Throws()
        {
            Func<Task> act = () => BuildLogWithEvent(new UnrecognizedEventData(), playerNum: 1, winningPlayerNum: 1);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Trait("対象", "勝者ラベル")]
    public class WinnerLabel
    {
        [Theory(DisplayName = "構造化ログの勝者ラベルが勝者番号から導かれる")]
        [InlineData(null, null)]
        [InlineData(0, null)]
        [InlineData(2, "player2")]
        public async Task MapsWinningPlayerNum(int? winningPlayerNum, string? expected)
        {
            var log = await BuildLog(winningPlayerNum, BaseCreatedAt, BaseCreatedAt.AddMinutes(5));
            log.Winner.Should().Be(expected);
        }

        [Fact(DisplayName = "定義外の勝者番号は例外になる")]
        public async Task InvalidWinningPlayerNum_Throws()
        {
            Func<Task> act = () => BuildLog(3, BaseCreatedAt, BaseCreatedAt.AddMinutes(5));
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Trait("対象", "所要時間")]
    public class Duration
    {
        [Fact(DisplayName = "終了済みゲームの所要時間が秒数で算出される")]
        public async Task FinishedGame_ReportsSeconds()
        {
            var log = await BuildLog(1, BaseCreatedAt, BaseCreatedAt.AddSeconds(150));
            log.DurationSeconds.Should().Be(150);
        }

        [Fact(DisplayName = "進行中ゲームの所要時間は null になる")]
        public async Task InProgressGame_HasNullSeconds()
        {
            var log = await BuildLog(1, BaseCreatedAt, null);
            log.DurationSeconds.Should().BeNull();
        }

        [Theory(DisplayName = "終了済みゲームのテキスト所要時間が時・分・秒で整形される")]
        [InlineData(0, 2, 30, "2m30s")]
        [InlineData(1, 5, 7, "1h05m07s")]
        public async Task TextFormatsFinishedDuration(int hours, int minutes, int seconds, string expected)
        {
            var finishedAt = BaseCreatedAt.AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
            var text = await BuildText(BaseCreatedAt, finishedAt);
            text.Should().Contain(expected);
        }

        [Fact(DisplayName = "進行中ゲームのテキスト所要時間は進行中表記になる")]
        public async Task InProgressGame_TextShowsInProgress()
        {
            var text = await BuildText(BaseCreatedAt, null);
            text.Should().Contain("in progress");
        }
    }
}

using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

public class GameLogServiceTests
{
    /// <summary>Shared setup for GameLogService tests (repository, card cache, service, and a seeded finished game).</summary>
    public abstract class Base
    {
        protected readonly FakeGameRepository _repo = new();
        protected readonly TestCardCache _cc = new();
        protected readonly GameLogService _svc;

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", name: "えくぼ", mc: 300));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", name: "TestDB"));
            _svc = new GameLogService(_repo, _cc);
        }

        protected async Task<string> SeedFinishedGame()
        {
            var game = TestFactory.MakeGame();
            game.Npc2Model = "SHE";
            game.CreatedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var state = TestFactory.MakeGameState(turn: 8, p1Budget: 1200, p2Budget: 0);

            await _repo.CreateGame(game, state);

            // Add sample events
            _repo.SeedEvent(new GameEvent
            {
                GameID = "test-game",
                SequenceNumber = 1,
                EventType = ActionTypes.PlayCard,
                PlayerNum = 1,
                EventData = new PlayCardEventData { CardId = "TST-0001", Zone = "frontend", Index = 0 },
            });
            _repo.SeedEvent(new GameEvent
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
            _repo.SeedEvent(new GameEvent
            {
                GameID = "test-game",
                SequenceNumber = 3,
                EventType = EventTypes.TurnEnd,
                PlayerNum = null,
                EventData = new TurnEndEventData
                {
                    Phase = "battle",
                    // NextTurn と ActivePlayer を別値にし、説明文での取り違えを検出可能にする
                    NextTurn = 5,
                    ActivePlayer = 2,
                    CurrentPhase = "draw",
                },
            });
            _repo.SeedEvent(new GameEvent
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

        protected async Task<string> SeedNoGame()
        {
            var game = TestFactory.MakeGame();
            game.CreatedAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
            var state = TestFactory.MakeGameState(turn: 3, p1Budget: 800, p2Budget: 900);

            await _repo.CreateGame(game, state);
            await _repo.MarkNoGame("test-game");
            return "test-game";
        }
    }

    [Trait("対象", "構造化ログ")]
    public class GetGameLog : Base
    {
        [Fact(DisplayName = "存在しないゲーム ID の構造化ログは null になる")]
        public async Task ReturnsNull_WhenGameNotFound()
        {
            var result = await _svc.GetGameLog("nonexistent");
            result.Should().BeNull();
        }

        [Fact(DisplayName = "終了済みゲームの構造化ログに勝者・総ターン数・最終バジェット・エントリが載る")]
        public async Task ReturnsCorrectStructure()
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

        [Fact(DisplayName = "ノーゲームになった対戦の構造化ログでは、勝者が引き分けとも決着済みの勝敗とも異なる値になる")]
        public async Task NoGame_WinnerDiffersFromDrawAndDecidedOutcomes()
        {
            var gameId = await SeedNoGame();
            var log = await _svc.GetGameLog(gameId);

            log.Should().NotBeNull();
            log!.Winner.Should().Be("no_game");
        }

        [Fact(DisplayName = "構造化ログの各エントリが対応するイベントのデータを含む")]
        public async Task EntriesConveyEventData()
        {
            var gameId = await SeedFinishedGame();
            var log = await _svc.GetGameLog(gameId);

            // PlayCard はカード名と維持コストを伝える
            log!.Entries[0].Description.Should().Contain("えくぼ").And.Contain("300");
            // Attack はダメージ量と SLA ペナルティ額を伝える
            log.Entries[1].Description.Should().Contain("600").And.Contain("400");
            // TurnEnd は遷移先ターン番号 (5) とターンプレイヤー番号 (2) を伝える
            log.Entries[2].Description.Should().Contain("5").And.Contain("2");
            // GameOver は勝者を伝える
            log.Entries[3].Description.Should().Contain("P1");
        }
    }

    [Trait("対象", "テキストログ")]
    public class GetGameLogText : Base
    {
        [Fact(DisplayName = "存在しないゲーム ID のテキストログは null になる")]
        public async Task ReturnsNull_WhenGameNotFound()
        {
            var result = await _svc.GetGameLogText("nonexistent");
            result.Should().BeNull();
        }

        [Fact(DisplayName = "テキストログにゲーム ID・NPC モデル・最終バジェット・勝因が載る")]
        public async Task ConveysGameMetadata()
        {
            var gameId = await SeedFinishedGame();
            var text = await _svc.GetGameLogText(gameId);

            text.Should().NotBeNull();
            // ゲーム ID・NPC モデル・最終バジェット・勝因を伝える
            text.Should().Contain(gameId);
            text.Should().Contain("SHE");
            text.Should().Contain("1200");
            text.Should().Contain(WinReasons.BudgetZero);
        }

        [Fact(DisplayName = "テキストログに各イベントのデータが載る")]
        public async Task ConveysEventData()
        {
            var gameId = await SeedFinishedGame();
            var text = await _svc.GetGameLogText(gameId);

            // 各イベントのデータ (デプロイしたカード名・攻撃のダメージ量) がテキストに載る
            text.Should().Contain("えくぼ");
            text.Should().Contain("600");
        }

        [Fact(DisplayName = "ノーゲームになった対戦のテキストログには No Game と載り、Draw にも N/A にもならない")]
        public async Task NoGame_ShowsNoGame_NotDrawOrNotApplicable()
        {
            var gameId = await SeedNoGame();
            var text = await _svc.GetGameLogText(gameId);

            text.Should().NotBeNull();
            text.Should().Contain("No Game");
            text.Should().NotContain("Draw");
            text.Should().NotContain("N/A");
        }
    }

    [Trait("対象", "イベント説明文")]
    public class EventDescriptions : Base
    {
        [Theory(DisplayName = "各イベント種別の説明文が自身のペイロードのデータを含む")]
        [InlineData(ActionTypes.ScaleUp, "medium")]
        [InlineData(ActionTypes.Monetize, "300")]
        [InlineData(ActionTypes.DiscardHand, "2")]
        [InlineData(EventTypes.PhaseChange, "battle")]
        public async Task ConveyPayloadData(string eventType, string expectedData)
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

            _repo.SeedEvent(new GameEvent
            {
                GameID = "test-game",
                SequenceNumber = 1,
                EventType = eventType,
                PlayerNum = 1,
                EventData = eventData,
            });

            var log = await _svc.GetGameLog("test-game");
            // 各イベントが自身のペイロードのデータを説明文に載せる
            log!.Entries[0].Description.Should().ContainEquivalentOf(expectedData);
        }
    }

    [Trait("対象", "ログの JSON 直列化")]
    public class JsonSerialization : Base
    {
        [Fact(DisplayName = "構造化ログの JSON 直列化結果に game_id と entries が含まれる")]
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
}

using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

public class GameServiceTests
{
    /// <summary>Shared setup for GameService tests (repository, card cache, engine, NPC-capable service, and deck/summary helpers).</summary>
    public abstract class Base
    {
        protected readonly FakeGameRepository _repo = new();
        protected readonly TestCardCache _cc = new();
        protected readonly GameEngine _engine;
        protected readonly GameService _svc;

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 800, av: 1600, slaPenalty: 500, deployTurns: 1, name: "SlowCompute"));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002"));
            _engine = new GameEngine(_repo, _cc, new EffectRegistry(), new InitiativeCatalog(TestFactory.StandardInitiatives()));
            var npcDeck = Enumerable.Range(0, InitialValues.DeckSize)
                .Select(_ => new DeckEntry { CardId = "TST-0001", Copies = 1 })
                .ToList();
            var aiConfigs = new Dictionary<string, AiConfig>
            {
                [Factions.SHE] = new AiConfig { Model = Factions.SHE, Faction = Factions.SHE, Deck = npcDeck, RoutineId = "IN-0001", SpecialId = "IN-0002" },
            };
            var npcRunner = new NpcRunner(_engine, _repo, _cc, aiConfigs, NullNpcLogger.Instance);
            _svc = new GameService(_engine, _repo, _cc, npcRunner, aiConfigs);
        }

        protected List<DeckSnapshotCard> MakePlayerCards(string cardId = "TST-0001")
        {
            return Enumerable.Range(0, InitialValues.DeckSize)
                .Select(_ => new DeckSnapshotCard { CardId = cardId })
                .ToList();
        }

        protected static readonly List<PlayerSummarySnapshot> DefaultPlayerSummaries =
        [
            new() { PlayerNum = 1, Name = "p1", Level = 1 },
            new() { PlayerNum = 2, Name = "p2", Level = 1 },
        ];

        protected static readonly List<PlayerSummarySnapshot> NpcPlayerSummaries =
        [
            new() { PlayerNum = 1, Name = "p1", Level = 1 },
            new() { PlayerNum = 2, Name = "SHE 配達員", Level = null },
        ];

        /// <summary>No-op logger for NpcRunner so tests run without log noise.</summary>
        protected class NullNpcLogger : ILogger<NpcRunner>
        {
            public static readonly NullNpcLogger Instance = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }

    [Trait("対象", "PvP マッチからのゲーム生成")]
    public class CreateGameFromMatch : Base
    {
        [Fact(DisplayName = "PvP マッチからゲームを生成するとステータスが Playing になる")]
        public async Task CreatesGame_WithCorrectStatus()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            game.Should().NotBeNull();
            game.Status.Should().Be(GameStatus.Playing);
            game.GameID.Should().NotBeNullOrEmpty();
        }

        [Fact(DisplayName = "PvP マッチからのゲーム生成でプレイヤーサマリが永続化される")]
        public async Task PersistsPlayerSummaries()
        {
            var cards = MakePlayerCards();
            var summaries = new List<PlayerSummarySnapshot>
            {
                new() { PlayerNum = 1, Name = "alice", Level = 7 },
                new() { PlayerNum = 2, Name = "bob", Level = 12 },
            };

            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", summaries);

            var persisted = await _repo.GetPlayerSummaries(game.GameID);
            persisted.Should().HaveCount(2);
            persisted.Should().Contain(s => s.PlayerNum == 1 && s.Name == "alice" && s.Level == 7);
            persisted.Should().Contain(s => s.PlayerNum == 2 && s.Name == "bob" && s.Level == 12);
        }

        [Fact(DisplayName = "PvP マッチからのゲーム生成後、状態がターン 1・メインフェーズで初期化される")]
        public async Task InitializesState_InMainPhase()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);
            state.Should().NotBeNull();
            // After PostCreateAdvance, draw phase auto-advances to main
            state!.CurrentPhase.Should().Be(Phase.Main);
            state.CurrentTurn.Should().Be(1);
        }

        [Fact(DisplayName = "PvP マッチからのゲーム生成で両プレイヤーが初期バジェットを持つ")]
        public async Task PlayersHaveInitialBudget()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);
            state!.Player1Budget.Should().Be(BattleConstants.InitialBudget);
            state.Player2Budget.Should().Be(BattleConstants.InitialBudget);
        }
    }

    [Trait("対象", "NPC バトルの開始")]
    public class StartNPCBattle : Base
    {
        [Fact(DisplayName = "NPC バトルを開始すると NPC プレイヤー付きのゲームが生成される")]
        public async Task CreatesGame_WithNpcPlayer()
        {
            var cards = MakePlayerCards();
            var game = await _svc.StartNPCBattle(cards, "IN-0001", "IN-0002", Factions.SHE, NpcPlayerSummaries);

            game.Should().NotBeNull();
            game.Npc2Model.Should().NotBeNull();
            game.Status.Should().Be(GameStatus.Playing);
        }

        [Fact(DisplayName = "空のデッキで NPC バトルを開始すると例外になる")]
        public async Task EmptyDeck_Throws()
        {
            var act = () => _svc.StartNPCBattle([], "IN-0001", "IN-0002", "SHE-easy", NpcPlayerSummaries);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*empty*");
        }

        [Fact(DisplayName = "未知の陣営で NPC バトルを開始すると例外になる")]
        public async Task UnknownFaction_Throws()
        {
            var cards = MakePlayerCards();
            var act = () => _svc.StartNPCBattle(cards, "IN-0001", "IN-0002", "unknown_faction", NpcPlayerSummaries);

            await act.Should().ThrowAsync<GameRuleException>()
                .WithMessage("*No AI config found*");
        }
    }

    [Trait("対象", "プレイヤーアクションの処理")]
    public class ProcessAction : Base
    {
        [Fact(DisplayName = "カードをプレイするアクションを処理すると状態を含む結果が返り決着しない")]
        public async Task PlayCard_ReturnsStateWithResult()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);
            var cardToPlay = state!.GetHand(state.ActivePlayer).First();

            var req = new PlayCardRequest
            {
                CardInstanceID = cardToPlay.InstanceID,
                Zone = Zones.Frontend,
                Index = 0,
            };

            var result = await _svc.ProcessAction(game.GameID, state.ActivePlayer, ActionType.PlayCard, req);

            result.Should().NotBeNull();
            result.State.Should().NotBeNull();
            result.GameOver.Should().BeNull();
        }

        [Fact(DisplayName = "投了のアクションを処理すると決着結果が返る")]
        public async Task Forfeit_ReturnsGameOver()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);

            var result = await _svc.ProcessAction(game.GameID, state!.ActivePlayer, ActionType.Forfeit, new ForfeitRequest { Reason = WinReasons.Surrender });

            result.Should().NotBeNull();
            result.GameOver.Should().NotBeNull();
            result.State.Should().NotBeNull();
        }

        [Fact(DisplayName = "フェーズ終了のアクションを処理すると有効な状態が返り決着しない")]
        public async Task EndPhase_ReturnsValidState()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);
            state!.CurrentPhase.Should().Be(Phase.Main);

            var result = await _svc.ProcessAction(game.GameID, state.ActivePlayer, ActionType.EndPhase, new object());

            result.Should().NotBeNull();
            result.State.Should().NotBeNull();
            result.GameOver.Should().BeNull();
        }

        [Fact(DisplayName = "PvP ゲームではアクション処理後に NPC 待ちが常に false になる")]
        public async Task PvpGame_NpcPendingAlwaysFalse()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);

            var result = await _svc.ProcessAction(
                game.GameID, state!.ActivePlayer, ActionType.EndPhase, new object());

            result.IsNpcPending.Should().BeFalse("PvP games never have NPC pending");
        }

        [Fact(DisplayName = "ターン切り替え時に発行される turn_start イベントの自分のターン判定が false になる")]
        public async Task TurnStartEvent_ContainsIsMyTurn()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);

            // Turn 1 Main → end_phase skips Battle (first turn), switches turn directly
            var result = await _svc.ProcessAction(game.GameID, state!.ActivePlayer, ActionType.EndPhase, new object());

            var turnStartEvent = result.Events
                .FirstOrDefault(e => e.Event.EventType == EventTypes.TurnStart);
            turnStartEvent.Should().NotBeNull("turn switch should emit a turn_start event");
            turnStartEvent!.Event.EventData.Should().BeOfType<TurnStartEventData>()
                .Which.IsMyTurn.Should().BeFalse(
                    "active player switched, so the requesting player's turn is over");
        }
    }

    [Trait("対象", "プレイヤー向けゲーム状態の取得")]
    public class GetGameStateForPlayer : Base
    {
        [Fact(DisplayName = "プレイヤー向けゲーム状態を取得すると自分ビューと相手ビューが返る")]
        public async Task ReturnsClientState()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var clientState = await _svc.GetGameStateForPlayer(game.GameID, 1);

            clientState.Should().NotBeNull();
            clientState!.GameID.Should().Be(game.GameID);
            clientState.MyView.Should().NotBeNull();
            clientState.OppView.Should().NotBeNull();
        }

        [Fact(DisplayName = "存在しないゲームのプレイヤー向け状態取得は例外になる")]
        public async Task UnknownGame_Throws()
        {
            var act = () => _svc.GetGameStateForPlayer("nonexistent", 1);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Trait("対象", "プレイヤーのターンコントロール取得")]
    public class GetTurnControlsForPlayer : Base
    {
        [Fact(DisplayName = "ターンプレイヤーはターンコントロールを取得できる")]
        public async Task ActivePlayer_ReturnsControls()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);

            var controls = await _svc.GetTurnControlsForPlayer(game.GameID, state!.ActivePlayer);

            controls.Should().NotBeNull();
            controls!.CanEndPhase.Should().BeTrue("main phase allows ending");
        }

        [Fact(DisplayName = "ターンプレイヤーでないプレイヤーのターンコントロールは null になる")]
        public async Task InactivePlayer_ReturnsNull()
        {
            var cards = MakePlayerCards();
            var game = await _svc.CreateGameFromMatch(cards, "IN-0001", "IN-0002", cards, "IN-0001", "IN-0002", DefaultPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);
            long inactivePlayerNum = state!.ActivePlayer == 1 ? 2 : 1;

            var controls = await _svc.GetTurnControlsForPlayer(game.GameID, inactivePlayerNum);

            controls.Should().BeNull();
        }

        [Fact(DisplayName = "存在しないゲームのターンコントロール取得は例外になる")]
        public async Task UnknownGame_Throws()
        {
            var act = () => _svc.GetTurnControlsForPlayer("nonexistent", 1);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}

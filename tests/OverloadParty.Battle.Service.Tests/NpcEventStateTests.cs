using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Service;

public class NpcEventStateTests
{
    /// <summary>Shared setup for NPC event-state tests (repository, NPC-capable service, and turn-driving helpers).</summary>
    public abstract class Base
    {
        protected readonly FakeGameRepository _repo = new();
        protected readonly GameService _svc;

        /// <summary>Uses the production NPC AI config directory.</summary>
        protected Base() : this(npcDataDirOverride: null)
        {
        }

        /// <summary>Uses <paramref name="npcDataDirOverride"/> in place of the production NPC AI config directory when given.</summary>
        protected Base(string? npcDataDirOverride)
        {
            var (effects, cc) = TestEffectSetup.Get();
            var engine = new GameEngine(_repo, cc, effects, new InitiativeCatalog(TestFactory.StandardInitiatives()));

            var npcDataDir = npcDataDirOverride
                ?? FindNpcDataDir()
                ?? throw new FileNotFoundException("NPC data directory not found");
            var aiConfigs = AiConfigLoader.LoadAll(npcDataDir);

            var npcRunner = new NpcRunner(engine, _repo, cc, aiConfigs, NullNpcLogger.Instance);
            _svc = new GameService(engine, _repo, cc, npcRunner, aiConfigs);
        }

        /// <summary>
        /// Repeatedly creates NPC battles until one where the player is the first
        /// active player. This guarantees the NPC has not yet taken its first turn.
        /// </summary>
        protected async Task<(Game Game, long PlayerNum)> StartGameWithPlayerFirst()
        {
            for (int i = 0; i < 20; i++)
            {
                var cards = MakePlayerCards("SH-0001");
                var game = await _svc.StartNPCBattle(cards, "IN-0001", "IN-0002", "SHE-easy", NpcPlayerSummaries);
                var state = await _repo.GetGameState(game.GameID);
                if (state!.ActivePlayer == 1)
                {
                    return (game, 1);
                }
            }
            throw new InvalidOperationException(
                "unable to land on player-first after 20 attempts (random first player)");
        }

        /// <summary>Alias for readability at call sites.</summary>
        protected Task<(Game Game, long PlayerNum)> StartGameWithNpcNext() => StartGameWithPlayerFirst();
        protected Task<(Game Game, long PlayerNum)> StartGameWithPlayerActive() => StartGameWithPlayerFirst();

        /// <summary>
        /// Issues end_phase calls until the active player changes. On turn 2+ the
        /// player goes Main→Battle→End before the turn switches.
        /// </summary>
        protected async Task<GameActionResult> EndPlayerTurn(string gameID, long playerNum)
        {
            GameActionResult result = new();
            for (int i = 0; i < 5; i++)
            {
                result = await _svc.ProcessAction(gameID, playerNum, ActionType.EndPhase, new object());
                var state = await _repo.GetGameState(gameID);
                if (state!.ActivePlayer != playerNum || result.GameOver is not null)
                {
                    return result;
                }
            }
            throw new InvalidOperationException("player turn never ended");
        }

        /// <summary>
        /// Simulates the gateway's yield loop: kicks off the NPC turn, then calls
        /// AdvanceNpcTurn repeatedly while IsNpcPending is true, aggregating events.
        /// </summary>
        /// <param name="npcModel">対戦相手となる NPC モデル ID。</param>
        protected async Task<GameActionResult> RunNpcTurn(string npcModel = "SHE-easy")
        {
            var cards = MakePlayerCards("SH-0001");
            var game = await _svc.StartNPCBattle(cards, "IN-0001", "IN-0002", npcModel, NpcPlayerSummaries);

            var state = await _repo.GetGameState(game.GameID);

            var initial = state!.ActivePlayer == 1
                ? await _svc.ProcessAction(game.GameID, 1, ActionType.EndPhase, new object())
                : await _svc.AdvanceNpcTurn(game.GameID);

            var events = new List<ActionEventWithState>(initial.Events);
            var current = initial;
            while (current.IsNpcPending && current.GameOver is null)
            {
                current = await _svc.AdvanceNpcTurn(game.GameID);
                events.AddRange(current.Events);
            }

            return new GameActionResult
            {
                GameOver = current.GameOver,
                State = current.State,
                Events = events,
                IsNpcPending = current.IsNpcPending,
            };
        }

        protected static List<DeckSnapshotCard> MakePlayerCards(string cardId)
        {
            return Enumerable.Range(0, InitialValues.DeckSize)
                .Select(_ => new DeckSnapshotCard { CardId = cardId })
                .ToList();
        }

        protected static readonly List<PlayerSummarySnapshot> NpcPlayerSummaries =
        [
            new() { PlayerNum = 1, Name = "p1", Level = 1 },
            new() { PlayerNum = 2, Name = "SHE 配達員", Level = null },
        ];

        protected static string? FindNpcDataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "OverloadParty.Battle.Npc", "Data");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>Walks up from the test binary to find this test project's own NPC AI config fixtures.</summary>
        protected static string? FindTestFixtureNpcDataDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "TestData", "npc-ai");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>No-op logger for NpcRunner so tests run without log noise.</summary>
        protected class NullNpcLogger : ILogger<NpcRunner>
        {
            public static readonly NullNpcLogger Instance = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => false;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }

    [Trait("対象", "NPC の end_phase イベントの状態")]
    public class EndPhaseEvent : Base
    {
        [Fact(DisplayName = "NPC の end_phase イベントの状態が人間プレイヤーへのターン切り替えを反映する")]
        public async Task StateReflectsTurnSwitch()
        {
            var result = await RunNpcTurn();

            // NPC のターン中、end_phase でターンが切り替わる
            var endPhaseEvents = result.Events
                .Where(e => e.Event.EventType == ActionTypes.EndPhase && e.State is not null)
                .ToList();

            foreach (var evt in endPhaseEvents)
            {
                evt.State!.IsMyTurn.Should().BeTrue(
                    "after NPC's end_phase, it should be the human player's turn");
            }
        }
    }

    [Trait("対象", "NPC ターン終了時の turn_start イベント")]
    public class TurnStartEvent : Base
    {
        [Fact(DisplayName = "ターンが自分に切り替わったとき、turn_start イベントの自分のターン判定が true になる")]
        public async Task IsMyTurn_BecomesTrue_WhenTurnSwitchesToPlayer()
        {
            var result = await RunNpcTurn();

            // RunNpcTurn loops until control returns to the human player, so the
            // last turn_start event emitted is necessarily the switch back to them.
            var turnStartEvent = result.Events
                .LastOrDefault(e => e.Event.EventType == EventTypes.TurnStart);

            turnStartEvent.Should().NotBeNull("NPC turn completion should emit a turn_start event");
            turnStartEvent!.Event.EventData.Should().BeOfType<TurnStartEventData>()
                .Which.IsMyTurn.Should().BeTrue("turn switched to the human player");
        }
    }

    [Trait("対象", "NPC の逐次アクション譲渡とカード秘匿")]
    public class YieldMode : Base
    {
        [Fact(DisplayName = "プレイヤーの end_phase 応答には NPC の play_card イベントが含まれず NPC 待ちになる")]
        public async Task ProcessAction_PlayerEndPhase_DoesNotBatchNpcEvents()
        {
            var (game, playerNum) = await StartGameWithNpcNext();

            var result = await EndPlayerTurn(game.GameID, playerNum);

            result.Events.Should().NotContain(
                e => e.Event.EventType == ActionTypes.PlayCard,
                "NPC play_card events must not be bundled into the player's ProcessAction response");
            result.IsNpcPending.Should().BeTrue(
                "after the player's end_phase the NPC is active, so the gateway needs to loop");
        }

        [Fact(DisplayName = "NPC ターンの進行は 1 回につき 1 アクションを返し、NPC ターン終了後にプレイヤーへ制御が戻る")]
        public async Task AdvanceNpcTurn_ReturnsOneActionAtATime_ThenPlayerRegainsTurn()
        {
            var (game, playerNum) = await StartGameWithNpcNext();
            await EndPlayerTurn(game.GameID, playerNum);

            int steps = 0;
            GameActionResult current;
            do
            {
                current = await _svc.AdvanceNpcTurn(game.GameID);
                // Each AdvanceNpcTurn call corresponds to one engine action,
                // which can emit at most one play_card event.
                current.Events.Count(e => e.Event.EventType == ActionTypes.PlayCard)
                    .Should().BeLessThanOrEqualTo(1,
                        "one advance call must yield at most one play_card event");
                steps++;
                steps.Should().BeLessThan(100, "guard against infinite yield loops");
            } while (current.IsNpcPending && current.GameOver is null);

            current.IsNpcPending.Should().BeFalse("NPC turn ended, control returns to the player");
            steps.Should().BeGreaterThan(1,
                "the NPC should take multiple steps (actions + end_phase) before yielding back");

            if (current.GameOver is null)
            {
                var postState = await _repo.GetGameState(game.GameID);
                postState!.ActivePlayer.Should().Be(playerNum, "control must return to the player");
            }
        }

        [Fact(DisplayName = "プレイヤーがメインフェーズでカードをプレイし手番が続くとき NPC 待ちが false になる")]
        public async Task ProcessAction_WhenNextActorIsPlayer_NpcPendingIsFalse()
        {
            var (game, playerNum) = await StartGameWithPlayerActive();

            // player plays a card; since player is still active for remaining actions
            // in Main (or until end_phase), IsNpcPending should be false.
            var hand = (await _repo.GetGameState(game.GameID))!.GetHand(playerNum);
            var first = hand[0];

            var result = await _svc.ProcessAction(
                game.GameID, playerNum,
                ActionType.PlayCard,
                new PlayCardRequest
                {
                    CardInstanceID = first.InstanceID,
                    Zone = Zones.Frontend,
                    Index = 0,
                });

            result.IsNpcPending.Should().BeFalse(
                "player is still the active player after playing a card in main phase");
        }

        [Fact(DisplayName = "プレイヤー自身の play_card イベントは cardId を秘匿しない")]
        public async Task ProcessAction_PlayerOwnPlayCard_DoesNotRedact()
        {
            var (game, playerNum) = await StartGameWithPlayerActive();

            var hand = (await _repo.GetGameState(game.GameID))!.GetHand(playerNum);
            var first = hand[0];

            var result = await _svc.ProcessAction(
                game.GameID, playerNum,
                ActionType.PlayCard,
                new PlayCardRequest
                {
                    CardInstanceID = first.InstanceID,
                    Zone = Zones.Frontend,
                    Index = 0,
                });

            var playCard = result.Events.First(e => e.Event.EventType == ActionTypes.PlayCard);
            var data = playCard.Event.EventData.Should().BeOfType<PlayCardEventData>().Subject;
            data.CardId.Should().Be(first.CardID,
                "player's own play_card must keep the real cardId (actor == viewer)");
        }
    }

    [Trait("対象", "NPC の play_card イベントにおける相手プレイヤーからのカード秘匿")]
    public class OpponentCardVisibility : Base
    {
        public OpponentCardVisibility()
            : base(FindTestFixtureNpcDataDir()
                ?? throw new FileNotFoundException("test fixture NPC data directory not found"))
        {
        }

        [Fact(DisplayName = "相手が裏向きカードのみをプレイしたとき、play_card イベントの cardId が秘匿される")]
        public async Task AdvanceNpcTurn_OpponentDeckIsAllFaceDown_RedactsEveryPlayCardId()
        {
            var result = await RunNpcTurn("TST-FaceDownDeploy");

            var playCards = result.Events
                .Where(e => e.Event.EventType == ActionTypes.PlayCard)
                .Select(e => e.Event.EventData.Should().BeOfType<PlayCardEventData>().Subject)
                .ToList();

            playCards.Should().NotBeEmpty(
                "デッキの全カードがデプロイ可能なリソースなので、NPC は自分のターンに必ず何か配置する");
            playCards.Should().OnlyContain(pc => pc.CardId == "",
                "デッキの全カードのデプロイターンが 1 以上 (裏向き) なので cardId は常に秘匿される");
        }

        [Fact(DisplayName = "相手が表向きカードのみをプレイしたとき、play_card イベントの cardId は秘匿されない")]
        public async Task AdvanceNpcTurn_OpponentDeckIsAllFaceUp_KeepsEveryPlayCardId()
        {
            var result = await RunNpcTurn("TST-FaceUpDeploy");

            var playCards = result.Events
                .Where(e => e.Event.EventType == ActionTypes.PlayCard)
                .Select(e => e.Event.EventData.Should().BeOfType<PlayCardEventData>().Subject)
                .ToList();

            playCards.Should().NotBeEmpty(
                "デッキの全カードがデプロイ可能なリソースなので、NPC は自分のターンに必ず何か配置する");
            playCards.Should().OnlyContain(pc => pc.CardId != "",
                "デッキの全カードのデプロイターンが 0 (表向き) なので cardId は秘匿されない");
        }

        [Fact(DisplayName = "相手がリアクティブカードのみをプレイしたとき、play_card イベントの cardId が秘匿される")]
        public async Task AdvanceNpcTurn_OpponentDeckIsAllReactive_RedactsEveryPlayCardId()
        {
            var result = await RunNpcTurn("TST-ReactiveDeploy");

            var playCards = result.Events
                .Where(e => e.Event.EventType == ActionTypes.PlayCard)
                .Select(e => e.Event.EventData.Should().BeOfType<PlayCardEventData>().Subject)
                .ToList();

            playCards.Should().NotBeEmpty(
                "デッキの全カードがリアクティブなので、NPC は自分のターンに必ず何か伏せる");
            playCards.Should().OnlyContain(pc => pc.CardId == "",
                "デッキの全カードがリアクティブ (常に裏向きで伏せられる) なので cardId は常に秘匿される");
        }
    }

    [Trait("対象", "NPC の手札調整アクションの組み立て")]
    public class HandAdjustmentDiscard : Base
    {
        private const string HandCardId = "SH-0001";

        private static List<UndeployedCard> MakeNpcHand(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new UndeployedCard { InstanceID = $"npc_h_{i}", CardID = HandCardId })
                .ToList();

        [Theory(DisplayName = "NPC の手札が手札上限を超えてエンドフェーズに入ると、超過枚数を捨てて手札上限枚数になる")]
        [InlineData("手札 8 枚のとき、超過 2 枚を捨てる", 8, 2)]
        [InlineData("手札 7 枚のとき、超過 1 枚を捨てる", 7, 1)]
        public async Task HandExceedsLimit_DiscardsDownToLimit(string _, int handSize, int expectedDiscardCount)
        {
            var (game, _) = await StartGameWithPlayerFirst();
            var state = await _repo.GetGameState(game.GameID);
            state!.ActivePlayer = 2;
            state.CurrentPhase = Phase.End;
            state.Player2Hand = MakeNpcHand(handSize);

            var result = await _svc.AdvanceNpcTurn(game.GameID);

            var discardEvent = result.Events.Should().ContainSingle(
                e => e.Event.EventType == ActionTypes.DiscardHand).Subject;
            var data = discardEvent.Event.EventData.Should().BeOfType<DiscardHandEventData>().Subject;
            data.DiscardedIds.Should().HaveCount(expectedDiscardCount);

            var postState = await _repo.GetGameState(game.GameID);
            postState!.Player2Hand.Should().HaveCount(BattleConstants.HandLimit);
        }

        [Fact(DisplayName = "NPC の手札が手札上限のままエンドフェーズに残る矛盾状態のとき、エラーになる")]
        public async Task HandAtLimit_EndPhase_Throws()
        {
            var (game, _) = await StartGameWithPlayerFirst();
            var state = await _repo.GetGameState(game.GameID);
            state!.ActivePlayer = 2;
            state.CurrentPhase = Phase.End;
            state.Player2Hand = MakeNpcHand(BattleConstants.HandLimit);

            var act = () => _svc.AdvanceNpcTurn(game.GameID);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no discard needed*");
        }
    }
}

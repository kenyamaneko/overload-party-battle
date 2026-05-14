using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;

namespace OverloadParty.Battle.Tests.Service;

/// <summary>
/// Verifies that each NPC action event carries the state reflecting that action's result.
/// </summary>
public class NpcEventStateTests
{
    private readonly MockGameRepository _repo = new();
    private readonly GameService _svc;
    private readonly ICardCache _cc;

    public NpcEventStateTests()
    {
        var (effects, cc) = TestEffectSetup.Get();
        _cc = cc;
        var engine = new GameEngine(_repo, cc);
        engine.SetEffectRegistry(effects);

        var npcDataDir = FindNpcDataDir()
            ?? throw new FileNotFoundException("NPC data directory not found");
        var aiConfigs = AiConfigLoader.LoadAll(npcDataDir);

        var npcRunner = new NpcRunner(engine, _repo, cc, aiConfigs, NullNpcLogger.Instance);
        _svc = new GameService(engine, _repo, cc, npcRunner, aiConfigs);
    }

    [Fact]
    public async Task NpcEndPhaseEvent_StateReflectsTurnSwitch()
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

    // ─── Yield mode (one-action-per-call) ──────────────────────

    [Fact]
    public async Task ProcessAction_PlayerEndPhase_DoesNotBatchNpcEvents()
    {
        var (game, playerNum) = await StartGameWithNpcNext();

        var result = await EndPlayerTurn(game.GameID, playerNum);

        result.Events.Should().NotContain(
            e => e.Event.EventType == ActionTypes.PlayCard,
            "NPC play_card events must not be bundled into the player's ProcessAction response");
        result.NpcPending.Should().BeTrue(
            "after the player's end_phase the NPC is active, so the gateway needs to loop");
    }

    [Fact]
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
        } while (current.NpcPending && current.GameOver is null);

        current.NpcPending.Should().BeFalse("NPC turn ended, control returns to the player");
        steps.Should().BeGreaterThan(1,
            "the NPC should take multiple steps (actions + end_phase) before yielding back");

        if (current.GameOver is null)
        {
            var postState = await _repo.GetGameState(game.GameID);
            postState!.ActivePlayer.Should().Be(playerNum, "control must return to the player");
        }
    }

    [Fact]
    public async Task ProcessAction_WhenNextActorIsPlayer_NpcPendingIsFalse()
    {
        var (game, playerNum) = await StartGameWithPlayerActive();

        // player plays a card; since player is still active for remaining actions
        // in Main (or until end_phase), NpcPending should be false.
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

        result.NpcPending.Should().BeFalse(
            "player is still the active player after playing a card in main phase");
    }

    [Fact]
    public async Task AdvanceNpcTurn_PlayCardEvent_RedactsFaceDownCardId()
    {
        // From the player's viewpoint, opponent play_card events must redact
        // the cardId whenever the card is face-down (DeployTurns>0 or Reactive);
        // face-up cards (Strategy/Attachment/deploy_turns=0 resource) keep cardId.
        var result = await RunNpcTurn();

        var playCards = result.Events
            .Where(e => e.Event.EventType == ActionTypes.PlayCard)
            .Select(e => e.Event)
            .ToList();

        playCards.Should().NotBeEmpty("NPC should play at least one card during main phase");

        bool sawRedacted = false;
        foreach (var evt in playCards)
        {
            var data = evt.EventData.Should().BeOfType<PlayCardEventData>().Subject;
            var cardId = data.CardId;
            if (cardId == "")
            {
                sawRedacted = true;
                continue;
            }
            var def = _cc.Get(cardId);
            def.Should().NotBeNull();
            (def!.CardType == CardTypes.Reactive || def.DeployTurns > 0).Should().BeFalse(
                $"face-down card {cardId} should have been redacted (type={def.CardType}, deploy_turns={def.DeployTurns})");
        }

        sawRedacted.Should().BeTrue(
            "SHE-easy deck contains cards with DeployTurns>0; at least one should have been redacted");
    }

    [Fact]
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

    // ─── Start helpers ─────────────────────────────────────────

    /// <summary>
    /// Repeatedly creates NPC battles until one where the player is the first
    /// active player. This guarantees the NPC has not yet taken its first turn.
    /// </summary>
    private async Task<(Game Game, long PlayerNum)> StartGameWithPlayerFirst()
    {
        for (int i = 0; i < 20; i++)
        {
            var cards = MakePlayerCards("SH-0001");
            var game = await _svc.StartNPCBattle(cards, "SHE-easy", NpcPlayerSummaries);
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
    private Task<(Game Game, long PlayerNum)> StartGameWithNpcNext() => StartGameWithPlayerFirst();
    private Task<(Game Game, long PlayerNum)> StartGameWithPlayerActive() => StartGameWithPlayerFirst();

    /// <summary>
    /// Issues end_phase calls until the active player changes. On turn 2+ the
    /// player goes Main→Battle→End before the turn switches.
    /// </summary>
    private async Task<GameActionResult> EndPlayerTurn(string gameID, long playerNum)
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

    // ─── Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Simulates the gateway's yield loop: kicks off the NPC turn, then calls
    /// AdvanceNpcTurn repeatedly while NpcPending is true, aggregating events.
    /// </summary>
    private async Task<GameActionResult> RunNpcTurn()
    {
        var cards = MakePlayerCards("SH-0001");
        var game = await _svc.StartNPCBattle(cards, "SHE-easy", NpcPlayerSummaries);

        var state = await _repo.GetGameState(game.GameID);

        var initial = state!.ActivePlayer == 1
            ? await _svc.ProcessAction(game.GameID, 1, ActionType.EndPhase, new object())
            : await _svc.AdvanceNpcTurn(game.GameID);

        var events = new List<ActionEventWithState>(initial.Events);
        var current = initial;
        while (current.NpcPending && current.GameOver is null)
        {
            current = await _svc.AdvanceNpcTurn(game.GameID);
            events.AddRange(current.Events);
        }

        return new GameActionResult
        {
            GameOver = current.GameOver,
            State = current.State,
            Events = events,
            NpcPending = current.NpcPending,
        };
    }

    private static List<DeckSnapshotCard> MakePlayerCards(string cardId)
    {
        return Enumerable.Range(0, InitialValues.DeckSize)
            .Select(_ => new DeckSnapshotCard { CardId = cardId })
            .ToList();
    }

    private static readonly List<PlayerSummarySnapshot> NpcPlayerSummaries =
    [
        new() { PlayerNum = 1, Name = "p1", Level = 1 },
        new() { PlayerNum = 2, Name = "SHE 配達員", Level = null },
    ];

    private static string? FindNpcDataDir()
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

    private class NullNpcLogger : ILogger<NpcRunner>
    {
        public static readonly NullNpcLogger Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}

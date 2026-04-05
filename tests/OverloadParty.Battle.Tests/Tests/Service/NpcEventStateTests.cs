using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Data.Mock;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using GD = OverloadParty.GameData;

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
    public async Task NpcPlayCardEvents_EachStateReflectsOneMoreDeploy()
    {
        var result = await RunNpcTurn();

        var playCardEvents = result.Events
            .Where(e => e.Event.EventType == ActionTypes.PlayCard && e.State is not null)
            .ToList();

        playCardEvents.Should().HaveCountGreaterThanOrEqualTo(2,
            "NPC should deploy multiple cards during main phase");

        for (int i = 1; i < playCardEvents.Count; i++)
        {
            var prev = CountOppFieldCards(playCardEvents[i - 1].State!);
            var curr = CountOppFieldCards(playCardEvents[i].State!);
            curr.Should().Be(prev + 1,
                $"play_card event {i} should reflect exactly one new deploy vs event {i - 1}");
        }
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
        var (game, playerID) = await StartGameWithNpcNext();

        var result = await EndPlayerTurn(game.GameID, playerID);

        result.Events.Should().NotContain(
            e => e.Event.EventType == ActionTypes.PlayCard,
            "NPC play_card events must not be bundled into the player's ProcessAction response");
        result.NpcPending.Should().BeTrue(
            "after the player's end_phase the NPC is active, so the gateway needs to loop");
    }

    [Fact]
    public async Task AdvanceNpcTurn_ReturnsOneActionAtATime_ThenPlayerRegainsTurn()
    {
        var (game, playerID) = await StartGameWithNpcNext();
        var playerNum = game.ResolvePlayerNum(playerID);
        await EndPlayerTurn(game.GameID, playerID);

        int steps = 0;
        GameActionResult current;
        do
        {
            current = await _svc.AdvanceNpcTurn(game.GameID, playerID);
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
        var (game, playerID) = await StartGameWithPlayerActive();

        // player plays a card; since player is still active for remaining actions
        // in Main (or until end_phase), NpcPending should be false.
        var hand = (await _repo.GetGameState(game.GameID))!.GetHand(
            game.ResolvePlayerNum(playerID));
        var first = hand[0];

        var result = await _svc.ProcessAction(
            game.GameID, playerID,
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
            var cardId = (string)evt.EventData!["cardId"];
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
        var (game, playerID) = await StartGameWithPlayerActive();

        var hand = (await _repo.GetGameState(game.GameID))!.GetHand(
            game.ResolvePlayerNum(playerID));
        var first = hand[0];

        var result = await _svc.ProcessAction(
            game.GameID, playerID,
            ActionType.PlayCard,
            new PlayCardRequest
            {
                CardInstanceID = first.InstanceID,
                Zone = Zones.Frontend,
                Index = 0,
            });

        var playCard = result.Events.First(e => e.Event.EventType == ActionTypes.PlayCard);
        var cardId = (string)playCard.Event.EventData!["cardId"];
        cardId.Should().Be(first.CardID,
            "player's own play_card must keep the real cardId (actor == viewer)");
    }

    // ─── Start helpers ─────────────────────────────────────────

    /// <summary>
    /// Repeatedly creates NPC battles until one where the player is the first
    /// active player. This guarantees the NPC has not yet taken its first turn.
    /// </summary>
    private async Task<(Game Game, string PlayerID)> StartGameWithPlayerFirst()
    {
        for (int i = 0; i < 20; i++)
        {
            var cards = MakePlayerCards("SH-0001");
            var game = await _svc.StartNPCBattle("player1", 1, cards, "SHE-easy");
            var state = await _repo.GetGameState(game.GameID);
            if (state!.ActivePlayer == 1)
            {
                return (game, "player1");
            }
        }
        throw new InvalidOperationException(
            "unable to land on player-first after 20 attempts (random first player)");
    }

    /// <summary>Alias for readability at call sites.</summary>
    private Task<(Game Game, string PlayerID)> StartGameWithNpcNext() => StartGameWithPlayerFirst();
    private Task<(Game Game, string PlayerID)> StartGameWithPlayerActive() => StartGameWithPlayerFirst();

    /// <summary>
    /// Issues end_phase calls until the active player changes. On turn 2+ the
    /// player goes Main→Battle→End before the turn switches.
    /// </summary>
    private async Task<GameActionResult> EndPlayerTurn(string gameID, string playerID)
    {
        var game = await _repo.GetGame(gameID);
        var playerNum = game!.ResolvePlayerNum(playerID);
        GameActionResult result = new();
        for (int i = 0; i < 5; i++)
        {
            result = await _svc.ProcessAction(gameID, playerID, ActionType.EndPhase, new object());
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
        var game = await _svc.StartNPCBattle("player1", 1, cards, "SHE-easy");

        var state = await _repo.GetGameState(game.GameID);

        var initial = state!.ActivePlayer == 1
            ? await _svc.ProcessAction(game.GameID, "player1", ActionType.EndPhase, new object())
            : await _svc.AdvanceNpcTurn(game.GameID, "player1");

        var events = new List<ActionEventWithState>(initial.Events);
        var current = initial;
        while (current.NpcPending && current.GameOver is null)
        {
            current = await _svc.AdvanceNpcTurn(game.GameID, "player1");
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

    private static int CountOppFieldCards(GD.ClientGameState state)
    {
        var field = state.OppView.Field;
        return field.Frontend.Count(r => r is not null)
             + field.Backend.Count(r => r is not null)
             + field.Support.Count(s => s is not null);
    }

    private static List<DeckSnapshotCard> MakePlayerCards(string cardId)
    {
        return Enumerable.Range(0, GameConstants.DeckSize)
            .Select(_ => new DeckSnapshotCard { CardId = cardId })
            .ToList();
    }

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

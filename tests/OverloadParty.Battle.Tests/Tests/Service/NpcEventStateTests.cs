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

    public NpcEventStateTests()
    {
        var (effects, cc) = TestEffectSetup.Get();
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

    // ─── Helpers ────────────────────────────────────────────────

    private async Task<GameActionResult> RunNpcTurn()
    {
        var cards = MakePlayerCards("SH-0001");
        var game = await _svc.StartNPCBattle("player1", 1, cards, "SHE-easy");

        var state = await _repo.GetGameState(game.GameID);

        if (state!.ActivePlayer == 1)
        {
            return await _svc.ProcessAction(
                game.GameID, "player1", ActionType.EndPhase, new object());
        }
        return await _svc.AdvanceNpcTurn(game.GameID, "player1");
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

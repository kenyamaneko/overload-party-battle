using System.Text;
using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Service;

/// <summary>
/// Builds human-readable game logs (replay) from persisted game events.
/// </summary>
public class GameLogService
{
    private readonly IGameRepository _gameRepo;
    private readonly ICardCache _cardCache;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public GameLogService(IGameRepository gameRepo, ICardCache cardCache)
    {
        _gameRepo = gameRepo;
        _cardCache = cardCache;
    }

    // ─── JSON log ────────────────────────────────────────────────

    public async Task<GameLogResponse?> GetGameLog(string gameID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null) return null;

        var state = await _gameRepo.GetGameState(gameID, ct);
        var events = await _gameRepo.GetEvents(gameID, ct);

        var isNpc = NpcConstants.IsNpcPlayer(game.Player1ID) || NpcConstants.IsNpcPlayer(game.Player2ID);
        var durationSecs = game.FinishedAt.HasValue
            ? (long)(game.FinishedAt.Value - game.CreatedAt).TotalSeconds
            : (long?)null;

        var winnerLabel = game.WinnerID switch
        {
            null or "" => null,
            _ when game.WinnerID == game.Player1ID => "player1",
            _ => "player2",
        };

        var winReason = FindWinReason(events);

        var entries = events
            .Select(e => new GameLogEntry
            {
                Seq = e.SequenceNumber,
                EventType = e.EventType,
                Description = EventToDescription(e, game),
            })
            .ToList();

        return new GameLogResponse
        {
            GameId = gameID,
            Player1Id = game.Player1ID,
            Player2Id = game.Player2ID,
            Winner = winnerLabel,
            WinReason = winReason,
            TotalTurns = state?.CurrentTurn ?? 0,
            DurationSeconds = durationSecs,
            FinalBudget = state is not null
                ? new FinalBudgetInfo { Player1 = state.Player1Budget, Player2 = state.Player2Budget }
                : null,
            Entries = entries,
        };
    }

    // ─── Text log ────────────────────────────────────────────────

    public async Task<string?> GetGameLogText(string gameID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null) return null;

        var state = await _gameRepo.GetGameState(gameID, ct);
        var events = await _gameRepo.GetEvents(gameID, ct);

        var sb = new StringBuilder();

        // Header
        sb.AppendLine($"=== Game {gameID} ===");

        var p1Label = NpcConstants.IsNpcPlayer(game.Player1ID)
            ? $"{game.Player1ID} (NPC)"
            : game.Player1ID;
        var p2Label = NpcConstants.IsNpcPlayer(game.Player2ID)
            ? $"{game.Player2ID} (NPC)"
            : game.Player2ID;
        sb.AppendLine($"P1: {p1Label}  vs  P2: {p2Label}");

        // Winner / duration
        var winReason = FindWinReason(events);
        var winnerTag = game.WinnerID switch
        {
            null or "" => winReason == "draw" ? "Draw" : "N/A",
            _ when game.WinnerID == game.Player1ID => $"P1 ({winReason})",
            _ => $"P2 ({winReason})",
        };

        var turns = state?.CurrentTurn ?? 0;
        var durationStr = game.FinishedAt.HasValue
            ? FormatDuration(game.FinishedAt.Value - game.CreatedAt)
            : "in progress";
        sb.AppendLine($"Winner: {winnerTag} | {turns} turns | {durationStr}");

        if (state is not null)
        {
            sb.AppendLine($"Final Budget: P1={state.Player1Budget}  P2={state.Player2Budget}");
        }

        sb.AppendLine();

        // Events
        foreach (var e in events)
        {
            var desc = EventToDescription(e, game);
            sb.AppendLine($"[{e.SequenceNumber}] {desc}");
        }

        return sb.ToString();
    }

    // ─── Serialization helpers ───────────────────────────────────

    public byte[] SerializeToJson(GameLogResponse log)
    {
        return JsonSerializer.SerializeToUtf8Bytes(log, JsonOpts);
    }

    // ─── Event description conversion ────────────────────────────

    private string EventToDescription(GameEvent evt, Game game)
    {
        var playerTag = PlayerTag(evt.PlayerID, game);
        var data = evt.EventData;

        return evt.EventType switch
        {
            WireActionTypes.PlayCard => DescribePlayCard(playerTag, data),
            WireActionTypes.AttachCard => DescribeAttachCard(playerTag, data),
            WireActionTypes.Attack => DescribeAttack(playerTag, data),
            WireActionTypes.ScaleUp => DescribeScaleUp(playerTag, data),
            WireActionTypes.Monetize => DescribeMonetize(playerTag, data),
            WireActionTypes.DiscardHand => DescribeDiscardHand(playerTag, data),
            WireActionTypes.ActivateEffect => DescribeActivateEffect(playerTag, data),
            "reactive_revealed" => $"{playerTag} reactive revealed",
            WireActionTypes.Migrate => DescribeMigrate(playerTag, data),
            "migration_complete" => "Migration complete",
            WireActionTypes.PhaseChange => DescribePhaseChange(playerTag, data),
            WireActionTypes.PhaseEnd => DescribePhaseEnd(data),
            WireActionTypes.TurnEnd => DescribeTurnEnd(data),
            "game_over" => DescribeGameOver(game),
            _ => $"{playerTag} {evt.EventType}",
        };
    }

    private string DescribePlayCard(string player, Dictionary<string, object>? data)
    {
        if (data is null) return $"{player} played a card";

        var cardId = GetLong(data, "cardId");
        var zone = GetString(data, "zone");
        var cancelled = GetBool(data, "cancelled");
        var cardName = ResolveCardName(cardId);

        if (cancelled)
            return $"{player} deploy of \"{cardName}\" was cancelled";

        var card = cardId.HasValue ? _cardCache.Get(cardId.Value) : null;
        var cost = card?.MaintenanceCost ?? 0;
        var costStr = cost > 0 ? $" [-{cost} Budget]" : "";
        return $"{player} deployed \"{cardName}\" to {CapitalizeFirst(zone)}{costStr}";
    }

    private string DescribeAttachCard(string player, Dictionary<string, object>? data)
    {
        if (data is null) return $"{player} attached a card";

        var cardId = GetLong(data, "cardId");
        var cardName = ResolveCardName(cardId);
        return $"{player} attached \"{cardName}\"";
    }

    private string DescribeAttack(string player, Dictionary<string, object>? data)
    {
        if (data is null) return $"{player} attacked";

        var damage = GetLong(data, "damage") ?? 0;
        var destroyed = GetBool(data, "destroyed");
        var slaPenalty = GetLong(data, "slaPenalty");
        var cancelled = GetBool(data, "cancelled");

        if (cancelled)
            return $"{player} attack was cancelled";

        var parts = new List<string> { $"{player} attacked for {damage} damage" };
        if (destroyed) parts.Add("(destroyed)");
        if (slaPenalty.HasValue && slaPenalty.Value > 0) parts.Add($"[SLA -{slaPenalty.Value}]");
        return string.Join(" ", parts);
    }

    private string DescribeScaleUp(string player, Dictionary<string, object>? data)
    {
        if (data is null) return $"{player} scaled up";

        var targetRank = GetString(data, "targetRank") ?? "?";
        var family = GetString(data, "instanceFamily");
        var familyStr = family is not null ? $" {family}" : "";
        return $"{player} scaled up → {CapitalizeFirst(targetRank)}{familyStr}";
    }

    private string DescribeMonetize(string player, Dictionary<string, object>? data)
    {
        var amount = GetLong(data, "totalAmount") ?? 0;
        return $"{player} distributed {amount} Yield";
    }

    private string DescribeDiscardHand(string player, Dictionary<string, object>? data)
    {
        var count = GetLong(data, "discardedCount") ?? 0;
        return $"{player} discarded {count} card{(count != 1 ? "s" : "")}";
    }

    private string DescribeActivateEffect(string player, Dictionary<string, object>? data)
    {
        if (data is null) return $"{player} activated an effect";

        var cardNo = GetLong(data, "cardNo");
        var cardName = ResolveCardName(cardNo);
        return $"{player} activated effect: {cardName}";
    }

    private string DescribeMigrate(string player, Dictionary<string, object>? data)
    {
        if (data is null) return $"{player} migrated";

        var srcCardId = GetLong(data, "sourceCardId");
        var tgtCardId = GetLong(data, "targetCardId");
        var srcName = ResolveCardName(srcCardId);
        var tgtName = ResolveCardName(tgtCardId);
        return $"{player} migrated \"{srcName}\" → \"{tgtName}\"";
    }

    private static string DescribePhaseChange(string player, Dictionary<string, object>? data)
    {
        var prev = GetString(data, "previousPhase") ?? "?";
        var curr = GetString(data, "currentPhase") ?? "?";
        return $"{player} ended {CapitalizeFirst(prev)} → {CapitalizeFirst(curr)}";
    }

    private static string DescribePhaseEnd(Dictionary<string, object>? data)
    {
        var phase = GetString(data, "phase") ?? "?";
        var needsDiscard = GetBool(data, "needsDiscard");
        var suffix = needsDiscard ? " (discard required)" : "";
        return $"{CapitalizeFirst(phase)} phase ended{suffix}";
    }

    private static string DescribeTurnEnd(Dictionary<string, object>? data)
    {
        var nextTurn = GetLong(data, "nextTurn") ?? 0;
        var activePlayer = GetLong(data, "activePlayer") ?? 0;
        return $"Turn end → Turn {nextTurn} (P{activePlayer})";
    }

    private string DescribeGameOver(Game game)
    {
        var winReason = game.WinnerID switch
        {
            null or "" => "Draw",
            _ => "",
        };

        if (string.IsNullOrEmpty(winReason))
        {
            var winner = game.WinnerID == game.Player1ID ? "P1" : "P2";
            return $"Game over: {winner} wins";
        }

        return $"Game over: {winReason}";
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private static string PlayerTag(string? playerID, Game game)
    {
        if (string.IsNullOrEmpty(playerID)) return "System";
        if (playerID == game.Player1ID) return "P1";
        if (playerID == game.Player2ID) return "P2";
        return "??";
    }

    private string ResolveCardName(long? cardNo)
    {
        if (cardNo is null) return "???";
        var card = _cardCache.Get(cardNo.Value);
        return card?.CardName ?? $"Card#{cardNo}";
    }

    private static string? FindWinReason(List<GameEvent> events)
    {
        var gameOverEvent = events.LastOrDefault(e => e.EventType == "game_over");
        if (gameOverEvent?.EventData is not null)
        {
            var reason = GetString(gameOverEvent.EventData, "winReason")
                      ?? GetString(gameOverEvent.EventData, "reason");
            if (reason is not null) return reason;
        }
        return null;
    }

    private static string CapitalizeFirst(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return char.ToUpper(s[0]) + s[1..];
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h{ts.Minutes:D2}m{ts.Seconds:D2}s";
        return $"{ts.Minutes}m{ts.Seconds:D2}s";
    }

    // ─── EventData value extraction ──────────────────────────────

    private static string? GetString(Dictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var val)) return null;
        return val switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => val.ToString(),
        };
    }

    private static long? GetLong(Dictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var val)) return null;
        return val switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt64(),
            _ when long.TryParse(val.ToString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool GetBool(Dictionary<string, object>? data, string key)
    {
        if (data is null || !data.TryGetValue(key, out var val)) return false;
        return val switch
        {
            bool b => b,
            JsonElement je when je.ValueKind == JsonValueKind.True => true,
            JsonElement je when je.ValueKind == JsonValueKind.False => false,
            _ when bool.TryParse(val.ToString(), out var parsed) => parsed,
            _ => false,
        };
    }
}

// ─── Response DTOs ───────────────────────────────────────────────

public class GameLogResponse
{
    public string GameId { get; init; } = "";
    public string Player1Id { get; init; } = "";
    public string Player2Id { get; init; } = "";
    public string? Winner { get; init; }
    public string? WinReason { get; init; }
    public long TotalTurns { get; init; }
    public long? DurationSeconds { get; init; }
    public FinalBudgetInfo? FinalBudget { get; init; }
    public List<GameLogEntry> Entries { get; init; } = [];
}

public class FinalBudgetInfo
{
    public long Player1 { get; init; }
    public long Player2 { get; init; }
}

public class GameLogEntry
{
    public long Seq { get; init; }
    public string EventType { get; init; } = "";
    public string Description { get; init; } = "";
}

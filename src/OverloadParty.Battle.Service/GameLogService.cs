using System.Text;
using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;


namespace OverloadParty.Battle.Service;

/// <summary>
/// GameLogService は永続化されたゲームイベントから人間可読なゲームログ（リプレイ）を構築します
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

    /// <summary>指定 Game の構造化ゲームログを返す。</summary>
    /// <param name="gameID">対象 Game の ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>Game が存在しない場合は null、それ以外は GameLogResponse。</returns>
    public async Task<GameLogResponse?> GetGameLog(string gameID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null)
        {
            return null;
        }

        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found");
        var events = await _gameRepo.GetEvents(gameID, ct);

        var durationSecs = game.FinishedAt.HasValue
            ? (long)(game.FinishedAt.Value - game.CreatedAt).TotalSeconds
            : (long?)null;

        var winnerLabel = game.WinningPlayerNum switch
        {
            null or 0 => null,
            1 => "player1",
            2 => "player2",
            var n => throw new InvalidOperationException($"Invalid WinningPlayerNum: {n}"),
        };

        var winReason = game.WinReason ?? FindWinReasonFromEvents(events);

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
            Winner = winnerLabel,
            WinReason = winReason,
            TotalTurns = state.CurrentTurn,
            DurationSeconds = durationSecs,
            FinalBudget = new FinalBudgetInfo { Player1 = state.Player1Budget, Player2 = state.Player2Budget },
            Entries = entries,
        };
    }

    // ─── Text log ────────────────────────────────────────────────

    /// <summary>指定 Game の人間可読なテキスト形式ゲームログを返す。</summary>
    /// <param name="gameID">対象 Game の ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>Game が存在しない場合は null、それ以外はテキスト形式のログ。</returns>
    public async Task<string?> GetGameLogText(string gameID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct);
        if (game is null)
        {
            return null;
        }

        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found");
        var events = await _gameRepo.GetEvents(gameID, ct);

        var sb = new StringBuilder();

        // ヘッダー
        sb.AppendLine($"=== Game {gameID} ===");

        var p1Label = game.Npc1Model is not null
            ? $"NPC ({game.Npc1Model})"
            : "P1";
        var p2Label = game.Npc2Model is not null
            ? $"NPC ({game.Npc2Model})"
            : "P2";
        sb.AppendLine($"P1: {p1Label}  vs  P2: {p2Label}");

        // 勝者/所要時間
        var winReason = game.WinReason ?? FindWinReasonFromEvents(events);
        var winnerTag = game.WinningPlayerNum switch
        {
            null => "N/A",
            0 => "Draw",
            1 => $"P1 ({winReason})",
            2 => $"P2 ({winReason})",
            var n => throw new InvalidOperationException($"Invalid WinningPlayerNum: {n}"),
        };

        var turns = state.CurrentTurn;
        var durationStr = game.FinishedAt.HasValue
            ? FormatDuration(game.FinishedAt.Value - game.CreatedAt)
            : "in progress";
        sb.AppendLine($"Winner: {winnerTag} | {turns} turns | {durationStr}");

        sb.AppendLine($"Final Budget: P1={state.Player1Budget}  P2={state.Player2Budget}");

        sb.AppendLine();

        // イベント一覧
        foreach (var e in events)
        {
            var desc = EventToDescription(e, game);
            sb.AppendLine($"[{e.SequenceNumber}] {desc}");
        }

        return sb.ToString();
    }

    // ─── Serialization helpers ───────────────────────────────────

    /// <summary>GameLogResponse を JSON バイト列にシリアライズする。</summary>
    /// <param name="log">シリアライズ対象のゲームログ。</param>
    /// <returns>UTF-8 JSON バイト列。</returns>
    public byte[] SerializeToJson(GameLogResponse log)
    {
        return JsonSerializer.SerializeToUtf8Bytes(log, JsonOpts);
    }

    // ─── Event description conversion ────────────────────────────

    private string EventToDescription(GameEvent evt, Game game)
    {
        var playerTag = FormatPlayerTag(evt);

        return evt.EventData switch
        {
            PlayCardEventData d => DescribePlayCard(playerTag, d),
            AttachCardEventData d => DescribeAttachCard(playerTag, d),
            AttackEventData d => DescribeAttack(playerTag, d),
            ScaleUpEventData d => DescribeScaleUp(playerTag, d),
            MonetizeEventData d => DescribeMonetize(playerTag, d),
            DiscardHandEventData d => DescribeDiscardHand(playerTag, d),
            UseEffectEventData d => DescribeUseEffect(playerTag, d),
            PhaseChangeEventData d => DescribePhaseChange(playerTag, d),
            PhaseEndEventData d => DescribePhaseEnd(d),
            TurnEndEventData d => DescribeTurnEnd(d),
            TurnStartInternalEventData d => DescribeTurnStart(d),
            ReactiveRevealedEventData => $"{playerTag} reactive revealed",
            GameOverEventData => DescribeGameOver(game),
            BattleStartEventData => $"{playerTag} battle start",
            SelectSlotEventData d => DescribeSelectSlot(playerTag, d),
            null => throw new InvalidOperationException(
                $"GameEvent {evt.SequenceNumber} (type={evt.EventType}) has null EventData"),
            _ => throw new InvalidOperationException(
                $"Unknown IEventData runtime type: {evt.EventData.GetType().Name}"),
        };
    }

    private string DescribePlayCard(string player, PlayCardEventData d)
    {
        var cardName = ResolveCardName(d.CardId);

        if (d.Cancelled == true)
        {
            return $"{player} deploy of \"{cardName}\" was cancelled";
        }

        var card = !string.IsNullOrEmpty(d.CardId) ? _cardCache.Get(d.CardId) : null;
        var cost = card?.MaintenanceCost ?? 0;
        var costStr = cost > 0 ? $" [-{cost} Budget]" : "";
        return $"{player} deployed \"{cardName}\" to {CapitalizeFirst(d.Zone)}{costStr}";
    }

    private string DescribeAttachCard(string player, AttachCardEventData d)
    {
        var cardName = ResolveCardName(d.CardId);
        return $"{player} attached \"{cardName}\"";
    }

    private static string DescribeAttack(string player, AttackEventData d)
    {
        if (d.Cancelled == true)
        {
            return $"{player} attack was cancelled";
        }

        var parts = new List<string> { $"{player} attacked for {d.Damage} damage" };
        if (d.Destroyed)
        {
            parts.Add("(destroyed)");
        }

        if (d.SlaPenalty is { } sla && sla > 0)
        {
            parts.Add($"[SLA -{sla}]");
        }

        return string.Join(" ", parts);
    }

    private static string DescribeScaleUp(string player, ScaleUpEventData d)
    {
        var familyStr = d.InstanceFamily is not null ? $" {d.InstanceFamily}" : "";
        return $"{player} scaled up → {CapitalizeFirst(d.TargetRank)}{familyStr}";
    }

    private static string DescribeMonetize(string player, MonetizeEventData d) =>
        $"{player} distributed {d.TotalAmount} Yield";

    private static string DescribeDiscardHand(string player, DiscardHandEventData d) =>
        $"{player} discarded {d.DiscardedCount} card{(d.DiscardedCount != 1 ? "s" : "")}";

    private string DescribeUseEffect(string player, UseEffectEventData d)
    {
        var cardName = ResolveCardName(d.CardId);
        return $"{player} activated effect: {cardName}";
    }

    private static string DescribePhaseChange(string player, PhaseChangeEventData d) =>
        $"{player} ended {CapitalizeFirst(d.PreviousPhase)} → {CapitalizeFirst(d.CurrentPhase)}";

    private static string DescribePhaseEnd(PhaseEndEventData d)
    {
        var suffix = d.NeedsDiscard ? " (discard required)" : "";
        return $"{CapitalizeFirst(d.Phase)} phase ended{suffix}";
    }

    private static string DescribeTurnEnd(TurnEndEventData d) =>
        $"Turn end → Turn {d.NextTurn} (P{d.ActivePlayer})";

    private static string DescribeTurnStart(TurnStartInternalEventData d) =>
        $"Turn start → Turn {d.Turn} (P{d.ActivePlayer})";

    private string DescribeSelectSlot(string player, SelectSlotEventData d)
    {
        var cardName = ResolveCardName(d.CardId);
        return $"{player} selected slot for \"{cardName}\" at {CapitalizeFirst(d.Zone)}[{d.Index}]";
    }

    private string DescribeGameOver(Game game)
    {
        return game.WinningPlayerNum switch
        {
            null or 0 => "Game over: Draw",
            1 => "Game over: P1 wins",
            2 => "Game over: P2 wins",
            var n => throw new InvalidOperationException($"Invalid WinningPlayerNum: {n}"),
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private static string FormatPlayerTag(GameEvent evt) => evt.PlayerNum switch
    {
        null => "System",
        1 => "P1",
        2 => "P2",
        _ => "??",
    };

    private string ResolveCardName(string? cardId)
    {
        if (string.IsNullOrEmpty(cardId))
        {
            return "???";
        }

        var card = _cardCache.Get(cardId);
        return card?.CardName ?? $"Card#{cardId}";
    }

    private static string? FindWinReasonFromEvents(List<GameEvent> events)
    {
        var gameOverEvent = events.LastOrDefault(e => e.EventType == EventTypes.GameOver);
        return gameOverEvent?.EventData is GameOverEventData go ? go.WinReason : null;
    }

    private static string CapitalizeFirst(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        return char.ToUpper(s[0]) + s[1..];
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h{ts.Minutes:D2}m{ts.Seconds:D2}s";
        }

        return $"{ts.Minutes}m{ts.Seconds:D2}s";
    }
}

// ─── Response DTOs ───────────────────────────────────────────────

public class GameLogResponse
{
    public string GameId { get; init; } = "";
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

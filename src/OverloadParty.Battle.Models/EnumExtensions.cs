namespace OverloadParty.Battle.Models;

/// <summary>
/// String conversion helpers for enums. These produce the snake_case / original-case
/// values used in JSON and DB, keeping Models free of any JSON serializer dependency.
/// </summary>
public static class EnumExtensions
{
    // ─── Phase ──────────────────────────────────────────────

    public static string ToWireString(this Phase phase) => phase switch
    {
        Phase.Draw => "draw",
        Phase.Main => "main",
        Phase.Battle => "battle",
        Phase.End => "end",
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static Phase ParsePhase(string s) => s switch
    {
        "draw" => Phase.Draw,
        "main" => Phase.Main,
        "battle" => Phase.Battle,
        "end" => Phase.End,
        _ => throw new ArgumentException($"Unknown phase: {s}")
    };

    // ─── Rank ───────────────────────────────────────────────

    public static string ToWireString(this Rank rank) => rank switch
    {
        Rank.Small => "small",
        Rank.Medium => "medium",
        Rank.Large => "large",
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };

    public static Rank ParseRank(string s) => s switch
    {
        "small" => Rank.Small,
        "medium" => Rank.Medium,
        "large" => Rank.Large,
        _ => throw new ArgumentException($"Unknown rank: {s}")
    };

    // ─── InstanceFamily ─────────────────────────────────────

    public static string ToWireString(this InstanceFamily family) => family switch
    {
        InstanceFamily.M => "M",
        InstanceFamily.C => "C",
        InstanceFamily.R => "R",
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    public static InstanceFamily ParseInstanceFamily(string s) => s switch
    {
        "M" => InstanceFamily.M,
        "C" => InstanceFamily.C,
        "R" => InstanceFamily.R,
        _ => throw new ArgumentException($"Unknown instance family: {s}")
    };

    // ─── GameStatus ─────────────────────────────────────────

    public static string ToWireString(this GameStatus status) => status switch
    {
        GameStatus.Waiting => "waiting",
        GameStatus.Playing => "playing",
        GameStatus.Finished => "finished",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static GameStatus ParseGameStatus(string s) => s switch
    {
        "waiting" => GameStatus.Waiting,
        "playing" => GameStatus.Playing,
        "finished" => GameStatus.Finished,
        _ => throw new ArgumentException($"Unknown game status: {s}")
    };

    // ─── WinReason ──────────────────────────────────────────

    public static string ToWireString(this WinReason reason) => reason switch
    {
        WinReason.BudgetZero => "budget_zero",
        WinReason.SystemDown => "system_down",
        WinReason.RepositoryOut => "repository_out",
        WinReason.Timeout => "timeout",
        WinReason.Disconnect => "disconnect",
        WinReason.TurnLimit => "turn_limit",
        WinReason.Draw => "draw",
        WinReason.LaunchFailure => "launch_failure",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    public static WinReason ParseWinReason(string s) => s switch
    {
        "budget_zero" => WinReason.BudgetZero,
        "system_down" => WinReason.SystemDown,
        "repository_out" => WinReason.RepositoryOut,
        "timeout" => WinReason.Timeout,
        "disconnect" => WinReason.Disconnect,
        "turn_limit" => WinReason.TurnLimit,
        "draw" => WinReason.Draw,
        "launch_failure" => WinReason.LaunchFailure,
        _ => throw new ArgumentException($"Unknown win reason: {s}")
    };

    // ─── ActionType ─────────────────────────────────────────

    public static string ToWireString(this ActionType action) => action switch
    {
        ActionType.PlayCard => WireActionTypes.PlayCard,
        ActionType.Attack => WireActionTypes.Attack,
        ActionType.ScaleUp => WireActionTypes.ScaleUp,
        ActionType.DistributeYield => WireActionTypes.DistributeYield,
        ActionType.DiscardHand => WireActionTypes.DiscardHand,
        ActionType.ActivateEffect => WireActionTypes.ActivateEffect,
        ActionType.SetReactive => WireActionTypes.SetReactive,
        ActionType.Migrate => WireActionTypes.Migrate,
        ActionType.EndPhase => WireActionTypes.EndPhase,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static ActionType ParseActionType(string s) => s switch
    {
        WireActionTypes.PlayCard => ActionType.PlayCard,
        WireActionTypes.Attack => ActionType.Attack,
        WireActionTypes.ScaleUp => ActionType.ScaleUp,
        WireActionTypes.DistributeYield => ActionType.DistributeYield,
        WireActionTypes.DiscardHand => ActionType.DiscardHand,
        WireActionTypes.ActivateEffect => ActionType.ActivateEffect,
        WireActionTypes.SetReactive => ActionType.SetReactive,
        WireActionTypes.Migrate => ActionType.Migrate,
        WireActionTypes.EndPhase => ActionType.EndPhase,
        _ => throw new ArgumentException($"Unknown action type: {s}")
    };

    // ─── Zone ───────────────────────────────────────────────

    public static string ToWireString(this Zone zone) => zone switch
    {
        Zone.Frontend => "frontend",
        Zone.Backend => "backend",
        Zone.Support => "support",
        _ => throw new ArgumentOutOfRangeException(nameof(zone))
    };

    public static Zone ParseZone(string s) => s switch
    {
        "frontend" => Zone.Frontend,
        "backend" => Zone.Backend,
        "support" => Zone.Support,
        _ => throw new ArgumentException($"Unknown zone: {s}")
    };

    // ─── CardType helpers ───────────────────────────────────

    public static CardTypeCategory GetCategory(string cardType) => cardType switch
    {
        CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl => CardTypeCategory.Compute,
        CardTypes.Database or CardTypes.ObjectStorage or CardTypes.CacheDB => CardTypeCategory.Data,
        CardTypes.Platform or CardTypes.Attachment or CardTypes.Strategy or CardTypes.Reactive or CardTypes.Incident => CardTypeCategory.Support,
        _ => throw new ArgumentException($"Unknown card type: {cardType}")
    };

    // ─── Faction ────────────────────────────────────────────

    public static Faction ParseFaction(string s) => s switch
    {
        "SD" => Faction.SD,
        "Tenki" => Faction.Tenki,
        "Sugar" => Faction.Sugar,
        "Tuners" => Faction.Tuners,
        "Neutral" or "" => Faction.Neutral,
        _ => throw new ArgumentException($"Unknown faction: {s}")
    };

    public static string ToWireString(this Faction faction) => faction switch
    {
        Faction.SD => "SD",
        Faction.Tenki => "Tenki",
        Faction.Sugar => "Sugar",
        Faction.Tuners => "Tuners",
        Faction.Neutral => "Neutral",
        _ => throw new ArgumentOutOfRangeException(nameof(faction))
    };
}

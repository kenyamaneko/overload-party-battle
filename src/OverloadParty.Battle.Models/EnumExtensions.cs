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
        Phase.Draw => Phases.Draw,
        Phase.Main => Phases.Main,
        Phase.Battle => Phases.Battle,
        Phase.End => Phases.End,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static Phase ParsePhase(string s) => s switch
    {
        Phases.Draw => Phase.Draw,
        Phases.Main => Phase.Main,
        Phases.Battle => Phase.Battle,
        Phases.End => Phase.End,
        _ => throw new ArgumentException($"Unknown phase: {s}")
    };

    // ─── Rank ───────────────────────────────────────────────

    public static string ToWireString(this Rank rank) => rank switch
    {
        Rank.Small => Ranks.Small,
        Rank.Medium => Ranks.Medium,
        Rank.Large => Ranks.Large,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };

    public static Rank ParseRank(string s) => s switch
    {
        Ranks.Small => Rank.Small,
        Ranks.Medium => Rank.Medium,
        Ranks.Large => Rank.Large,
        _ => throw new ArgumentException($"Unknown rank: {s}")
    };

    // ─── InstanceFamily ─────────────────────────────────────

    public static string ToWireString(this InstanceFamily family) => family switch
    {
        InstanceFamily.M => InstanceFamilies.M,
        InstanceFamily.C => InstanceFamilies.C,
        InstanceFamily.R => InstanceFamilies.R,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    public static InstanceFamily ParseInstanceFamily(string s) => s switch
    {
        InstanceFamilies.M => InstanceFamily.M,
        InstanceFamilies.C => InstanceFamily.C,
        InstanceFamilies.R => InstanceFamily.R,
        _ => throw new ArgumentException($"Unknown instance family: {s}")
    };

    // ─── GameStatus ─────────────────────────────────────────

    public static string ToWireString(this GameStatus status) => status switch
    {
        GameStatus.Playing => OverloadParty.GameData.GameStatus.Playing,
        GameStatus.Finished => OverloadParty.GameData.GameStatus.Finished,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static GameStatus ParseGameStatus(string s) => s switch
    {
        OverloadParty.GameData.GameStatus.Playing => GameStatus.Playing,
        OverloadParty.GameData.GameStatus.Finished => GameStatus.Finished,
        _ => throw new ArgumentException($"Unknown game status: {s}")
    };

    // ─── WinReason ──────────────────────────────────────────

    public static string ToWireString(this WinReason reason) => reason switch
    {
        WinReason.BudgetZero => WinReasons.BudgetZero,
        WinReason.SystemDown => WinReasons.SystemDown,
        WinReason.RepositoryOut => WinReasons.RepositoryOut,
        WinReason.TurnTimeout => WinReasons.TurnTimeout,
        WinReason.Disconnect => WinReasons.Disconnect,
        WinReason.TurnLimit => WinReasons.TurnLimit,
        WinReason.Draw => WinReasons.Draw,
        WinReason.LaunchFailure => WinReasons.LaunchFailure,
        WinReason.Surrender => WinReasons.Surrender,
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    public static WinReason ParseWinReason(string s) => s switch
    {
        WinReasons.BudgetZero => WinReason.BudgetZero,
        WinReasons.SystemDown => WinReason.SystemDown,
        WinReasons.RepositoryOut => WinReason.RepositoryOut,
        WinReasons.TurnTimeout => WinReason.TurnTimeout,
        WinReasons.Disconnect => WinReason.Disconnect,
        WinReasons.TurnLimit => WinReason.TurnLimit,
        WinReasons.Draw => WinReason.Draw,
        WinReasons.LaunchFailure => WinReason.LaunchFailure,
        WinReasons.Surrender => WinReason.Surrender,
        _ => throw new ArgumentException($"Unknown win reason: {s}")
    };

    // ─── ActionType ─────────────────────────────────────────

    public static string ToWireString(this ActionType action) => action switch
    {
        ActionType.PlayCard => WireActionTypes.PlayCard,
        ActionType.Attack => WireActionTypes.Attack,
        ActionType.ScaleUp => WireActionTypes.ScaleUp,
        ActionType.Monetize => WireActionTypes.Monetize,
        ActionType.DiscardHand => WireActionTypes.DiscardHand,
        ActionType.UseEffect => WireActionTypes.UseEffect,
        ActionType.SetReactive => WireActionTypes.SetReactive,
        ActionType.Migrate => WireActionTypes.Migrate,
        ActionType.EndPhase => WireActionTypes.EndPhase,
        ActionType.Forfeit => WireActionTypes.Forfeit,
        ActionType.SelectSlot => WireActionTypes.SelectSlot,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    public static ActionType ParseActionType(string s) => s switch
    {
        WireActionTypes.PlayCard => ActionType.PlayCard,
        WireActionTypes.Attack => ActionType.Attack,
        WireActionTypes.ScaleUp => ActionType.ScaleUp,
        WireActionTypes.Monetize => ActionType.Monetize,
        WireActionTypes.DiscardHand => ActionType.DiscardHand,
        WireActionTypes.UseEffect => ActionType.UseEffect,
        WireActionTypes.SetReactive => ActionType.SetReactive,
        WireActionTypes.Migrate => ActionType.Migrate,
        WireActionTypes.EndPhase => ActionType.EndPhase,
        WireActionTypes.Forfeit => ActionType.Forfeit,
        WireActionTypes.SelectSlot => ActionType.SelectSlot,
        _ => throw new ArgumentException($"Unknown action type: {s}")
    };

    // ─── Zone ───────────────────────────────────────────────

    public static string ToWireString(this Zone zone) => zone switch
    {
        Zone.Frontend => Zones.Frontend,
        Zone.Backend => Zones.Backend,
        Zone.Support => Zones.Support,
        _ => throw new ArgumentOutOfRangeException(nameof(zone))
    };

    public static Zone ParseZone(string s) => s switch
    {
        Zones.Frontend => Zone.Frontend,
        Zones.Backend => Zone.Backend,
        Zones.Support => Zone.Support,
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

}

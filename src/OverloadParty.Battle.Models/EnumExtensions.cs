namespace OverloadParty.Battle.Models;

/// <summary>
/// String conversion helpers for enums. These produce the snake_case / original-case
/// values used in JSON and DB, keeping Models free of any JSON serializer dependency.
/// </summary>
public static class EnumExtensions
{
    // ─── Phase ──────────────────────────────────────────────

    /// <summary>
    /// Phase をワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="phase">変換対象のフェーズ</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this Phase phase) => phase switch
    {
        Phase.Draw => Phases.Draw,
        Phase.Main => Phases.Main,
        Phase.Battle => Phases.Battle,
        Phase.End => Phases.End,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    /// <summary>
    /// ワイヤー表現の文字列を Phase に変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後の Phase</returns>
    public static Phase ParsePhase(string s) => s switch
    {
        Phases.Draw => Phase.Draw,
        Phases.Main => Phase.Main,
        Phases.Battle => Phase.Battle,
        Phases.End => Phase.End,
        _ => throw new ArgumentException($"Unknown phase: {s}")
    };

    // ─── Rank ───────────────────────────────────────────────

    /// <summary>
    /// ランクをワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="rank">変換対象のランク</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this Rank rank) => rank switch
    {
        Rank.Small => Ranks.Small,
        Rank.Medium => Ranks.Medium,
        Rank.Large => Ranks.Large,
        _ => throw new ArgumentOutOfRangeException(nameof(rank))
    };

    /// <summary>
    /// ワイヤー表現の文字列をランクに変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後のランク</returns>
    public static Rank ParseRank(string s) => s switch
    {
        Ranks.Small => Rank.Small,
        Ranks.Medium => Rank.Medium,
        Ranks.Large => Rank.Large,
        _ => throw new ArgumentException($"Unknown rank: {s}")
    };

    // ─── InstanceFamily ─────────────────────────────────────

    /// <summary>
    /// インスタンスファミリーをワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="family">変換対象のインスタンスファミリー</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this InstanceFamily family) => family switch
    {
        InstanceFamily.M => InstanceFamilies.M,
        InstanceFamily.C => InstanceFamilies.C,
        InstanceFamily.R => InstanceFamilies.R,
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };

    /// <summary>
    /// ワイヤー表現の文字列をインスタンスファミリーに変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後のインスタンスファミリー</returns>
    public static InstanceFamily ParseInstanceFamily(string s) => s switch
    {
        InstanceFamilies.M => InstanceFamily.M,
        InstanceFamilies.C => InstanceFamily.C,
        InstanceFamilies.R => InstanceFamily.R,
        _ => throw new ArgumentException($"Unknown instance family: {s}")
    };

    // ─── GameStatus ─────────────────────────────────────────

    /// <summary>
    /// ゲームステータスをワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="status">変換対象のゲームステータス</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this GameStatus status) => status switch
    {
        GameStatus.Playing => OverloadParty.GameLogicConstants.GameStatus.Playing,
        GameStatus.Finished => OverloadParty.GameLogicConstants.GameStatus.Finished,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    /// <summary>
    /// ワイヤー表現の文字列をゲームステータスに変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後のゲームステータス</returns>
    public static GameStatus ParseGameStatus(string s) => s switch
    {
        OverloadParty.GameLogicConstants.GameStatus.Playing => GameStatus.Playing,
        OverloadParty.GameLogicConstants.GameStatus.Finished => GameStatus.Finished,
        _ => throw new ArgumentException($"Unknown game status: {s}")
    };

    // ─── WinReason ──────────────────────────────────────────

    /// <summary>
    /// 勝利理由をワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="reason">変換対象の勝利理由</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this WinReason reason) => reason switch
    {
        WinReason.BudgetZero => WinReasons.BudgetZero,
        WinReason.SystemDown => WinReasons.SystemDown,
        WinReason.DeckOut => WinReasons.DeckOut,
        WinReason.TurnTimeout => WinReasons.TurnTimeout,
        WinReason.Disconnect => WinReasons.Disconnect,
        WinReason.TurnLimit => WinReasons.TurnLimit,
        WinReason.Draw => WinReasons.Draw,
        WinReason.LaunchFailure => WinReasons.LaunchFailure,
        WinReason.Surrender => WinReasons.Surrender,
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    /// <summary>
    /// ワイヤー表現の文字列を勝利理由に変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後の勝利理由</returns>
    public static WinReason ParseWinReason(string s) => s switch
    {
        WinReasons.BudgetZero => WinReason.BudgetZero,
        WinReasons.SystemDown => WinReason.SystemDown,
        WinReasons.DeckOut => WinReason.DeckOut,
        WinReasons.TurnTimeout => WinReason.TurnTimeout,
        WinReasons.Disconnect => WinReason.Disconnect,
        WinReasons.TurnLimit => WinReason.TurnLimit,
        WinReasons.Draw => WinReason.Draw,
        WinReasons.LaunchFailure => WinReason.LaunchFailure,
        WinReasons.Surrender => WinReason.Surrender,
        _ => throw new ArgumentException($"Unknown win reason: {s}")
    };

    // ─── ActionType ─────────────────────────────────────────

    /// <summary>
    /// アクション種別をワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="action">変換対象のアクション種別</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this ActionType action) => action switch
    {
        ActionType.PlayCard => ActionTypes.PlayCard,
        ActionType.Attack => ActionTypes.Attack,
        ActionType.ScaleUp => ActionTypes.ScaleUp,
        ActionType.Monetize => ActionTypes.Monetize,
        ActionType.DiscardHand => ActionTypes.DiscardHand,
        ActionType.UseIgnition => ActionTypes.UseIgnition,
        ActionType.UseInitiative => ActionTypes.UseInitiative,
        ActionType.EndPhase => ActionTypes.EndPhase,
        ActionType.Forfeit => ActionTypes.Forfeit,
        ActionType.ForfeitBoth => ActionTypes.ForfeitBoth,
        ActionType.SelectSlot => ActionTypes.SelectSlot,
        ActionType.ResolvePendingChoice => ActionTypes.ResolvePendingChoice,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>
    /// ワイヤー表現の文字列をアクション種別に変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後のアクション種別</returns>
    public static ActionType ParseActionType(string s) => s switch
    {
        ActionTypes.PlayCard => ActionType.PlayCard,
        ActionTypes.Attack => ActionType.Attack,
        ActionTypes.ScaleUp => ActionType.ScaleUp,
        ActionTypes.Monetize => ActionType.Monetize,
        ActionTypes.DiscardHand => ActionType.DiscardHand,
        ActionTypes.UseIgnition => ActionType.UseIgnition,
        ActionTypes.UseInitiative => ActionType.UseInitiative,
        ActionTypes.EndPhase => ActionType.EndPhase,
        ActionTypes.Forfeit => ActionType.Forfeit,
        ActionTypes.ForfeitBoth => ActionType.ForfeitBoth,
        ActionTypes.SelectSlot => ActionType.SelectSlot,
        ActionTypes.ResolvePendingChoice => ActionType.ResolvePendingChoice,
        _ => throw new ArgumentException($"Unknown action type: {s}")
    };

    // ─── Zone ───────────────────────────────────────────────

    /// <summary>
    /// ゾーンをワイヤー表現の文字列に変換します
    /// </summary>
    /// <param name="zone">変換対象のゾーン</param>
    /// <returns>ワイヤー表現の文字列</returns>
    public static string ToWireString(this Zone zone) => zone switch
    {
        Zone.Frontend => Zones.Frontend,
        Zone.Backend => Zones.Backend,
        Zone.Support => Zones.Support,
        _ => throw new ArgumentOutOfRangeException(nameof(zone))
    };

    /// <summary>
    /// ワイヤー表現の文字列をゾーンに変換します
    /// </summary>
    /// <param name="s">ワイヤー表現の文字列</param>
    /// <returns>パース後のゾーン</returns>
    public static Zone ParseZone(string s) => s switch
    {
        Zones.Frontend => Zone.Frontend,
        Zones.Backend => Zone.Backend,
        Zones.Support => Zone.Support,
        _ => throw new ArgumentException($"Unknown zone: {s}")
    };

    // ─── CardType helpers ───────────────────────────────────

    /// <summary>
    /// カードタイプ文字列から大分類カテゴリを返します
    /// </summary>
    /// <param name="cardType">カードタイプ文字列</param>
    /// <returns>カードタイプの大分類カテゴリ</returns>
    public static CardTypeCategory GetCategory(string cardType) => cardType switch
    {
        CardTypes.Compute => CardTypeCategory.Compute,
        CardTypes.DataResource => CardTypeCategory.DataResource,
        CardTypes.Platform or CardTypes.Attachment or CardTypes.Strategy
            or CardTypes.Reactive or CardTypes.Incident => CardTypeCategory.Support,
        _ => throw new ArgumentException($"Unknown card type: {cardType}")
    };

}

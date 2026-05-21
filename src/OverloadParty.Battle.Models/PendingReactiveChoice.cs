using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Models;

/// <summary>
/// reactive 効果が発動中で、プレイヤーの選択を待っている状態を表す。
/// 効果ハンドラは選択値が揃うまで一旦停止し、ChooserPlayerNum のプレイヤーが
/// ResolvePendingChoice アクションを送ると効果を再実行する。
/// </summary>
public class PendingReactiveChoice
{
    /// <summary>選択を行うプレイヤー番号。リアクティブの所有者と一致しない場合あり (相手選択)。</summary>
    public required long ChooserPlayerNum { get; set; }

    /// <summary>リアクティブの所有者プレイヤー番号。効果再開時の PlayerNum はこちらを使う。</summary>
    public required long OwnerPlayerNum { get; set; }

    /// <summary>選択待ちのリアクティブカードのカード ID (効果ハンドラ検索用)。</summary>
    public required string ReactiveCardId { get; set; }

    /// <summary>選択待ちのリアクティブカードのインスタンス ID (サポートゾーン上のカード特定用)。</summary>
    public required string ReactiveInstanceId { get; set; }

    /// <summary>効果ハンドラ検索用のトリガー種別 (on_destroy / on_attack_declared 等)。</summary>
    public required TriggerType Trigger { get; set; }

    /// <summary>選択キー (ChoiceData に詰める key、"cardId" / "targetInstanceId" 等)。</summary>
    public required string ChoiceKey { get; set; }

    /// <summary>選択候補。クライアントが UI 表示する対象 ID 一覧。</summary>
    public required List<string> Candidates { get; set; }

    /// <summary>選択カテゴリ。クライアント UI の対象種別判定に使う ("hand_card" / "field_target" 等)。</summary>
    public required string ChoiceKind { get; set; }

    // ─── Resume context (効果再開時に EffectContext を再構築するためのスナップショット) ──

    /// <summary>効果発火元のリソースインスタンス ID。</summary>
    public string? SourceInstanceId { get; set; }

    /// <summary>効果対象のリソースインスタンス ID。</summary>
    public string? TargetInstanceId { get; set; }

    /// <summary>
    /// 破壊済みのリソースを Target とする on_destroy 用のスナップショット。
    /// 再実行時に state のフィールドから見つからない場合のフォールバックに使う。
    /// </summary>
    public DeployedResource? TargetSnapshot { get; set; }

    /// <summary>
    /// Source 側も同じ理由でスナップショットを保持する。
    /// </summary>
    public DeployedResource? SourceSnapshot { get; set; }

    /// <summary>トリガーイベントを起こしたプレイヤー番号 (event_owner)。</summary>
    public long? EventOwnerNum { get; set; }

    /// <summary>on_incident トリガー時のインシデントカード ID。</summary>
    public string? IncidentCardId { get; set; }

    /// <summary>on_attack_declared トリガー時の攻撃ダメージ値。</summary>
    public long? EventDamage { get; set; }
}

/// <summary>
/// 選択カテゴリ (PendingReactiveChoice.ChoiceKind の値) の定数。
/// </summary>
public static class ChoiceKinds
{
    /// <summary>手札の 1 枚を選ぶ。</summary>
    public const string HandCard = "hand_card";

    /// <summary>フィールド上のリソースを 1 体選ぶ。</summary>
    public const string FieldTarget = "field_target";
}

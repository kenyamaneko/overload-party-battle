using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Models;

/// <summary>
/// 効果が発動中で、プレイヤーの選択を待っている状態を表す。
/// 効果ハンドラは選択値が揃うまで一旦停止し、ChooserPlayerNum のプレイヤーが
/// ResolvePendingChoice アクションを送ると効果を再実行する。
/// </summary>
public class PendingEffectChoice
{
    /// <summary>選択を行うプレイヤー番号。効果の所有者と一致しない場合あり (相手選択)。</summary>
    public required long ChooserPlayerNum { get; set; }

    /// <summary>効果の所有者プレイヤー番号。効果再開時の PlayerNum はこちらを使う。</summary>
    public required long OwnerPlayerNum { get; set; }

    /// <summary>選択待ちの効果を持つカードのカード ID (効果ハンドラ検索用)。</summary>
    public required string EffectCardId { get; set; }

    /// <summary>選択待ちの効果を持つカードのインスタンス ID。</summary>
    public required string EffectInstanceId { get; set; }

    /// <summary>効果ハンドラ検索用のトリガー種別 (on_destroy / on_attack_declared 等)。</summary>
    public required TriggerType Trigger { get; set; }

    /// <summary>選択キー (ChoiceData に詰める key、"cardId" / "instanceId" 等)。</summary>
    public required string ChoiceKey { get; set; }

    /// <summary>選択候補。クライアントが UI 表示する対象 ID 一覧。</summary>
    public required List<string> Candidates { get; set; }

    /// <summary>選択カテゴリ。ChoiceKinds の値を入れる。</summary>
    public required string ChoiceKind { get; set; }

    // ─── Resume context (効果再開時に EffectContext を再構築するための文脈) ──

    /// <summary>効果発火元のリソース。破壊済みも含めて suspend 時点の参照を保持する。</summary>
    public DeployedResource? Source { get; set; }

    /// <summary>効果対象のリソース。破壊済みも含めて suspend 時点の参照を保持する。</summary>
    public DeployedResource? Target { get; set; }

    /// <summary>トリガーイベントを起こしたプレイヤー番号 (event_owner)。</summary>
    public long? EventOwnerNum { get; set; }

    /// <summary>on_incident トリガー時のインシデントカード ID。</summary>
    public string? IncidentCardId { get; set; }

    /// <summary>on_attack_declared トリガー時の攻撃ダメージ値。</summary>
    public long? EventDamage { get; set; }

    /// <summary>
    /// 効果を中断した op の位置。再開はここから始めるため、手前の op は再実行されない。
    /// </summary>
    public int ResumeOpIndex { get; set; }

    /// <summary>エンドフェーズ効果の途中で中断した場合の、フェーズを終えようとしているプレイヤー番号。</summary>
    public long? EndPhasePlayerNum { get; set; }

    /// <summary>中断までに発動を終えたエンドフェーズ効果のインスタンス ID。再開時はこれらを飛ばす。</summary>
    public List<string> EndPhaseFiredInstanceIds { get; set; } = [];
}

/// <summary>
/// 選択カテゴリ (PendingEffectChoice.ChoiceKind の値) の定数。
/// </summary>
public static class ChoiceKinds
{
    /// <summary>手札の 1 枚を選ぶ。</summary>
    public const string HandCard = "hand_card";

    /// <summary>フィールド上のリソースを 1 体選ぶ。</summary>
    public const string FieldTarget = "field_target";

    /// <summary>デッキの上から提示された候補のうち 1 枚を選ぶ。</summary>
    public const string DeckTop = "deck_top";

    /// <summary>効果の分岐肢のうち 1 つを選ぶ。</summary>
    public const string Branch = "branch";

    /// <summary>相手のサポートゾーンに伏せられた裏向きリアクティブカードを 1 枚選ぶ。</summary>
    public const string FaceDownReactive = "face_down_reactive";
}

/// <summary>
/// 選択肢 1 件。Key は解決時に返す識別子。
/// </summary>
public class ChoiceOption
{
    /// <summary>選択を識別する値 (branch キー / カード・インスタンス ID)。</summary>
    public required string Key { get; set; }
}

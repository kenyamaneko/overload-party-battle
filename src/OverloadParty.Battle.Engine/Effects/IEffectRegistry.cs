using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// EffectResult は効果ハンドラの実行結果を保持します
/// </summary>
public class EffectResult
{
    /// <summary>Events generated during effect execution.</summary>
    public List<GameEvent> Events { get; set; } = [];

    /// <summary>Whether the triggering action should be cancelled.</summary>
    public bool ShouldCancelAction { get; set; }

    /// <summary>ガード条件が不満足で効果が発動しなかった場合 true。</summary>
    public bool HasGuardFailed { get; set; }

    /// <summary>選択待ち状態。choice op が ChoiceData 不足で suspend したとき設定される。</summary>
    public PendingEffectChoice? PendingChoice { get; set; }
}

/// <summary>
/// EffectContext は効果ハンドラ実行時に渡されるコンテキストです
/// </summary>
public class EffectContext
{
    /// <summary>Current game state.</summary>
    public required BattleGameState State { get; init; }

    /// <summary>The game metadata.</summary>
    public required Game Game { get; init; }

    /// <summary>Player number of the effect owner (1 or 2).</summary>
    public required long PlayerNum { get; init; }

    /// <summary>Source resource that triggered the effect, if any.</summary>
    public DeployedResource? Source { get; init; }

    /// <summary>Target resource of the effect, if any.</summary>
    public DeployedResource? Target { get; init; }

    /// <summary>Support-zone source (for platform/reactive cards).</summary>
    public DeployedSupport? SupSource { get; init; }

    /// <summary>SupSource を持たない起動効果経路で、選択待ち・再開時に効果を同定するカード ID。</summary>
    public string? EffectCardId { get; init; }

    /// <summary>SupSource を持たない起動効果経路で、選択待ち・再開時に効果を同定するインスタンス ID。</summary>
    public string? EffectInstanceId { get; init; }

    /// <summary>Card definition cache for lookups.</summary>
    public required ICardCache CardCache { get; init; }

    /// <summary>Player choice data for branching effects.</summary>
    public Dictionary<string, object>? ChoiceData { get; init; }

    /// <summary>トリガーとなったイベントを起こしたプレイヤー番号</summary>
    public long? EventOwnerNum { get; init; }

    /// <summary>on_incident トリガーのインシデントカード定義</summary>
    public CardDefinition? IncidentCard { get; init; }

    /// <summary>on_attack_declared イベントの攻撃ダメージ</summary>
    public long? EventDamage { get; init; }

    /// <summary>入れ子のトリガー発火に使う効果レジストリ</summary>
    public required IEffectRegistry Effects { get; init; }

    /// <summary>
    /// 発動中のトリガー種別。choice op が選択待ちを state に保存するときに使う。
    /// 選択待ちに入りうる経路では必ずセットする。
    /// </summary>
    public TriggerType? Trigger { get; set; }

    /// <summary>
    /// 選択の解決で効果を再開するときの開始位置。中断した op を指す。
    /// 手前の op は実行済みなので飛ばす。0 なら先頭からの通常実行。
    /// </summary>
    public int ResumeFromOpIndex { get; init; }
}

/// <summary>
/// 効果ハンドラのデリゲート型
/// </summary>
public delegate EffectResult EffectHandler(EffectContext ctx);

/// <summary>
/// バジェットRequirement は効果の Op から抽出されたバジェット条件を保持します
/// </summary>
public class BudgetRequirement
{
    /// <summary>Minimum budget needed (from MinBudgetGuard). Null if no minimum.</summary>
    public long? MinBudget { get; init; }

    /// <summary>Maximum budget allowed (from MaxBudgetGuard). Null if no maximum.</summary>
    public long? MaxBudget { get; init; }

    /// <summary>
    /// Checks whether the given budget satisfies both min and max constraints.
    /// </summary>
    /// <param name="budget">The budget value to check.</param>
    /// <returns>True if the budget is within the required range.</returns>
    public bool IsSatisfied(long budget) =>
        (MinBudget is null || budget >= MinBudget) &&
        (MaxBudget is null || budget <= MaxBudget);
}

/// <summary>
/// Registry for looking up effect handlers by card ID and trigger type.
/// Engine depends only on this interface; implementation is in Effects/.
/// </summary>
public interface IEffectRegistry
{
    /// <summary>
    /// Retrieves the effect handler for the given card and trigger, or null if none registered.
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <param name="trigger">検索対象のトリガー種別。</param>
    /// <returns>The handler, or null if not found.</returns>
    EffectHandler? Get(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns whether a handler is registered for the given card and trigger.
    /// </summary>
    /// <param name="cardId">Card ID to check.</param>
    /// <param name="trigger">Trigger type to check.</param>
    /// <returns>True if a handler exists.</returns>
    bool Has(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns budget requirements for the given effect, or null if none.
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <param name="trigger">検索対象のトリガー種別。</param>
    /// <returns>抽出したバジェット条件、または条件なしの場合は null。</returns>
    BudgetRequirement? GetBudgetRequirement(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns NPC classification for an effect, or null if no ops are stored.
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <param name="trigger">検索対象のトリガー種別。</param>
    /// <returns>分類結果。op 列が未保存の場合は null。</returns>
    EffectInfo? GetEffectInfo(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns branch keys if the effect uses BranchOnChoice, or null otherwise.
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <param name="trigger">検索対象のトリガー種別。</param>
    /// <returns>分岐キーのリスト。BranchOnChoice が使われていない場合は null。</returns>
    List<string>? GetChoiceOptions(string cardId, TriggerType trigger);

    /// <summary>
    /// Returns the raw ops for an effect, or null if no handler or no ops are stored.
    /// Used by <see cref="AvailableActions"/> to inspect filter information that
    /// only the ops themselves carry (e.g. <c>trash_to_hand</c> filters).
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <param name="trigger">検索対象のトリガー種別。</param>
    /// <returns>登録済み op 列。未登録または op 列が未保存の場合は null。</returns>
    IEffectOp[]? GetOps(string cardId, TriggerType trigger);

    /// <summary>
    /// カードが持つパッシブ効果を登録します。1 枚のカードが複数件持つ場合は呼び出しごとに積み上がります。
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <param name="def">登録するパッシブ効果の定義。</param>
    void RegisterPassive(string cardId, PassiveEffectDef def);

    /// <summary>
    /// カードが持つパッシブ効果を全て返します。
    /// </summary>
    /// <param name="cardId">検索対象のカード ID。</param>
    /// <returns>登録済みのパッシブ効果定義群。未登録なら空。</returns>
    IReadOnlyList<PassiveEffectDef> GetPassives(string cardId);
}

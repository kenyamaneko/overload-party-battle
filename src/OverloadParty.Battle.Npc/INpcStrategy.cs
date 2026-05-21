using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Strategy defines the interface for NPC decision-making.
/// 入力は情報秘匿済みの ClientGameState — human が見られない情報には触れない。
/// </summary>
public interface INpcStrategy
{
    /// <summary>
    /// メインフェーズで実行するアクション列を決定します。
    /// </summary>
    /// <param name="clientState">NPC 視点の情報秘匿済みゲーム状態。</param>
    /// <returns>NPC が試行するアクション列。</returns>
    List<NpcAction> DecideMainPhaseActions(GD.ClientGameState clientState);

    /// <summary>
    /// バトルフェーズで実行するアクション列を決定します。
    /// </summary>
    List<NpcAction> DecideBattlePhaseActions(GD.ClientGameState clientState);

    /// <summary>
    /// 手札上限超過分として捨てるカードを決定します。
    /// </summary>
    /// <param name="clientState">NPC 視点の情報秘匿済みゲーム状態。</param>
    /// <param name="discardCount">捨てるべき枚数。</param>
    /// <returns>捨てるカードの InstanceID 列。</returns>
    List<string> DecideDiscard(GD.ClientGameState clientState, int discardCount);

    /// <summary>
    /// 効果由来のスロット選択への応答を決定します。
    /// </summary>
    /// <returns>選択アクション。応答対象がなければ null。</returns>
    NpcAction? DecideSlotSelect(GD.ClientGameState clientState);

    /// <summary>
    /// 効果処理中のプレイヤー選択 (PendingEffectChoice) への応答を決定します。
    /// </summary>
    /// <returns>解決アクション。候補が無いなど解決不能なら null。</returns>
    NpcAction? DecidePendingEffectChoice(GD.ClientGameState clientState);
}

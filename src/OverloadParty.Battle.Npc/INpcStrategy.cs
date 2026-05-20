using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Strategy defines the interface for NPC decision-making.
/// Available actions are pre-computed by the engine; the AI selects which to take.
/// </summary>
public interface INpcStrategy
{
    /// <summary>
    /// メインフェーズで実行するアクション列を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲーム。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="available">エンジンが事前計算した実行可能アクション一覧。</param>
    /// <returns>NPC が試行するアクション列。</returns>
    List<NpcAction> DecideMainPhaseActions(BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available);

    /// <summary>
    /// バトルフェーズで実行するアクション列を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲーム。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="available">エンジンが事前計算した実行可能アクション一覧。</param>
    /// <returns>NPC が試行するアクション列。</returns>
    List<NpcAction> DecideBattlePhaseActions(BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available);

    /// <summary>
    /// 手札上限超過分として捨てるカードを決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="discardCount">捨てるべき枚数。</param>
    /// <returns>捨てるカードの InstanceID 列。</returns>
    List<string> DecideDiscard(BattleGameState state, long npcPlayerNum, int discardCount);

    /// <summary>
    /// 保留中のスロット選択への応答を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <returns>選択アクション。応答対象がなければ null。</returns>
    NpcAction? DecideSlotSelect(BattleGameState state, long npcPlayerNum);
}

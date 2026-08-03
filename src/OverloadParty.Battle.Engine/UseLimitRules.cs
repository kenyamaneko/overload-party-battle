using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// カード記載の回数制限 (use_limit) を、使い切ったかの判定と使用の記録の 2 つに分けて扱います。
/// アクションの列挙と実行時の検証が同じ判定を返すよう、両者はここを経由します。
/// </summary>
public static class UseLimitRules
{
    /// <summary>
    /// 効果が宣言した回数制限を使い切っているかを判定します。
    /// </summary>
    /// <param name="limit">効果が宣言する回数制限。記載がなければ null で、常に使用できる。</param>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">効果を使うプレイヤー番号。</param>
    /// <param name="cardId">効果を持つカードの ID。1 ゲーム 1 回の判定キーになる。</param>
    /// <param name="source">効果を持つリソース。リソース由来でなければ null。</param>
    /// <param name="supSource">効果を持つサポート。サポート由来でなければ null。</param>
    /// <returns>使い切っていれば true。</returns>
    public static bool IsConsumed(
        UseLimitKind? limit,
        BattleGameState state,
        long playerNum,
        string cardId,
        DeployedResource? source,
        DeployedSupport? supSource) => limit switch
        {
            null => false,
            UseLimitKind.OncePerTurn =>
                (source?.EffectUsedThisTurn ?? false) || (supSource?.EffectUsedThisTurn ?? false),
            UseLimitKind.OncePerGame =>
                state.GetStatus(playerNum).UsedOncePerGameCardIds.Contains(cardId),
            _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "unknown use limit"),
        };

    /// <summary>
    /// 効果の回数制限を 1 回分使ったものとして記録します。
    /// </summary>
    /// <param name="limit">効果が宣言する回数制限。</param>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">効果を使うプレイヤー番号。</param>
    /// <param name="cardId">効果を持つカードの ID。1 ゲーム 1 回の記録キーになる。</param>
    /// <param name="source">効果を持つリソース。リソース由来でなければ null。</param>
    /// <param name="supSource">効果を持つサポート。サポート由来でなければ null。</param>
    public static void MarkConsumed(
        UseLimitKind limit,
        BattleGameState state,
        long playerNum,
        string cardId,
        DeployedResource? source,
        DeployedSupport? supSource)
    {
        switch (limit)
        {
            case UseLimitKind.OncePerTurn:
                if (source is not null) { source.EffectUsedThisTurn = true; }
                if (supSource is not null) { supSource.EffectUsedThisTurn = true; }
                break;

            case UseLimitKind.OncePerGame:
                var usedCardIds = state.GetStatus(playerNum).UsedOncePerGameCardIds;
                if (!usedCardIds.Contains(cardId)) { usedCardIds.Add(cardId); }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(limit), limit, "unknown use limit");
        }
    }
}

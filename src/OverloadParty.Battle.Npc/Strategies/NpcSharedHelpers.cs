using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// NpcAi とその Strategies/ サブクラス間で共有するヘルパー。
/// 状態を持たない純関数のみを置く。
/// </summary>
internal static class NpcSharedHelpers
{
    /// <summary>
    /// カード ID からカード定義を取得します。未登録なら例外を送出します。
    /// </summary>
    /// <param name="cc">カード定義の参照元。</param>
    /// <param name="cardId">取得対象のカード ID。</param>
    /// <returns>該当するカード定義。</returns>
    public static CardDefinition ResolveCard(ICardCache cc, string cardId) =>
        cc.Get(cardId)
            ?? throw new InvalidOperationException($"Card '{cardId}' not found in card cache");

    /// <summary>
    /// カード定義が指定タイプキーに一致するか判定します。
    /// </summary>
    /// <param name="card">判定対象のカード定義。</param>
    /// <param name="typeKey">比較するタイプキー。</param>
    /// <returns>一致するなら true。</returns>
    public static bool MatchesCardType(CardDefinition card, string typeKey) =>
        EffectHelpers.MatchesCardType(card, typeKey);

    /// <summary>
    /// カードのデプロイ優先度を AI 設定から解決します。
    /// </summary>
    /// <param name="config">適用する AI 設定。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <param name="card">対象カードの定義。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <returns>デプロイ優先度。該当エントリがなければ 0。</returns>
    public static int ResolveDeployPriority(
        AiConfig config, ICardCache cc, CardDefinition card, DecisionContext ctx)
    {
        if (config.Deploy.ConditionalPriorities is not null)
        {
            var match = config.Deploy.ConditionalPriorities
                .FirstOrDefault(cp => cp.CardId == card.CardId);
            if (match is not null)
            {
                return GuardChecker.Check(match.Condition, ctx, cc)
                    ? match.Priority
                    : match.FallbackPriority;
            }
        }

        var byId = config.Deploy.Priorities
            .FirstOrDefault(e => e.CardId is not null && e.CardId == card.CardId);
        if (byId is not null)
        {
            return byId.Priority;
        }

        var byType = config.Deploy.Priorities
            .FirstOrDefault(e => e.CardType is not null && MatchesCardType(card, e.CardType));
        return byType?.Priority ?? 0;
    }

    /// <summary>
    /// フィールド上の全リソースの維持コスト合計を返します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>維持コストの合計。</returns>
    public static long TotalFieldMaintenanceCost(Field field, ICardCache cc) =>
        FieldHelpers.AllResources(field)
            .Sum(r => ResolveCard(cc, r.CardID).MaintenanceCost);

    /// <summary>
    /// 追加コストを加味した維持コスト合計がバジェット連動の上限を超えるか判定します。
    /// </summary>
    /// <param name="config">適用する AI 設定。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="additionalCost">追加で発生する維持コスト。</param>
    /// <returns>上限を超えるなら true。</returns>
    public static bool WouldExceedMaintenanceLimit(
        AiConfig config, ICardCache cc, DecisionContext ctx, long additionalCost)
    {
        var current = TotalFieldMaintenanceCost(ctx.Field, cc);
        var limit = (long)(config.Budget.MaintenanceLimitRatio * ctx.Budget);
        return current + additionalCost > limit;
    }
}

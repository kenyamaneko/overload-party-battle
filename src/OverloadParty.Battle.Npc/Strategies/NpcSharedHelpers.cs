using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

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
    public static CardDefinition ResolveCard(ICardCache cc, string cardId) =>
        cc.Get(cardId)
            ?? throw new InvalidOperationException($"Card '{cardId}' not found in card cache");

    /// <summary>
    /// カード定義が指定タイプキーに一致するか判定します。
    /// </summary>
    public static bool MatchesCardType(CardDefinition card, string typeKey) =>
        EffectHelpers.MatchesCardType(card, typeKey);

    /// <summary>
    /// カードのデプロイ優先度を AI 設定から解決します。
    /// </summary>
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
    /// 自フィールド上の全リソースの維持コスト合計を返します。
    /// </summary>
    public static long TotalFieldMaintenanceCost(GD.Field field, ICardCache cc) =>
        WireFieldHelpers.AllResources(field)
            .Sum(r => ResolveCard(cc, r.CardID).MaintenanceCost);

    /// <summary>
    /// 追加コストを加味した維持コスト合計がバジェット連動の上限を超えるか判定します。
    /// </summary>
    public static bool WouldExceedMaintenanceLimit(
        AiConfig config, ICardCache cc, DecisionContext ctx, long additionalCost)
    {
        var current = TotalFieldMaintenanceCost(ctx.Field, cc);
        var limit = (long)(config.Budget.MaintenanceLimitRatio * ctx.Budget);
        return current + additionalCost > limit;
    }
}

using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// Monetize (Insight 配分) の判断。
/// reserve_ratio 分を残して、優先順位の高い出口から埋めていく。
/// </summary>
internal sealed class MonetizeStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;

    public MonetizeStrategy(AiConfig config, ICardCache cc)
    {
        _config = config;
        _cc = cc;
    }

    /// <summary>
    /// 収益化アクション列を決定します。
    /// </summary>
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="available">エンジンが事前計算した実行可能アクション一覧。</param>
    /// <param name="insightPool">自分のインサイトプールの残量。</param>
    /// <returns>収益化アクション列。</returns>
    public List<NpcAction> Decide(DecisionContext ctx, List<AvailableAction> available, long insightPool)
    {
        var yieldActions = ActionFilter.FilterByType(available, ActionTypes.Monetize);
        if (yieldActions.Count == 0)
        {
            return [];
        }

        var sorted = TargetSelector.OrderActions(yieldActions, _config.Monetize.OrderBy, ctx.Field, _cc);

        var reserve = (long)(insightPool * _config.Monetize.ReserveRatio);
        var distributable = insightPool - reserve;

        var dists = new List<MonetizeDistribution>();
        var remaining = distributable;

        foreach (var a in sorted)
        {
            if (remaining <= 0)
            {
                break;
            }
            var amount = Math.Min(a.RemainingCapacity, remaining);
            if (amount > 0)
            {
                dists.Add(new MonetizeDistribution
                {
                    InstanceID = a.SourceInstanceID!,
                    Amount = amount,
                });
                remaining -= amount;
            }
        }

        if (dists.Count == 0)
        {
            return [];
        }

        return
        [
            new NpcAction
            {
                ActionType = ActionTypes.Monetize,
                Data = new MonetizeRequest { Distributions = dists },
            },
        ];
    }
}

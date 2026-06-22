using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// 収益化 (Insight 配分) の判断。
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
    public List<NpcAction> Decide(DecisionContext ctx, List<GD.AvailableAction> available, long insightPool)
    {
        var yieldActions = ActionFilter.FilterByType<GD.MonetizeAction>(available);
        if (yieldActions.Count == 0)
        {
            return [];
        }

        var sorted = TargetSelector.OrderActions(yieldActions, a => a.SourceInstanceID, _config.Monetize.OrderBy, ctx.Field, _cc);

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

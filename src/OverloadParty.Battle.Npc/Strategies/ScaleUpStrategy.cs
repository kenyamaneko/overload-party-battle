using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// スケールアップの実行判断。維持コスト上限を超えないように累積確認する。
/// </summary>
internal sealed class ScaleUpStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;

    public ScaleUpStrategy(AiConfig config, ICardCache cc)
    {
        _config = config;
        _cc = cc;
    }

    public List<NpcAction> Decide(DecisionContext ctx, List<AvailableAction> available)
    {
        var scaleActions = ActionFilter.FilterByType(available, ActionTypes.ScaleUp);
        var family = ResolveInstanceFamily(ctx);
        var sorted = TargetSelector.OrderActions(scaleActions, _config.ScaleUp.OrderBy, ctx.Field, _cc);

        var actions = new List<NpcAction>();
        var addedMaintenanceCost = 0L;

        foreach (var a in sorted)
        {
            var cardId = ActionFilter.ResolveCardIdForInstance(a.SourceInstanceID!, ctx.Field);
            var estimatedCostIncrease = NpcSharedHelpers.ResolveCard(_cc, cardId).MaintenanceCost;
            if (estimatedCostIncrease > 0
                && NpcSharedHelpers.WouldExceedMaintenanceLimit(
                    _config, _cc, ctx, addedMaintenanceCost + estimatedCostIncrease))
            {
                continue;
            }

            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.ScaleUp,
                Data = new ScaleUpRequest
                {
                    InstanceID = a.SourceInstanceID!,
                    TargetRank = a.TargetRank!,
                    InstanceFamily = a.NeedsFamily ? family : null,
                },
            });
            addedMaintenanceCost += estimatedCostIncrease;
        }
        return actions;
    }

    private string ResolveInstanceFamily(DecisionContext ctx)
    {
        var match = _config.ScaleUp.ConditionalFamily?
            .FirstOrDefault(cf => GuardChecker.Check(cf.Condition, ctx, _cc));
        return match?.Family ?? _config.ScaleUp.InstanceFamily;
    }
}

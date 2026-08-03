using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// 自フィールド上のカードの起動効果 (UseIgnition) の実行判断。
/// </summary>
internal sealed class IgnitionStrategy
{
    private readonly ICardCache _cc;
    private readonly IEffectRegistry _effects;

    public IgnitionStrategy(ICardCache cc, IEffectRegistry effects)
    {
        _cc = cc;
        _effects = effects;
    }

    /// <summary>
    /// 自フィールド上のカードの起動効果アクション列を決定します。
    /// </summary>
    public List<NpcAction> Decide(
        DecisionContext ctx, List<GD.AvailableAction> available, AiConfig activeConfig)
    {
        var ignitionActions = ActionFilter.FilterByType<GD.UseIgnitionAction>(available);

        var candidates = ignitionActions
            .Select(a =>
            {
                var cardId = ActionFilter.ResolveCardIdForInstance(a.SourceInstanceID!, ctx.Field);
                var (pri, use, choiceData) = PriorityResolver.Evaluate(
                    cardId, TriggerType.Ignition, ctx, activeConfig, _effects, _cc, a.ValidTargets);
                return (Action: a, CardId: cardId, Priority: pri, Use: use, ChoiceData: choiceData);
            })
            .Where(x => x.Use)
            .Select(x =>
            {
                string? targetId = x.ChoiceData is not null && x.ChoiceData.TryGetValue("instanceId", out var id)
                    ? id.ToString()
                    : null;

                if (x.Action.EffectTargetType == AvailableAction.ChoiceEffectTargetType && targetId is null)
                {
                    if (!(x.Action.ValidTargets?.Count > 0))
                    {
                        return ((GD.UseIgnitionAction Action, int Priority, string? TargetId)?)null;
                    }
                    targetId = SelectTargetFromValid(x.CardId, x.Action.ValidTargets, ctx, activeConfig);
                    if (targetId is null)
                    {
                        return null;
                    }
                }

                return (x.Action, x.Priority, targetId);
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderByDescending(x => x.Priority)
            .ToList();

        return candidates
            .Select(c => new NpcAction
            {
                ActionType = ActionTypes.UseIgnition,
                Data = new UseIgnitionRequest
                {
                    InstanceID = c.Action.SourceInstanceID!,
                    TargetInstanceID = c.TargetId,
                },
            })
            .ToList();
    }

    private string? SelectTargetFromValid(
        string cardId, List<string> validTargets, DecisionContext ctx, AiConfig activeConfig)
    {
        var info = _effects.GetEffectInfo(cardId, TriggerType.Ignition);
        if (info is not null)
        {
            var target = PriorityResolver.SelectTarget(
                info, ctx, activeConfig.TargetSelection, _cc, validTargets);
            if (target is not null)
            {
                return target;
            }
        }

        return validTargets.FirstOrDefault();
    }
}

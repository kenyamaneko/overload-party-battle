using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// ストラテジー / インシデント など即時効果カードの使用判断。
/// hold_until / use_conditions / PriorityResolver で use=true となったものだけを手札から発動する。
/// </summary>
internal sealed class ImmediateActionStrategy
{
    private readonly ICardCache _cc;
    private readonly IEffectRegistry _effects;

    public ImmediateActionStrategy(ICardCache cc, IEffectRegistry effects)
    {
        _cc = cc;
        _effects = effects;
    }

    /// <summary>
    /// 即時効果カードの使用アクション列を決定します。
    /// </summary>
    public List<NpcAction> Decide(
        DecisionContext ctx,
        List<GD.PlayCardAction> playActions,
        AiConfig activeConfig)
    {
        var candidates = CardCandidateBuilder.Build<Dictionary<string, object>?>(
            playActions,
            _cc,
            card => FieldHelpers.IsImmediateType(card.CardType)
                    && !ShouldHold(card, ctx, activeConfig)
                    && CheckUseConditions(card, ctx, activeConfig),
            (a, card) =>
            {
                var (pri, use, choice) = PriorityResolver.Evaluate(
                    card.CardId, TriggerType.Ignition, ctx, activeConfig, _effects, _cc, a.ValidTargets);
                if (!use)
                {
                    return null;
                }

                if (a.EffectTargetType == AvailableAction.ChoiceEffectTargetType && choice is null)
                {
                    if (!(a.ValidTargets?.Count > 0))
                    {
                        return null;
                    }
                    choice = new Dictionary<string, object> { ["instanceId"] = a.ValidTargets[0] };
                }

                return (pri, choice);
            });

        return candidates
            .Select(c => new NpcAction
            {
                ActionType = ActionTypes.PlayCard,
                Data = new PlayCardRequest
                {
                    CardInstanceID = c.Action.HandInstanceID!,
                    ChoiceData = c.Extra,
                },
            })
            .ToList();
    }

    private bool ShouldHold(CardDefinition card, DecisionContext ctx, AiConfig config)
    {
        if (config.ImmediateCards.HoldUntil is null)
        {
            return false;
        }

        return config.ImmediateCards.HoldUntil.Any(hold =>
            OverloadParty.Battle.Engine.Effects.EffectHelpers.MatchesCardType(card, hold.CardType)
            && !GuardChecker.Check(hold.Condition, ctx, _cc));
    }

    private bool CheckUseConditions(CardDefinition card, DecisionContext ctx, AiConfig config)
    {
        if (config.ImmediateCards.UseConditions is null)
        {
            return true;
        }

        var info = _effects.GetEffectInfo(card.CardId, TriggerType.Ignition);
        if (info is null)
        {
            return true;
        }

        return info.Categories
            .Select(PriorityResolver.MapCategoryToKey)
            .Where(catKey => catKey is not null
                             && config.ImmediateCards.UseConditions.ContainsKey(catKey))
            .All(catKey => GuardChecker.CheckAll(
                config.ImmediateCards.UseConditions[catKey!], ctx, _cc));
    }
}

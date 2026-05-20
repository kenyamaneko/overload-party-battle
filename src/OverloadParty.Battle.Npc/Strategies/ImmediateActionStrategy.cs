using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// Strategy / Incident など "即時効果" カードの使用判断。
/// hold_until / use_conditions / PriorityResolver で use=true となったものだけを
/// support ゾーンに配置する。
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
    /// <param name="ctx">意思決定コンテキスト。</param>
    /// <param name="playActions">PlayCard 系のアクション一覧。</param>
    /// <param name="usedZones">既に使用済みのゾーン集合。決定時に追記される。</param>
    /// <param name="activeConfig">現在の状況で有効な AI 設定。</param>
    /// <returns>使用アクション列。</returns>
    public List<NpcAction> Decide(
        DecisionContext ctx,
        List<AvailableAction> playActions,
        HashSet<string> usedZones,
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
                    card.CardId, TriggerType.Ignition, ctx, activeConfig, _effects, _cc);
                if (!use)
                {
                    return null;
                }

                if (a.EffectTargetType == "Choice" && choice is null)
                {
                    if (!(a.ValidTargets?.Count > 0))
                    {
                        return null;
                    }
                    choice = new Dictionary<string, object> { ["instanceId"] = a.ValidTargets[0] };
                }

                return (pri, choice);
            });

        var actions = new List<NpcAction>();
        foreach (var c in candidates)
        {
            var zone = ActionFilter.PickSupportZone(c.Action.ValidZones, usedZones);
            if (zone is null)
            {
                continue;
            }

            var pos = ActionFilter.ParseZoneStr(zone)!;
            actions.Add(new NpcAction
            {
                ActionType = ActionTypes.PlayCard,
                Data = new PlayCardRequest
                {
                    CardInstanceID = c.Action.HandInstanceID!,
                    Zone = pos.Zone,
                    Index = pos.Index,
                    ChoiceData = c.Extra,
                },
            });
            usedZones.Add(zone);
        }
        return actions;
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
            .Select(PriorityResolver.CategoryToKey)
            .Where(catKey => catKey is not null
                             && config.ImmediateCards.UseConditions.ContainsKey(catKey))
            .All(catKey => GuardChecker.CheckAll(
                config.ImmediateCards.UseConditions[catKey!], ctx, _cc));
    }
}

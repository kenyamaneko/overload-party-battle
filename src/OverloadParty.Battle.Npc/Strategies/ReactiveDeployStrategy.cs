using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// リアクティブカードのデプロイ判断。
/// サポートゾーンの max_slots を越えない範囲で優先度順に置く。
/// </summary>
internal sealed class ReactiveDeployStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;

    public ReactiveDeployStrategy(AiConfig config, ICardCache cc)
    {
        _config = config;
        _cc = cc;
    }

    /// <summary>
    /// リアクティブのデプロイアクション列を決定します。
    /// </summary>
    public List<NpcAction> Decide(List<GD.AvailableAction> playActions, HashSet<string> usedZones)
    {
        var reactive = _config.Reactive!;

        var candidates = CardCandidateBuilder.Build<object?>(
            playActions,
            _cc,
            card => card.CardType == CardTypes.Reactive,
            (_, card) =>
            {
                var pri = reactive.Priorities.GetValueOrDefault(card.CardId, 0);
                return pri > 0 ? (pri, (object?)null) : null;
            });

        var actions = new List<NpcAction>();
        var usedReactiveSlots = usedZones.Count(z => z.StartsWith("support_"));

        foreach (var c in candidates)
        {
            if (usedReactiveSlots >= reactive.MaxSlots)
            {
                break;
            }

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
                },
            });
            usedZones.Add(zone);
            usedReactiveSlots++;
        }

        return actions;
    }
}

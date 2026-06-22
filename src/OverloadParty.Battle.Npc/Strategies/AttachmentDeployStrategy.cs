using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// リソースデプロイ後に走るアタッチメントのデプロイ判断。
/// config.Attachments が null の場合は呼び出し側で skip されることを前提。
/// </summary>
internal sealed class AttachmentDeployStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;

    public AttachmentDeployStrategy(AiConfig config, ICardCache cc)
    {
        _config = config;
        _cc = cc;
    }

    /// <summary>
    /// アタッチメントのデプロイアクション列を決定します。
    /// </summary>
    public List<NpcAction> Decide(
        DecisionContext ctx, List<GD.PlayCardAction> playActions, HashSet<string> usedZones)
    {
        var attachments = _config.Attachments!;

        var candidates = CardCandidateBuilder.Build<object?>(
            playActions,
            _cc,
            card => card.CardType == CardTypes.Attachment,
            (_, card) =>
            {
                var pri = attachments.TryGetValue(card.CardId, out var entry) ? entry.Priority : 0;
                return (pri, null);
            });

        var actions = new List<NpcAction>();
        foreach (var c in candidates)
        {
            if (!(c.Action.ValidTargets?.Count > 0))
            {
                continue;
            }

            string? targetId;
            if (attachments.TryGetValue(c.Card.CardId, out var cfg) && cfg.Target is not null)
            {
                targetId = TargetSelector.ResolveFromValid(
                    cfg.Target, c.Action.ValidTargets, ctx.Field, ctx.OppField, _cc);
            }
            else
            {
                targetId = c.Action.ValidTargets.FirstOrDefault();
            }

            if (targetId is null)
            {
                continue;
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
                    TargetInstanceID = targetId,
                },
            });
            usedZones.Add(zone);
        }

        return actions;
    }
}

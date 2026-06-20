using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// リソース / プラットフォームのデプロイ判断。
/// 維持コスト上限を超えないよう候補を累積チェックする。
/// アタッチメント / リアクティブは責務が違うため別 strategy で扱う。
/// </summary>
internal sealed class DeployStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;

    public DeployStrategy(AiConfig config, ICardCache cc)
    {
        _config = config;
        _cc = cc;
    }

    /// <summary>
    /// リソース / プラットフォームのデプロイアクション列を決定します。
    /// </summary>
    public List<NpcAction> Decide(
        DecisionContext ctx, List<GD.AvailableAction> playActions, HashSet<string> usedZones)
    {
        var candidates = CardCandidateBuilder.Build<object?>(
            playActions,
            _cc,
            card => !FieldHelpers.IsImmediateType(card.CardType)
                    && card.CardType != CardTypes.Attachment
                    && card.CardType != CardTypes.Reactive,
            (_, card) => (NpcSharedHelpers.ResolveDeployPriority(_config, _cc, card, ctx), null));

        var actions = new List<NpcAction>();
        var deployed = new HashSet<string>();
        var addedMaintenanceCost = 0L;

        foreach (var c in candidates)
        {
            if (deployed.Contains(c.Action.HandInstanceID!))
            {
                continue;
            }

            if (c.Card.MaintenanceCost > 0
                && NpcSharedHelpers.WouldExceedMaintenanceLimit(
                    _config, _cc, ctx, addedMaintenanceCost + c.Card.MaintenanceCost))
            {
                continue;
            }

            var zone = PickDeployZone(c.Action.ValidZones, c.Card, usedZones);
            if (zone is null)
            {
                continue;
            }

            var pos = ActionFilter.ParseZoneStr(zone)!;
            var req = new PlayCardRequest
            {
                CardInstanceID = c.Action.HandInstanceID!,
                Zone = pos.Zone,
                Index = pos.Index,
            };
            if (c.Action.ChoiceOptions?.Count > 0)
            {
                var choice = ResolveDeployChoice(c.Card.CardId);
                req.ChoiceData = new Dictionary<string, object> { ["option"] = choice };
            }

            actions.Add(new NpcAction { ActionType = ActionTypes.PlayCard, Data = req });
            deployed.Add(c.Action.HandInstanceID!);
            usedZones.Add(zone);
            addedMaintenanceCost += c.Card.MaintenanceCost;
        }

        return actions;
    }

    private string? PickDeployZone(List<string>? validZones, CardDefinition card, HashSet<string> usedZones)
    {
        if (validZones is null)
        {
            return null;
        }

        if (_config.Deploy.ZonePreferences is not null)
        {
            var typeKey = ResolveZoneTypeKey(card);
            if (_config.Deploy.ZonePreferences.TryGetValue(typeKey, out var prefs))
            {
                var match = prefs
                    .Select(pref => validZones.FirstOrDefault(z =>
                        z.StartsWith(pref + "_") && !usedZones.Contains(z)))
                    .FirstOrDefault(z => z is not null);
                if (match is not null)
                {
                    return match;
                }
            }
        }

        return ActionFilter.PickBestZone(validZones, card, usedZones);
    }

    private static string ResolveZoneTypeKey(CardDefinition card)
    {
        // ObjectStorage は唯一フロントエンドにも置ける Data subtype なので独立キー扱い。
        if (card.IsDataResource && card.Subtype == "ObjectStorage") { return "ObjectStorage"; }
        return card.CardType;
    }

    private string ResolveDeployChoice(string cardId)
    {
        if (_config.Deploy.Choices is not null
            && _config.Deploy.Choices.TryGetValue(cardId, out var choice))
        {
            return choice;
        }

        throw new InvalidOperationException(
            $"No deploy choice configured for card '{cardId}' in model '{_config.Model}'");
    }
}

using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Fires OnFieldChange triggers on both players' fields when a card is deployed or destroyed.
/// Handlers are invoked in DeployOrder ascending order.
/// </summary>
public static class FieldChangeTrigger
{
    private static bool _firing;

    /// <summary>
    /// Fires OnFieldChange for all eligible cards on both players' fields.
    /// Re-entrant calls are ignored to prevent infinite loops.
    /// </summary>
    public static void Fire(
        GameState state, Game game, ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null || _firing) { return; }

        _firing = true;
        try
        {
            FireForPlayer(state, game, 1, cc, effects);
            FireForPlayer(state, game, 2, cc, effects);
        }
        finally
        {
            _firing = false;
        }
    }

    private static void FireForPlayer(
        GameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry effects)
    {
        var field = state.GetField(playerNum);

        var triggers = new List<(string CardId, long DeployOrder, DeployedResource? Source, DeployedSupport? SupSource)>();

        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (effects.Has(resource.CardID, TriggerType.OnFieldChange))
            {
                triggers.Add((resource.CardID, resource.DeployOrder, resource, null));
            }

            foreach (var att in resource.Attachments)
            {
                if (effects.Has(att.CardID, TriggerType.OnFieldChange))
                {
                    triggers.Add((att.CardID, resource.DeployOrder, resource, null));
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (!support.FaceUp || support.DeployingTurnsLeft > 0) { continue; }
            if (effects.Has(support.CardID, TriggerType.OnFieldChange))
            {
                triggers.Add((support.CardID, support.DeployOrder, null, support));
            }
        }

        triggers.Sort((a, b) => a.DeployOrder.CompareTo(b.DeployOrder));

        foreach (var (cardId, _, source, supSource) in triggers)
        {
            var handler = effects.Get(cardId, TriggerType.OnFieldChange);
            if (handler is null) { continue; }

            handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = source,
                SupSource = supSource,
                CardCache = cc,
            });
        }
    }
}

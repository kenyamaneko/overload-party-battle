using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Validates NPC AI config integrity against card definitions at load time.
/// Throws <see cref="InvalidOperationException"/> if the config is invalid,
/// terminating the battle before it starts.
/// </summary>
public static class AiConfigValidator
{
    public static void Validate(AiConfig config, ICardCache cc)
    {
        var deckCardIds = config.Deck
            .Select(d => d.CardId)
            .ToHashSet();

        ValidateAttachments(config, deckCardIds, cc);
        ValidateReactive(config, deckCardIds, cc);
        ValidateDeployChoices(config, deckCardIds);
        ValidateConditionalPriorities(config, deckCardIds);
    }

    private static void ValidateAttachments(
        AiConfig config, HashSet<string> deckCardIds, ICardCache cc)
    {
        var attachmentCards = deckCardIds
            .Where(id =>
            {
                var card = cc.Get(id);
                if (card is null)
                {
                    throw new InvalidOperationException(
                        $"Config '{config.Model}': deck references unknown card_id '{id}'");
                }
                return card.CardType == CardTypes.Attachment;
            })
            .ToList();

        if (attachmentCards.Count == 0)
        {
            return;
        }

        if (config.Attachments is null)
        {
            throw new InvalidOperationException(
                $"Config '{config.Model}': deck contains {attachmentCards.Count} Attachment card(s) " +
                $"but 'attachments' section is missing. Cards: {string.Join(", ", attachmentCards)}");
        }

        foreach (var cardId in attachmentCards)
        {
            if (!config.Attachments.ContainsKey(cardId))
            {
                throw new InvalidOperationException(
                    $"Config '{config.Model}': Attachment card '{cardId}' in deck " +
                    "has no entry in 'attachments' section");
            }
        }
    }

    private static void ValidateReactive(
        AiConfig config, HashSet<string> deckCardIds, ICardCache cc)
    {
        var reactiveCards = deckCardIds
            .Where(id =>
            {
                var card = cc.Get(id);
                if (card is null)
                {
                    throw new InvalidOperationException(
                        $"Config '{config.Model}': deck references unknown card_id '{id}'");
                }
                return card.CardType == CardTypes.Reactive;
            })
            .ToList();

        if (reactiveCards.Count == 0)
        {
            return;
        }

        if (config.Reactive is null)
        {
            throw new InvalidOperationException(
                $"Config '{config.Model}': deck contains {reactiveCards.Count} Reactive card(s) " +
                $"but 'reactive' section is missing. Cards: {string.Join(", ", reactiveCards)}");
        }

        foreach (var cardId in reactiveCards)
        {
            if (!config.Reactive.Priorities.ContainsKey(cardId))
            {
                throw new InvalidOperationException(
                    $"Config '{config.Model}': Reactive card '{cardId}' in deck " +
                    "has no entry in 'reactive.priorities'");
            }
        }
    }

    private static void ValidateDeployChoices(
        AiConfig config, HashSet<string> deckCardIds)
    {
        if (config.Deploy.Choices is null)
        {
            return;
        }

        foreach (var cardId in config.Deploy.Choices.Keys)
        {
            if (!deckCardIds.Contains(cardId))
            {
                throw new InvalidOperationException(
                    $"Config '{config.Model}': deploy.choices references " +
                    $"card '{cardId}' which is not in deck");
            }
        }
    }

    private static void ValidateConditionalPriorities(
        AiConfig config, HashSet<string> deckCardIds)
    {
        if (config.Deploy.ConditionalPriorities is null)
        {
            return;
        }

        foreach (var entry in config.Deploy.ConditionalPriorities)
        {
            if (!deckCardIds.Contains(entry.CardId))
            {
                throw new InvalidOperationException(
                    $"Config '{config.Model}': deploy.conditional_priorities references " +
                    $"card '{entry.CardId}' which is not in deck");
            }
        }
    }
}

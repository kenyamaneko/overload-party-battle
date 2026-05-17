using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// A card eligible to react to an event, identified by its carrier instance and card ID.
/// </summary>
public sealed class EventTriggerCandidate
{
    /// <summary>Card ID used to look up the effect handler.</summary>
    public required string CardId { get; init; }

    /// <summary>Deploy order of the carrier; lowest reacts first.</summary>
    public required long DeployOrder { get; init; }

    /// <summary>Resource carrier when the reacting card is a field resource.</summary>
    public DeployedResource? Resource { get; init; }

    /// <summary>Support carrier when the reacting card sits in the support zone.</summary>
    public DeployedSupport? Support { get; init; }

    /// <summary>Owner of the carrier, used to resolve the consumed Reactive's trash pile.</summary>
    public required long OwnerNum { get; init; }
}

/// <summary>
/// Fires an event-driven trigger across a scan range, applying the single-Reactive rule:
/// among card_type == Reactive carriers, only the earliest one whose effect resolves fires,
/// and it is flipped face-up then sent to trash. Platform / Attachment / Compute carriers
/// are persistent watchers and each fire on every event.
/// </summary>
public static class EventTriggerFiring
{
    /// <summary>
    /// Fires the given trigger for all candidates. Returns the collected events and whether
    /// the triggering action was cancelled by any reacting effect.
    /// </summary>
    public static (bool Cancelled, List<GameEvent> Events) Fire(
        BattleGameState state,
        IEffectRegistry effects,
        ICardCache cc,
        TriggerType trigger,
        IReadOnlyList<EventTriggerCandidate> candidates,
        Func<EventTriggerCandidate, EffectContext> buildContext)
    {
        var events = new List<GameEvent>();
        bool cancelled = false;

        var eligible = candidates
            .Where(c => effects.Has(c.CardId, trigger))
            .OrderBy(c => c.DeployOrder)
            .ToList();

        bool reactiveFired = false;

        foreach (var candidate in eligible)
        {
            bool isReactive = cc.MustGet(candidate.CardId).CardType == CardTypes.Reactive;

            // 1 event につき発動する Reactive は最も早い 1 枚のみ。
            if (isReactive && reactiveFired) { continue; }

            var handler = effects.Get(candidate.CardId, trigger)!;
            var result = handler(buildContext(candidate));

            if (result.GuardFailed)
            {
                // ガード不成立の Reactive は発動扱いにせず（使い切らない）、次の候補へ。
                continue;
            }

            events.AddRange(result.Events);
            cancelled |= result.CancelAction;

            if (isReactive)
            {
                reactiveFired = true;
                ConsumeReactive(state, candidate);
            }
        }

        return (cancelled, events);
    }

    /// <summary>
    /// Flips a fired Reactive face-up and moves it to its owner's trash pile.
    /// </summary>
    private static void ConsumeReactive(BattleGameState state, EventTriggerCandidate candidate)
    {
        if (candidate.Support is { } support)
        {
            support.FaceUp = true;
            FieldHelpers.RemoveSupportFromField(state.GetField(candidate.OwnerNum), support.InstanceID);
            CardMoveHelpers.AddToTrash(state, candidate.OwnerNum, support.CardID, support.InstanceID, support.ArtNo);
            return;
        }

        if (candidate.Resource is { } resource)
        {
            resource.FaceUp = true;
            FieldHelpers.RemoveResourceFromField(state.GetField(candidate.OwnerNum), resource.InstanceID);
            CardMoveHelpers.AddToTrash(state, candidate.OwnerNum, resource.CardID, resource.InstanceID, resource.ArtNo);
        }
    }
}

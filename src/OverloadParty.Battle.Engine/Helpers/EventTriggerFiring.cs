using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// event に反応しうるカード。担い手のインスタンスと card ID で識別します。
/// </summary>
public sealed class EventTriggerCandidate
{
    /// <summary>効果ハンドラの参照に使う card ID。</summary>
    public required string CardId { get; init; }

    /// <summary>担い手の DeployOrder。小さいものが先に反応する。</summary>
    public required long DeployOrder { get; init; }

    /// <summary>反応カードがフィールド上リソースのときの担い手。</summary>
    public DeployedResource? Resource { get; init; }

    /// <summary>反応カードがサポートゾーンにあるときの担い手。</summary>
    public DeployedSupport? Support { get; init; }

    /// <summary>担い手の所有プレイヤー番号。</summary>
    public required long OwnerNum { get; init; }
}

/// <summary>
/// event 駆動トリガーを走査範囲の候補に対して発火します。
/// </summary>
public static class EventTriggerFiring
{
    /// <summary>
    /// 候補にトリガーを発火し、収集イベントとアクションがキャンセルされたかを返します。
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
    /// 発動した Reactive を表向きにして所有者のトラッシュへ送ります。
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

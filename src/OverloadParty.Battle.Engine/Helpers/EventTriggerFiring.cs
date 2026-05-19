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

    /// <summary>1 イベントにつき最大 1 枚だけ発火し、発火後に消費される使い切りトリガーか。</summary>
    public required bool OneShot { get; init; }

    /// <summary>サポートゾーンのカードから候補を生成します。</summary>
    /// <param name="support">サポートゾーンの担い手。</param>
    /// <param name="ownerNum">担い手の所有プレイヤー番号。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>生成した候補。</returns>
    public static EventTriggerCandidate ForSupport(DeployedSupport support, long ownerNum, ICardCache cc) =>
        new()
        {
            CardId = support.CardID,
            DeployOrder = support.DeployOrder,
            Support = support,
            OwnerNum = ownerNum,
            OneShot = cc.MustGet(support.CardID).CardType == CardTypes.Reactive,
        };

    /// <summary>フィールド上リソースから候補を生成します。</summary>
    /// <param name="resource">フィールド上の担い手。</param>
    /// <param name="ownerNum">担い手の所有プレイヤー番号。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>生成した候補。</returns>
    public static EventTriggerCandidate ForResource(DeployedResource resource, long ownerNum, ICardCache cc) =>
        new()
        {
            CardId = resource.CardID,
            DeployOrder = resource.DeployOrder,
            Resource = resource,
            OwnerNum = ownerNum,
            OneShot = cc.MustGet(resource.CardID).CardType == CardTypes.Reactive,
        };
}

/// <summary>
/// event 駆動トリガーを走査範囲の候補に対して発火します。
/// </summary>
public static class EventTriggerFiring
{
    /// <summary>
    /// 候補にトリガーを発火し、収集イベントとアクションがキャンセルされたかを返します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="trigger">発火するトリガー種別。</param>
    /// <param name="candidates">走査対象の候補。</param>
    /// <param name="buildContext">候補ごとに効果実行コンテキストを組み立てる関数。</param>
    /// <param name="consumeOneShot">使い切りトリガーが発火したとき担い手を消費する処理。</param>
    /// <returns>アクションがキャンセルされたかと、収集したイベント。</returns>
    public static (bool Cancelled, List<GameEvent> Events) Fire(
        BattleGameState state,
        IEffectRegistry effects,
        TriggerType trigger,
        IReadOnlyList<EventTriggerCandidate> candidates,
        Func<EventTriggerCandidate, EffectContext> buildContext,
        Action<BattleGameState, EventTriggerCandidate> consumeOneShot)
    {
        var events = new List<GameEvent>();
        bool cancelled = false;

        var eligible = candidates
            .Where(c => effects.Has(c.CardId, trigger))
            .OrderBy(c => c.DeployOrder)
            .ToList();

        bool oneShotFired = false;

        foreach (var candidate in eligible)
        {
            // 使い切りトリガーは 1 イベントにつき 1 枚しか発火しない。
            if (candidate.OneShot && oneShotFired) { continue; }

            var handler = effects.Get(candidate.CardId, trigger)!;
            var result = handler(buildContext(candidate));

            if (result.GuardFailed)
            {
                // ガード不成立の使い切りトリガーは発動扱いにせず（消費しない）、次の候補へ。
                continue;
            }

            events.AddRange(result.Events);
            cancelled |= result.CancelAction;

            if (candidate.OneShot)
            {
                oneShotFired = true;
                consumeOneShot(state, candidate);
            }
        }

        return (cancelled, events);
    }
}

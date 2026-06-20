using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// イベントに対して効果が発動しうる、場のカード1枚。
/// フィールド上のリソースか、サポートゾーンのカードのいずれかを指す。
/// </summary>
public sealed class EventTriggerCandidate
{
    private EventTriggerCandidate() { }

    /// <summary>効果ハンドラを引くための card ID。</summary>
    public required string CardId { get; init; }

    /// <summary>同一イベントに複数のカードが発動するときの解決順。小さいほど先に発動する。</summary>
    public required long DeployOrder { get; init; }

    /// <summary>カードを所有するプレイヤー番号。</summary>
    public required long OwnerNum { get; init; }

    /// <summary>フィールド上のリソースのときの実体。サポートゾーンのカードのときは null。</summary>
    public DeployedResource? Resource { get; init; }

    /// <summary>サポートゾーンのカードのときの実体。フィールド上のリソースのときは null。</summary>
    public DeployedSupport? Support { get; init; }

    /// <summary>フィールド上のリソースから生成します。</summary>
    /// <param name="resource">候補化するリソース。</param>
    /// <param name="ownerNum">リソースを所有するプレイヤー番号。</param>
    /// <returns>生成された候補。</returns>
    public static EventTriggerCandidate ForResource(DeployedResource resource, long ownerNum) =>
        new()
        {
            CardId = resource.CardID,
            DeployOrder = resource.DeployOrder,
            OwnerNum = ownerNum,
            Resource = resource,
        };

    /// <summary>サポートゾーンのカードから生成します。</summary>
    /// <param name="support">候補化するサポートカード。</param>
    /// <param name="ownerNum">サポートカードを所有するプレイヤー番号。</param>
    /// <returns>生成された候補。</returns>
    public static EventTriggerCandidate ForSupport(DeployedSupport support, long ownerNum) =>
        new()
        {
            CardId = support.CardID,
            DeployOrder = support.DeployOrder,
            OwnerNum = ownerNum,
            Support = support,
        };
}

/// <summary>
/// 1つのイベントに対し、効果を持つカードを配置順に発動させます。
/// </summary>
public static class EventTriggerFiring
{
    /// <summary>
    /// 候補のうちトリガーの効果を持つカードを配置順に発動し、収集したイベントと
    /// アクションが無効化されたかを返します。リアクティブは1イベントにつき1枚のみ
    /// 発動し、発動後はトラッシュへ送られます。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="trigger">発動させるトリガーの種別。</param>
    /// <param name="candidates">発動を確認するカード。</param>
    /// <param name="buildContext">候補ごとに効果実行コンテキストを生成する関数。</param>
    /// <returns>アクションが無効化されたかと、収集したイベント。</returns>
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

        bool reactiveActivated = false;

        foreach (var candidate in eligible)
        {
            var reactive = AsReactive(candidate, cc);

            // リアクティブは1イベントにつき、最も早く配置された1枚のみ発動する。
            if (reactive is not null && reactiveActivated) { continue; }

            var handler = effects.Get(candidate.CardId, trigger)!;
            var effectCtx = buildContext(candidate);
            // 発動中の trigger を ctx に注入する。choice op が選択待ちを state に保存するときに参照される。
            effectCtx.Trigger = trigger;
            var result = handler(effectCtx);

            // 発動条件を満たさなかったリアクティブは発動扱いにせず（消費しない）、次の候補へ。
            if (result.HasGuardFailed) { continue; }

            events.AddRange(result.Events);

            if (reactive is not null)
            {
                reactiveActivated = true;
                ReactiveCard.Consume(state, reactive, candidate.OwnerNum);
            }

            // choice op が ChoiceData 不足で suspend したら state に保存して後続を止める。
            // 後続候補も同イベント契機なので、resume 後に必要であれば再走査する設計とする。
            if (result.PendingChoice is not null)
            {
                state.PendingEffectChoice = result.PendingChoice;
                break;
            }

            // アクションがキャンセルされたら、同じイベントを契機とする後続の効果は発火しない。
            // キャンセルでアクションが「発生しなかった」扱いになり、後続効果は発動契機を失う。
            if (result.ShouldCancelAction)
            {
                cancelled = true;
                break;
            }
        }

        return (cancelled, events);
    }

    /// <summary>
    /// 候補がリアクティブならその実体を、そうでなければ null を返します。
    /// </summary>
    private static DeployedSupport? AsReactive(EventTriggerCandidate candidate, ICardCache cc) =>
        candidate.Support is { } support
            && cc.MustGet(support.CardID).CardType == CardTypes.Reactive
            ? support
            : null;
}

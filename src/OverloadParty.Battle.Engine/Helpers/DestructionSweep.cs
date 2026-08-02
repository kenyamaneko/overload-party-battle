using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// 実効可用性が 0 以下のリソースを状態ベースで破壊し、破壊のライフサイクル
/// (SLA ペナルティ減算・on_destroy 発火・アタッチメントの連れトラッシュ・パッシブ効果再計算) を一元化する。
/// </summary>
public static class DestructionSweep
{
    /// <summary>
    /// 両フィールドを走査し、実効可用性が 0 以下のリソースを全て破壊する。
    /// ターンプレイヤーのフィールドから先に、フィールド内はフロントエンドのスロット順からバックエンドのスロット順で処理し、
    /// 破壊で盤面が変わるたびに再走査してカスケードを解決する。選択待ちが発生したら中断する。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>破壊のライフサイクルで発生したイベント一覧。</returns>
    public static List<GameEvent> Run(BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        var (events, anyDestroyed) = RunCore(state, game, cc, effects);

        if (anyDestroyed)
        {
            PassiveRecalculator.Recalculate(state, game, cc, effects);
        }

        return events;
    }

    /// <summary>
    /// 実効可用性に依らず、指定したリソースを明示的に破壊する。破壊後に走査を続け、
    /// 巻き添えで生じたカスケードも解決する。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="ownerNum">破壊するリソースの所有プレイヤー番号。</param>
    /// <param name="resource">破壊するリソース。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>破壊のライフサイクルとカスケードで発生したイベント一覧。</returns>
    public static List<GameEvent> DestroyNow(
        BattleGameState state, Game game, long ownerNum, DeployedResource resource,
        ICardCache cc, IEffectRegistry effects)
    {
        var (destroyEvents, destroyed) = DestroyOne(state, game, ownerNum, resource, cc, effects);
        var (cascadeEvents, cascadeDestroyed) = RunCore(state, game, cc, effects);

        var events = new List<GameEvent>(destroyEvents);
        events.AddRange(cascadeEvents);

        // DestroyOne 分とカスケード分を合わせて 1 回だけ発火する (Run() 経由だと二重発火しうるため RunCore を直接使う)。
        if (destroyed || cascadeDestroyed)
        {
            PassiveRecalculator.Recalculate(state, game, cc, effects);
        }

        return events;
    }

    /// <summary>
    /// 実効可用性 0 以下のリソースを、パッシブ効果の再計算を挟まずに走査・破壊する。
    /// </summary>
    private static (List<GameEvent> Events, bool AnyDestroyed) RunCore(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        var events = new List<GameEvent>();
        bool anyDestroyed = false;

        while (state.PendingEffectChoice is null)
        {
            var next = FindNextZeroed(state);
            if (next is null) { break; }

            var (destroyEvents, destroyed) = DestroyOne(state, game, next.Value.OwnerNum, next.Value.Resource, cc, effects);
            events.AddRange(destroyEvents);
            anyDestroyed |= destroyed;
        }

        return (events, anyDestroyed);
    }

    private static (long OwnerNum, DeployedResource Resource)? FindNextZeroed(BattleGameState state)
    {
        long turnPlayer = state.ActivePlayer;
        long otherPlayer = state.OpponentOf(turnPlayer);

        return FindZeroedOnField(state, turnPlayer) ?? FindZeroedOnField(state, otherPlayer);
    }

    private static (long OwnerNum, DeployedResource Resource)? FindZeroedOnField(BattleGameState state, long ownerNum)
    {
        var zeroed = FieldHelpers.AllResources(state.GetField(ownerNum)).FirstOrDefault(r => r.EffectiveAV <= 0);
        if (zeroed is null) { return null; }

        return (ownerNum, zeroed);
    }

    /// <summary>
    /// リソース 1 体を破壊コアに通し、本体とアタッチメントの on_destroy を配置順で発動する。
    /// </summary>
    private static (List<GameEvent> Events, bool Destroyed) DestroyOne(
        BattleGameState state, Game game, long ownerNum, DeployedResource resource,
        ICardCache cc, IEffectRegistry effects)
    {
        var field = state.GetField(ownerNum);
        var attachments = field.Support
            .Where(a => a.TargetInstanceID == resource.InstanceID)
            .ToList();

        if (!ResourceHelpers.DestroyResource(state, ownerNum, field, resource, cc))
        {
            return ([], false);
        }

        var events = FireOnDestroy(
            state, game, ownerNum, EventTriggerCandidate.ForResource(resource, ownerNum), resource, field, cc, effects);

        foreach (var attachment in attachments)
        {
            // ホストの on_destroy が選択待ちへ遷移したら、アタッチメント分はこの回では発動しない。
            if (state.PendingEffectChoice is not null) { break; }

            events.AddRange(FireOnDestroy(
                state, game, ownerNum, EventTriggerCandidate.ForSupport(attachment, ownerNum), resource, field, cc, effects));
        }

        return (events, true);
    }

    /// <summary>
    /// 破壊されたカードの on_destroy を、所有者のフィールド (自身＋他リソース＋サポートゾーン) に配置順で発動する。
    /// </summary>
    private static List<GameEvent> FireOnDestroy(
        BattleGameState state, Game game, long ownerNum, EventTriggerCandidate destroyedCandidate,
        DeployedResource? target, Field ownerField, ICardCache cc, IEffectRegistry effects)
    {
        var candidates = new List<EventTriggerCandidate>();

        if (effects.Has(destroyedCandidate.CardId, TriggerType.OnDestroy))
        {
            candidates.Add(destroyedCandidate);
        }

        candidates.AddRange(FieldHelpers.AllFaceUpResources(ownerField)
            .Select(res => EventTriggerCandidate.ForResource(res, ownerNum)));
        candidates.AddRange(FieldHelpers.AllTriggerableSupports(ownerField)
            .Select(sup => EventTriggerCandidate.ForSupport(sup, ownerNum)));

        var (_, events) = EventTriggerFiring.Fire(
            state, game, effects, cc, TriggerType.OnDestroy, candidates,
            candidate => new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = ownerNum,
                Source = candidate.Resource,
                SupSource = candidate.Support,
                Target = target,
                EventOwnerNum = ownerNum,
                CardCache = cc,
                Effects = effects,
            });

        return events;
    }
}

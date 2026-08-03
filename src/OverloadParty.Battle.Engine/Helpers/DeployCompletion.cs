using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// 表向きで場に入ったカードの稼働開始処理を、配置経路に依らず一元化する。
/// </summary>
public static class DeployCompletion
{
    /// <summary>
    /// 表向きで場に入ったリソースの稼働開始処理を行う。相手の on_deploy 誘発を先に解決し、
    /// キャンセルされた場合はリソースを場から除いてトラッシュへ送る。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="ownerNum">リソースを稼働させたプレイヤー番号。</param>
    /// <param name="resource">表向きで場に入ったリソース。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>デプロイがキャンセルされたかと、発火したイベント。</returns>
    public static (bool Cancelled, List<GameEvent> Events) CompleteResource(
        BattleGameState state, Game game, long ownerNum, DeployedResource resource,
        ICardCache cc, IEffectRegistry effects)
    {
        var events = new List<GameEvent>();

        // 相手の on_deploy 誘発はデプロイをキャンセルしうるため先に解決し、
        // キャンセルされなかった場合のみデプロイされたカード自身の効果を走らせる（2 段解決）。
        var (cancelled, triggerEvents) = FireOnDeployTriggers(
            state, game, ownerNum, cc, effects, resource, supSource: null);
        events.AddRange(triggerEvents);

        if (cancelled)
        {
            var field = state.GetField(ownerNum);
            FieldHelpers.RemoveResourceFromField(field, resource.InstanceID);
            CardMoveHelpers.AddToTrash(state, ownerNum, resource.CardID, resource.InstanceID, resource.ArtNo);
            PassiveRecalculator.Recalculate(state, game, cc, effects);
            return (true, events);
        }

        // TODO(#130): on_deploy トリガーの発火タイミング分割 (配置時/稼働時) で本経路の振り分けが変わる。
        if (effects.Has(resource.CardID, TriggerType.OnDeploy))
        {
            var handler = effects.Get(resource.CardID, TriggerType.OnDeploy)!;
            var result = handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = ownerNum,
                Source = resource,
                Target = resource,
                EventOwnerNum = ownerNum,
                CardCache = cc,
                Effects = effects,
                Trigger = TriggerType.OnDeploy,
                EffectCardId = resource.CardID,
                EffectInstanceId = resource.InstanceID,
            });
            events.AddRange(result.Events);
            if (result.PendingChoice is { } pendingChoice)
            {
                state.PendingEffectChoice = pendingChoice;
            }
        }

        PassiveRecalculator.Recalculate(state, game, cc, effects);

        // キャンセルされたデプロイは「発生しなかった」扱いになるため、稼働実績はキャンセル解決の後に立てる。
        state.SetHasOperated(ownerNum, true);

        return (false, events);
    }

    /// <summary>
    /// 表向きで場に入ったサポートカードの稼働開始処理を行う。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="ownerNum">サポートカードを稼働させたプレイヤー番号。</param>
    /// <param name="support">表向きで場に入ったサポートカード。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>発火したイベント。</returns>
    public static List<GameEvent> CompleteSupport(
        BattleGameState state, Game game, long ownerNum, DeployedSupport support,
        ICardCache cc, IEffectRegistry effects)
    {
        var events = new List<GameEvent>();

        var (cancelled, triggerEvents) = FireOnDeployTriggers(
            state, game, ownerNum, cc, effects, deployedResource: null, support);
        events.AddRange(triggerEvents);

        if (cancelled)
        {
            PassiveRecalculator.Recalculate(state, game, cc, effects);
            return events;
        }

        // TODO(#130): on_deploy トリガーの発火タイミング分割 (配置時/稼働時) で本経路の振り分けが変わる。
        if (effects.Has(support.CardID, TriggerType.OnDeploy))
        {
            var handler = effects.Get(support.CardID, TriggerType.OnDeploy)!;
            var result = handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = ownerNum,
                SupSource = support,
                EventOwnerNum = ownerNum,
                CardCache = cc,
                Effects = effects,
                Trigger = TriggerType.OnDeploy,
                EffectCardId = support.CardID,
                EffectInstanceId = support.InstanceID,
            });
            events.AddRange(result.Events);
            if (result.PendingChoice is { } pendingChoice)
            {
                state.PendingEffectChoice = pendingChoice;
            }
        }

        PassiveRecalculator.Recalculate(state, game, cc, effects);

        return events;
    }

    /// <summary>
    /// on_deploy の Stage 1 として相手サポートゾーンの誘発を発火します。
    /// </summary>
    private static (bool Cancelled, List<GameEvent> Events) FireOnDeployTriggers(
        BattleGameState state, Game game, long ownerNum, ICardCache cc, IEffectRegistry effects,
        DeployedResource? deployedResource, DeployedSupport? supSource)
    {
        var opponentNum = state.OpponentOf(ownerNum);
        var opponentField = state.GetField(opponentNum);

        var candidates = FieldHelpers.AllSupports(opponentField)
            .Select(s => EventTriggerCandidate.ForSupport(s, opponentNum))
            .ToList();

        return EventTriggerFiring.Fire(
            state, game, effects, cc, TriggerType.OnDeploy, candidates,
            candidate => new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = opponentNum,
                SupSource = candidate.Support,
                Source = deployedResource,
                Target = deployedResource,
                EventOwnerNum = ownerNum,
                CardCache = cc,
                Effects = effects,
                Trigger = TriggerType.OnDeploy,
                EffectCardId = candidate.CardId,
            });
    }
}

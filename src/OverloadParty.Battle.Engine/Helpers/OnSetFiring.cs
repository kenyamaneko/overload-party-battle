using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// 場に入ったカード自身の配置時効果を、配置経路に依らず一元化して発火する。
/// </summary>
public static class OnSetFiring
{
    /// <summary>
    /// 場に入ったカード自身の配置時効果を発火します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="ownerNum">カードを置いたプレイヤー番号。</param>
    /// <param name="cardId">置いたカードのカード ID。</param>
    /// <param name="instanceId">置いたカードのインスタンス ID。</param>
    /// <param name="source">効果の発火元リソース。アタッチメントでは装備先のリソース、サポートカードでは null。</param>
    /// <param name="supSource">効果の発火元サポートカード。リソースでは null。</param>
    /// <param name="choiceData">プレイヤーが同じアクションで送ってきた選択値。無ければ null。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>発火したイベントと、効果が選択を要求して中断した場合の選択待ち。選択待ちはゲーム状態にも載せる。</returns>
    public static (List<GameEvent> Events, PendingEffectChoice? PendingChoice) Fire(
        BattleGameState state, Game game, long ownerNum,
        string cardId, string instanceId,
        DeployedResource? source, DeployedSupport? supSource,
        Dictionary<string, object>? choiceData,
        ICardCache cc, IEffectRegistry effects)
    {
        if (!effects.Has(cardId, TriggerType.OnSet))
        {
            return ([], null);
        }

        var handler = effects.Get(cardId, TriggerType.OnSet)!;
        var result = handler(new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = ownerNum,
            Source = source,
            SupSource = supSource,
            Target = source,
            EventOwnerNum = ownerNum,
            CardCache = cc,
            ChoiceData = choiceData,
            Effects = effects,
            Trigger = TriggerType.OnSet,
            EffectCardId = cardId,
            EffectInstanceId = instanceId,
        });

        // 呼び出し側が載せ忘れると選択待ちが黙って消えるため、ゲーム状態への反映は発火元で行う。
        if (result.PendingChoice is not null)
        {
            state.PendingEffectChoice = result.PendingChoice;
        }

        return (result.Events, result.PendingChoice);
    }

    /// <summary>
    /// 配置時効果が選択待ちのまま、同じアクションで稼働開始処理へ進もうとしていないかを確かめます。
    /// </summary>
    /// <param name="onSetChoice">配置時効果が返した選択待ち。中断していなければ null。</param>
    /// <param name="cardId">配置したカードのカード ID。</param>
    public static void RejectDeferredChoiceBeforeDeployCompletion(
        PendingEffectChoice? onSetChoice, string cardId)
    {
        if (onSetChoice is null) { return; }

        // TODO(#281): 配置時の選択待ちを跨いで稼働開始処理を再開できるようにする。
        throw new InvalidOperationException(
            $"deferred on_set choice for {cardId} cannot suspend a deployment that completes in the same action (not yet supported)");
    }
}

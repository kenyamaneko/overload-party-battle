using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// 保留中の reactive 選択を、ChooserPlayerNum のプレイヤーが送ってきた選択値で解決する。
/// 効果ハンドラを再実行し、ChoiceData に選択値を載せた状態で choice op を成立させる。
/// </summary>
public static class ResolvePendingChoiceProcessor
{
    /// <summary>
    /// 保留中の reactive 選択を解決します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="playerNum">アクションを送ったプレイヤー番号。</param>
    /// <param name="req">選択値を含むリクエスト。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>効果再実行で生じたイベントと state 更新可否を含むアクション結果。</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        ResolvePendingChoiceRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var pending = state.PendingReactiveChoice
            ?? throw new GameRuleException("No pending reactive choice");

        if (pending.ChooserPlayerNum != playerNum)
        {
            throw new GameRuleException("Pending choice is for a different player");
        }

        if (!pending.Candidates.Contains(req.ChosenId))
        {
            throw new GameRuleException($"Chosen id '{req.ChosenId}' is not in candidates");
        }

        var handler = effects.Get(pending.ReactiveCardId, pending.Trigger)
            ?? throw new InvalidOperationException(
                $"Handler not found: {pending.ReactiveCardId} / {pending.Trigger}");

        // resume context から source / target を解決。on_destroy のように対象がフィールドに
        // 居ない場合は suspend 時のスナップショットにフォールバックする。
        var source = (pending.SourceInstanceId is not null
            ? FindResourceInState(state, pending.SourceInstanceId)
            : null) ?? pending.SourceSnapshot;
        var target = (pending.TargetInstanceId is not null
            ? FindResourceInState(state, pending.TargetInstanceId)
            : null) ?? pending.TargetSnapshot;
        CardDefinition? incidentCard = pending.IncidentCardId is not null
            ? cc.MustGet(pending.IncidentCardId)
            : null;

        // ChoiceData に選択値を載せて handler を再実行する。
        // ハンドラは ChoiceData が揃った状態で choice op を成立させ、最後まで進む。
        var choiceData = new Dictionary<string, object>
        {
            [pending.ChoiceKey] = req.ChosenId,
        };

        // pending を先にクリアしておく。再実行で choice op が再度 ChoiceData 不足を検知することは無いが、
        // 何らかの理由で同 handler が別の選択を要求した場合に古い pending が残り続けないよう先消し。
        state.PendingReactiveChoice = null;

        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            // 効果再実行はリアクティブの所有者視点で行う。EventOwnerNum はトリガーイベントを
            // 起こしたプレイヤー (例えば攻撃宣言時は攻撃側) で別管理。
            PlayerNum = pending.OwnerPlayerNum,
            Source = source,
            Target = target,
            CardCache = cc,
            ChoiceData = choiceData,
            EventOwnerNum = pending.EventOwnerNum,
            IncidentCard = incidentCard,
            EventDamage = pending.EventDamage,
            Effects = effects,
        };

        var result = handler(ctx);

        return new ActionResult
        {
            Events = result.Events,
            StateUpdated = true,
        };
    }

    private static DeployedResource? FindResourceInState(BattleGameState state, string instanceId) =>
        FieldHelpers.FindResourceByID(state.Player1Field, instanceId)
        ?? FieldHelpers.FindResourceByID(state.Player2Field, instanceId);
}

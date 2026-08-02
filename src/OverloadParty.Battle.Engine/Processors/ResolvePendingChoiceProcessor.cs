using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// 保留中の効果選択を、ChooserPlayerNum のプレイヤーが送ってきた選択値で解決する。
/// 効果ハンドラを再実行し、ChoiceData に選択値を載せた状態で choice op を成立させる。
/// </summary>
public static class ResolvePendingChoiceProcessor
{
    /// <summary>
    /// 保留中の効果選択を解決します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="playerNum">アクションを送ったプレイヤー番号。</param>
    /// <param name="req">選択値を含むリクエスト。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>効果再実行で生じたイベントと state 更新可否を含むアクション結果。</returns>
    public static ActionResult Process(
        BattleGameState state,
        Game game,
        long playerNum,
        ResolvePendingChoiceRequest req,
        ICardCache cc,
        IEffectRegistry effects)
    {
        var pending = state.PendingEffectChoice
            ?? throw new GameRuleException("No pending effect choice");

        if (pending.ChooserPlayerNum != playerNum)
        {
            throw new GameRuleException("Pending choice is for a different player");
        }

        if (!pending.Candidates.Contains(req.ChosenId))
        {
            throw new GameRuleException($"Chosen id '{req.ChosenId}' is not in candidates");
        }

        var handler = effects.Get(pending.EffectCardId, pending.Trigger)
            ?? throw new InvalidOperationException(
                $"Handler not found: {pending.EffectCardId} / {pending.Trigger}");

        CardDefinition? incidentCard = pending.IncidentCardId is not null
            ? cc.MustGet(pending.IncidentCardId)
            : null;

        // ChoiceData に選択値を載せて handler を再実行する。
        // ハンドラは ChoiceData が揃った状態で choice op を成立させ、最後まで進む。
        var choiceData = new Dictionary<string, object>
        {
            [pending.ChoiceKey] = req.ChosenId,
        };

        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            // 効果再実行は効果の所有者視点で行う。EventOwnerNum はトリガーイベントを
            // 起こしたプレイヤー (例えば攻撃宣言時は攻撃側) で別管理。
            PlayerNum = pending.OwnerPlayerNum,
            Source = pending.Source,
            Target = pending.Target,
            CardCache = cc,
            ChoiceData = choiceData,
            EventOwnerNum = pending.EventOwnerNum,
            IncidentCard = incidentCard,
            EventDamage = pending.EventDamage,
            Effects = effects,
            Trigger = pending.Trigger,
            EffectCardId = pending.EffectCardId,
            EffectInstanceId = pending.EffectInstanceId,
            ResumeFromOpIndex = pending.ResumeOpIndex,
        };

        var result = handler(ctx);

        // 再実行した効果がさらに選択を要求する多段選択を捨てずに繋ぐため、新たな選択待ちを伝播する。
        state.PendingEffectChoice = result.PendingChoice;

        // 中断していた on_destroy から再開したケースで、残っていた破壊対象を回収する。
        var events = new List<GameEvent>(result.Events);
        events.AddRange(DestructionSweep.Run(state, game, cc, effects));

        events.AddRange(ResumeSuspendedAttack(state, game, cc, effects, pending, result));

        return new ActionResult
        {
            Events = events,
        };
    }

    /// <summary>
    /// 攻撃宣言時の選択で保留していた攻撃を、解決結果を反映して確定させます。
    /// 多段選択でまだ選択待ちが残っている間は確定させません。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="pending">解決した選択待ち。</param>
    /// <param name="result">効果を再実行した結果。</param>
    /// <returns>攻撃の確定で生じたイベント。攻撃が保留されていなければ空。</returns>
    private static List<GameEvent> ResumeSuspendedAttack(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects,
        PendingEffectChoice pending, EffectResult result)
    {
        if (pending.Trigger != TriggerType.OnAttackDeclared || result.PendingChoice is not null)
        {
            return [];
        }

        if (pending.Source is not { } attacker || pending.Target is not { } defender
            || pending.EventOwnerNum is not { } attackerPlayerNum || pending.EventDamage is not { } rawDamage)
        {
            throw new InvalidOperationException(
                "suspended attack requires the attacker, the defender, the attacking player and the declared damage");
        }

        return AttackProcessor.ResumeAfterDeclaredChoice(
            state, game, attackerPlayerNum, attacker, defender, rawDamage,
            result.ShouldCancelAction, cc, effects);
    }
}

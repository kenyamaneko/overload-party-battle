using System.Linq;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// DrawPhaseProcessor はデプロイカウントダウンとカードドローを含むドローフェーズを処理します
/// </summary>
public static class DrawPhaseProcessor
{
    /// <summary>
    /// Executes draw phase logic: counts down deploy timers, draws a card, and checks win conditions.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>A game-over result if a win condition is met; otherwise <c>null</c>.</returns>
    public static GameOverResult? Process(BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        if (state.CurrentPhase != Phase.Draw) { return null; }

        ProcessDeployCountdown(state, game, cc, effects);

        if (!CanDraw(state))
        {
            return new GameOverResult(
                state.OpponentOf(state.ActivePlayer),
                WinReason.DeckOut.ToWireString());
        }

        CardMoveHelpers.DrawCards(state, state.ActivePlayer, 1);

        TurnManager.AdvancePhase(state);

        return WinConditionChecker.Check(state, game);
    }

    static bool CanDraw(BattleGameState state) =>
        state.GetRepository(state.ActivePlayer).Count > 0;

    static void ProcessDeployCountdown(BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        // カウントダウン完了 = 表向き稼働状態でフィールドに入った瞬間。ここで on_deploy を発火する。
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            if (resource.DeployingTurnsLeft > 0)
            {
                resource.DeployingTurnsLeft--;
                if (resource.DeployingTurnsLeft <= 0)
                {
                    resource.FaceUp = true;
                    state.SetHasOperated(playerNum, true);
                    FireOnDeploy(state, game, playerNum, cc, effects, source: resource, supSource: null);
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (support.DeployingTurnsLeft > 0)
            {
                support.DeployingTurnsLeft--;
                if (support.DeployingTurnsLeft <= 0)
                {
                    support.FaceUp = true;
                    FireOnDeploy(state, game, playerNum, cc, effects, source: null, supSource: support);
                }
            }
        }

        PassiveRecalculator.Recalculate(state, game, cc, effects);
    }

    /// <summary>
    /// デプロイのカウントダウン完了で稼働したカード自身の on_deploy 効果を発火します。
    /// </summary>
    static void FireOnDeploy(
        BattleGameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry effects,
        DeployedResource? source, DeployedSupport? supSource)
    {
        string cardId = source?.CardID ?? supSource!.CardID;
        if (!effects.Has(cardId, TriggerType.OnDeploy)) { return; }

        var handler = effects.Get(cardId, TriggerType.OnDeploy)!;
        var result = handler(new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = playerNum,
            Source = source,
            Target = source,
            SupSource = supSource,
            EventOwnerNum = playerNum,
            CardCache = cc,
            Effects = effects,
            Trigger = TriggerType.OnDeploy,
            EffectCardId = cardId,
            EffectInstanceId = source?.InstanceID ?? supSource!.InstanceID,
        });
        if (result.PendingChoice is not null)
        {
            // TODO(#130): on_deploy の発火タイミングを配置時/稼働時に分割し、稼働時に選択を要求する効果
            // のみがここに到達するようにした上で、resumable DrawPhase で中断・再開を支える。
            throw new InvalidOperationException(
                $"deferred on_deploy choice for {cardId} requires resumable draw phase (not yet supported)");
        }
    }
}

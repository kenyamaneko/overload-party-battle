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
    /// ドローフェーズを、デッキアウト判定・ドロー・デプロイターン経過処理の順に進め、最後に勝敗を判定します。
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>A game-over result if a win condition is met; otherwise <c>null</c>.</returns>
    public static GameOverResult? Process(BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        if (state.CurrentPhase != Phase.Draw) { return null; }

        if (!CanDraw(state))
        {
            return new GameOverResult(
                state.OpponentOf(state.ActivePlayer),
                WinReason.DeckOut.ToWireString());
        }

        CardMoveHelpers.DrawCards(state, state.ActivePlayer, 1);

        ProcessDeployCountdown(state, game, cc, effects);

        TurnManager.AdvancePhase(state);

        return WinConditionChecker.Check(state, game);
    }

    static bool CanDraw(BattleGameState state) =>
        state.GetRepository(state.ActivePlayer).Count > 0;

    static void ProcessDeployCountdown(BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        // カウントダウン完了 = 表向き稼働状態でフィールドに入った瞬間。ここで稼働開始処理を行う。
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            if (resource.DeployingTurnsLeft > 0)
            {
                resource.DeployingTurnsLeft--;
                if (resource.DeployingTurnsLeft <= 0)
                {
                    resource.FaceUp = true;
                    DeployCompletion.CompleteResource(state, game, playerNum, resource, cc, effects);
                    RejectDeferredChoice(state, resource.CardID);
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
                    DeployCompletion.CompleteSupport(state, game, playerNum, support, cc, effects);
                    RejectDeferredChoice(state, support.CardID);
                }
            }
        }

        PassiveRecalculator.Recalculate(state, game, cc, effects);
    }

    /// <summary>
    /// 稼働開始処理が選択待ちに遷移していたら、支えられない状態として拒否します。
    /// </summary>
    static void RejectDeferredChoice(BattleGameState state, string cardId)
    {
        if (state.PendingEffectChoice is null) { return; }

        // TODO(#172): 稼働時の選択待ちを中断・再開できるドローフェーズで支える。
        throw new InvalidOperationException(
            $"deferred on_deploy choice for {cardId} requires resumable draw phase (not yet supported)");
    }
}

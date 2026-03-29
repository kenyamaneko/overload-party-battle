using System.Linq;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes the draw phase including deploy countdowns, migration completion, and card draw.
/// </summary>
/// <remarks>
/// TODO: ドローフェーズの前に「スタートフェーズ」を設けることを検討する。
/// 現在はマイグレーション完了やデプロイカウントダウンがドローフェーズで処理されているが、
/// これらはドローとは独立したターン開始処理であり、別フェーズに分離すべき可能性がある。
/// </remarks>
public static class DrawPhaseProcessor
{
    /// <summary>
    /// Executes draw phase logic: counts down deploy timers, completes migrations, draws a card, and checks win conditions.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <returns>A game-over result if a win condition is met; otherwise <c>null</c>.</returns>
    public static GameOverResult? Process(GameState state, Game game, ICardCache cc, IEffectRegistry? effects = null)
    {
        if (state.CurrentPhase != Phase.Draw) { return null; }

        ProcessDeployCountdown(state, game, cc, effects);
        ProcessMigrationCompletion(state);

        if (!CanDraw(state))
        {
            return new GameOverResult(
                state.OpponentOf(state.ActivePlayer),
                WinReason.RepositoryOut.ToWireString());
        }

        CardMoveHelpers.DrawCards(state, state.ActivePlayer, 1);

        TurnManager.AdvancePhase(state);

        return WinConditionChecker.Check(state, game);
    }

    static bool CanDraw(GameState state) =>
        state.GetRepository(state.ActivePlayer).Count > 0;

    static void ProcessDeployCountdown(GameState state, Game game, ICardCache cc, IEffectRegistry? effects)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        foreach (var resource in FieldHelpers.AllResources(field))
        {
            if (resource.DeployingTurnsLeft > 0)
            {
                resource.DeployingTurnsLeft--;
                if (resource.DeployingTurnsLeft <= 0)
                {
                    resource.FaceUp = true;
                    state.SetHasHadActiveResource(playerNum, true);
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (support.DeployingTurnsLeft > 0)
            {
                support.DeployingTurnsLeft--;
                if (support.DeployingTurnsLeft <= 0
                    && effects?.Has(support.CardID, TriggerType.Deploy) == true)
                {
                    var handler = effects.Get(support.CardID, TriggerType.Deploy)!;
                    handler(new EffectContext
                    {
                        State = state,
                        Game = game,
                        PlayerNum = playerNum,
                        SupSource = support,
                        CardCache = cc,
                    });
                }
            }
        }
    }

    static void ProcessMigrationCompletion(GameState state)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        var completedMigrations = FieldHelpers.AllResources(field)
            .Where(r => r.MigratingFrom is not null
                && state.CurrentTurn - r.MigratingOnTurn >= 2)
            .ToList();

        foreach (var resource in completedMigrations)
        {
            var sourceID = resource.MigratingFrom!;
            var src = FieldHelpers.FindResourceByID(field, sourceID);
            if (src is not null)
            {
                FieldHelpers.RemoveResourceFromField(field, sourceID);
                CardMoveHelpers.AddToTrash(state, playerNum, src.CardID, sourceID, src.ArtNo);
            }

            resource.MigratingFrom = null;
            resource.MigratingOnTurn = 0;
        }
    }
}

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
    /// 稼働時の効果が選択を要求した場合は、残りの処理を積んだままドローフェーズのまま中断します。
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>勝敗が確定した場合の結果。未確定、または選択待ちで中断した場合は <c>null</c>。</returns>
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

        CollectDeployCompletions(state);

        return RunDeployCompletions(state, game, cc, effects);
    }

    /// <summary>
    /// 稼働開始処理の途中で入った選択待ちが解決された後、ドローフェーズの残りを進めます。
    /// 中断したカードが先頭に残っている状態、つまり <see cref="BattleGameState.PendingDeployCompletions"/>
    /// が空でない状態で呼びます。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>勝敗が確定した場合の結果。未確定、または再び選択待ちに入った場合は <c>null</c>。</returns>
    public static GameOverResult? ResumeDeployCompletions(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        // 中断した 1 体分は選択の解決で最後まで走っているため、先頭を取り除いてから残りを続ける。
        state.PendingDeployCompletions.RemoveAt(0);

        return RunDeployCompletions(state, game, cc, effects);
    }

    static bool CanDraw(BattleGameState state) =>
        state.GetRepository(state.ActivePlayer).Count > 0;

    /// <summary>
    /// 残デプロイターンを 1 ずつ減らし、0 に達したカードを稼働開始処理の対象として DeployOrder 昇順に積みます。
    /// </summary>
    static void CollectDeployCompletions(BattleGameState state)
    {
        var field = state.GetField(state.ActivePlayer);
        var completed = new List<(long DeployOrder, string InstanceID)>();

        foreach (var resource in FieldHelpers.AllResources(field))
        {
            if (resource.DeployingTurnsLeft <= 0) { continue; }

            resource.DeployingTurnsLeft--;
            if (resource.DeployingTurnsLeft > 0) { continue; }

            resource.FaceUp = true;
            completed.Add((resource.DeployOrder, resource.InstanceID));
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (support.DeployingTurnsLeft <= 0) { continue; }

            support.DeployingTurnsLeft--;
            if (support.DeployingTurnsLeft > 0) { continue; }

            completed.Add((support.DeployOrder, support.InstanceID));
        }

        // 稼働開始処理は相手のリアクティブを挟んで盤面を変えうるため、カウントダウンの減算を先に済ませる。
        // 減算を後回しにすると、同じドローフェーズで稼働するはずのカードが処理から漏れる。
        state.PendingDeployCompletions = [.. completed.OrderBy(c => c.DeployOrder).Select(c => c.InstanceID)];
    }

    /// <summary>
    /// 積まれた稼働開始処理を順に走らせ、全て終えたらフェーズを進めて勝敗を判定します。
    /// </summary>
    /// <returns>勝敗が確定した場合の結果。未確定、または選択待ちで中断した場合は <c>null</c>。</returns>
    static GameOverResult? RunDeployCompletions(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        while (state.PendingDeployCompletions.Count > 0)
        {
            var instanceID = state.PendingDeployCompletions[0];

            // 先に稼働したカードのリアクティブが盤面から除いた場合、稼働開始処理の対象は残っていない。
            if (FieldHelpers.FindResourceByID(field, instanceID) is { } resource)
            {
                DeployCompletion.CompleteResource(state, game, playerNum, resource, cc, effects);
            }
            else if (FieldHelpers.FindSupportByID(field, instanceID) is { } support)
            {
                DeployCompletion.CompleteSupport(state, game, playerNum, support, cc, effects);
            }

            // 稼働時の効果が選択待ちに入ったら、中断したカードを先頭に残したまま抜け、解決後に続きから進める。
            if (state.PendingEffectChoice is not null) { return null; }

            state.PendingDeployCompletions.RemoveAt(0);
        }

        PassiveRecalculator.Recalculate(state, game, cc, effects);

        TurnManager.AdvancePhase(state);

        return WinConditionChecker.Check(state, game);
    }
}

using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// TurnManager はターン交代とフェーズ遷移を管理します
/// </summary>
public static class TurnManager
{
    /// <summary>Returns <c>true</c> if <paramref name="currentTurn"/> is the first turn (turn 1 skips battle phase).</summary>
    /// <param name="currentTurn">The current turn number.</param>
    /// <returns>初手ターンであれば true。</returns>
    public static bool IsFirstTurn(long currentTurn) => currentTurn == 1;

    /// <summary>
    /// Advance to the next phase within a turn and update state.
    /// Main → Battle (or Main → End on first turn), Battle → End, Draw → Main.
    /// Returns the previous phase.
    /// </summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <returns>遷移前のフェーズ。</returns>
    public static Phase AdvancePhase(BattleGameState state)
    {
        var previous = state.CurrentPhase;
        state.CurrentPhase = previous switch
        {
            Phase.Main => IsFirstTurn(state.CurrentTurn) ? Phase.End : Phase.Battle,
            Phase.Battle => Phase.End,
            Phase.Draw => Phase.Main,
            _ => throw new GameRuleException(
                $"cannot advance from phase {previous.ToWireString()}"),
        };
        return previous;
    }

    /// <summary>
    /// Check if a given action type is allowed in the current phase.
    /// </summary>
    /// <param name="phase">判定対象のフェーズ。</param>
    /// <param name="action">判定対象のアクション種別。</param>
    /// <returns>そのフェーズで許可されているアクションなら true。</returns>
    public static bool IsActionAllowedInPhase(Phase phase, ActionType action) => phase switch
    {
        Phase.Main => action is ActionType.PlayCard or ActionType.ScaleUp or ActionType.Monetize
            or ActionType.UseIgnition or ActionType.UseInitiative or ActionType.EndPhase,
        Phase.Battle => action is ActionType.Attack or ActionType.UseIgnition
            or ActionType.EndPhase,
        Phase.End => action is ActionType.DiscardHand,
        _ => false,
    };

    /// <summary>
    /// Switch active player and start the next turn at Draw phase.
    /// </summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="clock">現在時刻の供給元。</param>
    public static void SwitchActivePlayer(BattleGameState state, IClock clock)
    {
        state.ActivePlayer = state.OpponentOf(state.ActivePlayer);
        state.CurrentTurn++;
        state.CurrentPhase = Phase.Draw;
        state.TurnStartedAt = clock.UtcNow;
    }
}

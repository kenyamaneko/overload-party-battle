using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class EndPhaseProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum, ICardCache cc)
    {
        var currentPhase = state.CurrentPhase;
        var events = new List<GameEvent>();
        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;

        switch (currentPhase)
        {
            case Phase.Main:
            {
                // First turn: skip battle → end phase directly
                if (TurnManager.IsFirstTurn(state.CurrentTurn))
                {
                    state.CurrentPhase = Phase.End;
                    return ProcessEndPhaseTransition(state, game, playerNum, cc, events, playerId, currentPhase);
                }

                // Main → Battle
                state.CurrentPhase = Phase.Battle;
                events.Add(new GameEvent
                {
                    GameID = game.GameID,
                    EventType = WireActionTypes.PhaseChange,
                    PlayerID = playerId,
                    EventData = new Dictionary<string, object>
                    {
                        ["previousPhase"] = currentPhase.ToWireString(),
                        ["currentPhase"] = state.CurrentPhase.ToWireString(),
                    }
                });
                return new ActionResult { Events = events, StateUpdated = true };
            }

            case Phase.Battle:
            {
                // Battle → End
                state.CurrentPhase = Phase.End;
                return ProcessEndPhaseTransition(state, game, playerNum, cc, events, playerId, currentPhase);
            }

            default:
                throw new GameRuleException($"cannot end phase {currentPhase.ToWireString()} manually");
        }
    }

    private static ActionResult ProcessEndPhaseTransition(
        GameState state, Game game, long playerNum, ICardCache cc,
        List<GameEvent> events, string playerId, Phase previousPhase)
    {
        bool needsDiscard = TurnManager.ProcessEndPhase(state, cc);
        var result = new ActionResult { Events = events, StateUpdated = true };

        if (needsDiscard)
        {
            result.NeedsDiscard = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = WireActionTypes.PhaseEnd,
                PlayerID = playerId,
                EventData = new Dictionary<string, object>
                {
                    ["phase"] = "end",
                    ["needsDiscard"] = true,
                }
            });
            return result;
        }

        // No discard needed: check launch failure, switch player, auto-advance
        if (WinConditionChecker.CheckLaunchFailure(state, playerNum))
        {
            var winnerNum = state.OpponentOf(playerNum);
            result.GameOver = true;
            result.WinnerNum = winnerNum;
            result.WinReason = WinReason.LaunchFailure.ToWireString();
            events.Add(MakeTurnEndEvent(game.GameID, playerId, state));
            return result;
        }

        TurnManager.SwitchActivePlayer(state);
        var (gameOver, winReason) = TurnManager.AutoAdvancePhases(state, game, cc);

        events.Add(MakeTurnEndEvent(game.GameID, playerId, state));

        if (gameOver)
        {
            result.GameOver = true;
            result.WinReason = winReason;
            var (winnerNum2, _, _) = WinConditionChecker.Check(state, game);
            result.WinnerNum = winnerNum2;
        }

        return result;
    }

    private static GameEvent MakeTurnEndEvent(string gameID, string playerId, GameState state)
    {
        return new GameEvent
        {
            GameID = gameID,
            EventType = WireActionTypes.TurnEnd,
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["phase"] = "end",
                ["nextTurn"] = state.CurrentTurn,
                ["activePlayer"] = state.ActivePlayer,
                ["currentPhase"] = state.CurrentPhase.ToWireString(),
            }
        };
    }
}

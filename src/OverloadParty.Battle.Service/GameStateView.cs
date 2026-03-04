using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Service;

/// <summary>
/// Info-hidden game state sent to a player via WebSocket.
/// </summary>
public class ClientGameState
{
    public string GameID { get; init; } = "";
    public long CurrentTurn { get; init; }
    public string CurrentPhase { get; init; } = "";
    public long ActivePlayer { get; init; }
    public bool IsMyTurn { get; init; }
    public required PlayerView MyView { get; init; }
    public required OpponentView OppView { get; init; }
}

/// <summary>
/// Player's own full state (no hiding).
/// </summary>
public class PlayerView
{
    public long PlayerNum { get; init; }
    public long Budget { get; init; }
    public long InsightPool { get; init; }
    public long TimeBank { get; init; }
    public required Field Field { get; init; }
    public required List<HandCard> Hand { get; init; }
    public int RepoCount { get; init; }
    public int TrashCount { get; init; }
    public List<AvailableAction>? AvailableActions { get; set; }
}

/// <summary>
/// Opponent's state with info hiding applied.
/// </summary>
public class OpponentView
{
    public long PlayerNum { get; init; }
    public long Budget { get; init; }
    public long InsightPool { get; init; }
    public long TimeBank { get; init; }
    public required OpponentField Field { get; init; }
    public int HandCount { get; init; }
    public int RepoCount { get; init; }
    public int TrashCount { get; init; }
}

/// <summary>
/// Opponent's field with reactive cards hidden.
/// </summary>
public class OpponentField
{
    public ResourceInstance?[] Frontend { get; init; } = new ResourceInstance?[GameConstants.SlotsPerZone];
    public ResourceInstance?[] Backend { get; init; } = new ResourceInstance?[GameConstants.SlotsPerZone];
    public HiddenSupportInstance?[] Support { get; init; } = new HiddenSupportInstance?[GameConstants.SlotsPerZone];
}

/// <summary>
/// Support instance with face-down card details hidden.
/// </summary>
public class HiddenSupportInstance
{
    public string InstanceID { get; init; } = "";
    public long? CardID { get; init; } // null if face-down
    public bool FaceDown { get; init; }
}

/// <summary>
/// Builds info-hidden game state for a specific player.
/// </summary>
public static class GameStateView
{
    public static ClientGameState Build(
        GameState state, Game game, long playerNum,
        ICardCache cc, IEffectRegistry? effects)
    {
        var oppNum = state.OpponentOf(playerNum);

        // Player's own view (full)
        var myField = state.GetField(playerNum);
        var myHand = state.GetHand(playerNum);
        var myRepo = state.GetRepository(playerNum);
        var myTrash = state.GetTrash(playerNum);
        var budget = state.GetBudget(playerNum);
        var insightPool = state.GetInsightPool(playerNum);

        var myView = new PlayerView
        {
            PlayerNum = playerNum,
            Budget = budget,
            InsightPool = insightPool,
            TimeBank = state.GetTimeBank(playerNum),
            Field = myField,
            Hand = myHand,
            RepoCount = myRepo.Count,
            TrashCount = myTrash.Count,
        };

        // Opponent view (hidden)
        var oppField = state.GetField(oppNum);
        var oppHand = state.GetHand(oppNum);
        var oppRepo = state.GetRepository(oppNum);
        var oppTrash = state.GetTrash(oppNum);

        var oppView = new OpponentView
        {
            PlayerNum = oppNum,
            Budget = state.GetBudget(oppNum),
            InsightPool = state.GetInsightPool(oppNum),
            TimeBank = state.GetTimeBank(oppNum),
            Field = BuildOpponentField(oppField),
            HandCount = oppHand.Count,
            RepoCount = oppRepo.Count,
            TrashCount = oppTrash.Count,
        };

        var cgs = new ClientGameState
        {
            GameID = game.GameID,
            CurrentTurn = state.CurrentTurn,
            CurrentPhase = state.CurrentPhase.ToWireString(),
            ActivePlayer = state.ActivePlayer,
            IsMyTurn = state.ActivePlayer == playerNum,
            MyView = myView,
            OppView = oppView,
        };

        // Compute available actions for the active player only
        if (state.ActivePlayer == playerNum && game.Status == GameStatus.Playing)
        {
            myView.AvailableActions = AvailableActions.Compute(
                state, game, playerNum,
                myField, oppField, myHand, budget, insightPool,
                cc, effects);
        }

        return cgs;
    }

    private static OpponentField BuildOpponentField(Field field)
    {
        return new OpponentField
        {
            Frontend = field.Frontend.ToArray().Select(HideResourceIfFaceDown).ToArray(),
            Backend = field.Backend.ToArray().Select(HideResourceIfFaceDown).ToArray(),
            Support = field.Support.ToArray().Select(sup => sup is null ? null : new HiddenSupportInstance
            {
                InstanceID = sup.InstanceID,
                FaceDown = sup.FaceDown,
                CardID = sup.FaceDown ? null : sup.CardID,
            }).ToArray(),
        };
    }

    private static ResourceInstance? HideResourceIfFaceDown(ResourceInstance? res)
    {
        if (res is null) return null;
        if (res.FaceUp) return res;

        // Hide all stats for face-down (still deploying) resources
        return new ResourceInstance
        {
            InstanceID = res.InstanceID,
            FaceUp = false,
            DeployingTurnsLeft = res.DeployingTurnsLeft,
        };
    }
}

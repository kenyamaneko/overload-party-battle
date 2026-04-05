using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

using GD = OverloadParty.GameData;

namespace OverloadParty.Battle.Service;

/// <summary>
/// Builds info-hidden game state for a specific player.
/// Maps engine-internal types (Models) to API-contract types (GameData).
/// </summary>
public static class GameStateView
{
    public static GD.ClientGameState Build(
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

        // Opponent data
        var oppField = state.GetField(oppNum);
        var oppHand = state.GetHand(oppNum);
        var oppRepo = state.GetRepository(oppNum);
        var oppTrash = state.GetTrash(oppNum);

        // Compute available actions BEFORE constructing PlayerView (init-only)
        GD.AvailableAction[]? availableActions = null;
        if (state.ActivePlayer == playerNum && game.Status == GameStatus.Playing)
        {
            availableActions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, myHand, budget, insightPool, cc, effects)
                .Select(MapAvailableAction)
                .ToArray();
        }

        var myView = new GD.PlayerView
        {
            PlayerNum = playerNum,
            Budget = budget,
            InsightPool = insightPool,
            TimeBank = state.GetTimeBank(playerNum),
            Field = MapField(myField),
            Hand = myHand.Select(MapUndeployedCard).ToArray(),
            RepoCount = myRepo.Count,
            TrashCount = myTrash.Count,
            Trash = myTrash.Select(MapUndeployedCard).ToArray(),
            AvailableActions = availableActions,
        };

        var oppView = new GD.OpponentView
        {
            PlayerNum = oppNum,
            Budget = state.GetBudget(oppNum),
            InsightPool = state.GetInsightPool(oppNum),
            TimeBank = state.GetTimeBank(oppNum),
            Field = BuildOpponentField(oppField, playerNum),
            HandCount = oppHand.Count,
            RepoCount = oppRepo.Count,
            TrashCount = oppTrash.Count,
            Trash = oppTrash.Select(MapUndeployedCard).ToArray(),
        };

        return new GD.ClientGameState
        {
            GameID = game.GameID,
            CurrentTurn = state.CurrentTurn,
            CurrentPhase = state.CurrentPhase.ToWireString(),
            ActivePlayer = state.ActivePlayer,
            IsMyTurn = state.ActivePlayer == playerNum,
            TurnStartedAt = state.TurnStartedAt,
            MyView = myView,
            OppView = oppView,
        };
    }

    // ─── Mapping helpers (Models → GameData) ────────────────

    private static GD.Field MapField(Field field)
    {
        return new GD.Field
        {
            Frontend = field.Frontend.ToArray().Select(r => r is null ? null : MapResource(r)).ToArray(),
            Backend = field.Backend.ToArray().Select(r => r is null ? null : MapResource(r)).ToArray(),
            Support = field.Support.ToArray().Select(s => s is null ? null : MapSupport(s)).ToArray(),
        };
    }

    private static GD.DeployedResource MapResource(DeployedResource r)
    {
        return new GD.DeployedResource
        {
            InstanceID = r.InstanceID,
            CardID = r.CardID,
            ArtNo = r.ArtNo,
            Rank = r.Rank?.ToWireString(),
            InstanceFamily = r.InstanceFamily?.ToWireString(),
            FaceUp = r.FaceUp,
            DeployingTurnsLeft = r.DeployingTurnsLeft,
            CurrentAV = r.CurrentAV,
            MaxAV = r.MaxAV,
            CurrentTP = r.CurrentTP,
            MaxTP = r.MaxTP,
            CurrentYield = r.CurrentYield,
            MaxYield = r.MaxYield,
            Damage = r.Damage,
            TemporaryEffects = r.TemporaryEffects.Select(MapTemporaryEffect).ToArray(),
            MonetizedAmount = r.MonetizedAmount,
            HasAttacked = r.HasAttacked,
            EffectUsedThisTurn = r.EffectUsedThisTurn,
            EffectUsedThisGame = r.EffectUsedThisGame,
            DeployedOnTurn = r.DeployedOnTurn,
            DeployOrder = r.DeployOrder,
            ElasticBonus = r.ElasticBonus,
            LastAttackTurn = r.LastAttackTurn,
        };
    }

    private static GD.DeployedSupport MapSupport(DeployedSupport s)
    {
        return new GD.DeployedSupport
        {
            InstanceID = s.InstanceID,
            CardID = s.CardID,
            ArtNo = s.ArtNo,
            FaceUp = s.FaceUp,
            DeployingTurnsLeft = s.DeployingTurnsLeft,
            DeployOrder = s.DeployOrder,
            EffectUsedThisTurn = s.EffectUsedThisTurn,
            EffectUsedThisGame = s.EffectUsedThisGame,
            TargetInstanceID = s.TargetInstanceID,
        };
    }

    private static GD.UndeployedCard MapUndeployedCard(UndeployedCard c)
    {
        return new GD.UndeployedCard
        {
            InstanceID = c.InstanceID,
            CardID = c.CardID,
            ArtNo = c.ArtNo,
        };
    }

    private static GD.TemporaryEffect MapTemporaryEffect(TemporaryEffect e)
    {
        return new GD.TemporaryEffect
        {
            EffectType = e.EffectType,
            Value = e.Value,
            Duration = e.Duration,
            SourceID = e.SourceID,
            Mode = e.Mode,
        };
    }

    private static GD.AvailableAction MapAvailableAction(AvailableAction a)
    {
        return new GD.AvailableAction
        {
            Type = a.Type,
            HandInstanceID = a.HandInstanceID,
            CardID = a.CardID,
            ValidZones = a.ValidZones,
            ValidTargets = a.ValidTargets,
            ChoiceOptions = a.ChoiceOptions,
            SourceInstanceID = a.SourceInstanceID,
            TargetRank = a.TargetRank,
            InstanceFamily = a.InstanceFamily,
            NeedsFamily = a.NeedsFamily,
            RemainingCapacity = a.RemainingCapacity,
            EffectTargetType = a.EffectTargetType,
            RequiredCount = a.RequiredCount,
        };
    }

    // ─── Opponent field with info hiding ────────────────────

    private static GD.OpponentField BuildOpponentField(Field field, long viewerPlayerNum)
    {
        return new GD.OpponentField
        {
            Frontend = field.Frontend.ToArray().Select(HideResourceIfFaceDown).ToArray(),
            Backend = field.Backend.ToArray().Select(HideResourceIfFaceDown).ToArray(),
            Support = field.Support.ToArray().Select(sup =>
            {
                if (sup is null) { return null; }

                bool peeked = !sup.FaceUp && sup.PeekedBy.Contains(viewerPlayerNum);
                return new GD.HiddenDeployedSupport
                {
                    InstanceID = sup.InstanceID,
                    FaceDown = !sup.FaceUp,
                    CardID = sup.FaceUp || peeked ? sup.CardID : null,
                    ArtNo = sup.FaceUp || peeked ? sup.ArtNo : 0,
                    Peeked = peeked,
                };
            }).ToArray(),
        };
    }

    private static GD.DeployedResource? HideResourceIfFaceDown(DeployedResource? res)
    {
        if (res is null) { return null; }
        if (res.FaceUp) { return MapResource(res); }

        // Hide all stats for face-down (still deploying) resources
        return new GD.DeployedResource
        {
            InstanceID = res.InstanceID,
            FaceUp = false,
            DeployingTurnsLeft = res.DeployingTurnsLeft,
        };
    }
}

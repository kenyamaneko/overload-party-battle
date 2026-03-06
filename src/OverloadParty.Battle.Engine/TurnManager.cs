using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Manages turn and phase transitions, draw phase processing, and end-of-turn logic.
/// </summary>
public static class TurnManager
{
    public static bool IsFirstTurn(long currentTurn) => currentTurn == 1;

    /// <summary>
    /// Process the draw phase: draw 1 card from repository.
    /// Throws GameRuleException if repository is empty (repository_out loss).
    /// </summary>
    public static void ProcessDrawPhase(GameState state)
    {
        var playerNum = state.ActivePlayer;
        var repo = state.GetRepository(playerNum);

        if (!repo.Any())
            throw new GameRuleException("repository_out");

        // Draw top card
        var drawnCard = repo.First();
        repo.Remove(drawnCard);

        // Assign new instance ID and add to hand
        var hand = state.GetHand(playerNum);
        hand.Add(new HandCard
        {
            InstanceID = state.NextInstanceID(),
            CardID = drawnCard.CardID
        });
    }

    /// <summary>
    /// Process the end phase: maintenance cost, yield generation, cleanup.
    /// Returns true if the player needs to discard (hand > 6).
    /// </summary>
    public static bool ProcessEndPhase(GameState state, ICardCache cc)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);
        long budget = state.GetBudget(playerNum);

        // 1. Collect maintenance cost from all face-up resources
        long totalMC = 0;
        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (resource.MigratingFrom is not null) continue; // Migrating resources don't cost

            var card = cc.MustGet(resource.CardID);
            long mc;

            if (card.Elastic)
            {
                // Elastic MC: max(0, intrinsicStat - freeTier) * costPerRequest / 100
                long intrinsic = card.IsComputeType ? card.BaseThroughput : card.BaseYield;
                long rankMult = GameConstants.RankMultiplier(resource.Rank);
                long scaledStat = intrinsic * rankMult + resource.ElasticBonus;
                mc = Math.Max(0, scaledStat - card.FreeTier) * card.CostPerRequest / 100;
            }
            else
            {
                mc = card.MaintenanceCost * GameConstants.RankMultiplier(resource.Rank);
            }

            totalMC += mc;
        }

        budget -= totalMC;
        state.SetBudget(playerNum, budget);

        // 2. Generate yield from backend data resources
        long totalYield = 0;
        foreach (var res in field.Backend)
        {
            if (!res.FaceUp || res.MigratingFrom is not null) continue;

            var card = cc.Get(res.CardID);
            if (card is null || !card.IsDataType) continue;

            long effectiveYield = StatCalculator.CalculateEffectiveYield(res, field, cc);
            totalYield += effectiveYield;

            // Apply elastic scaling if applicable
            if (card.Elastic && card.ElasticIncrement > 0)
            {
                res.ElasticBonus += card.ElasticIncrement;
            }
        }

        long insightPool = state.GetInsightPool(playerNum);
        state.SetInsightPool(playerNum, insightPool + totalYield);

        // 3. Remove expired temporary effects
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.TemporaryEffects.RemoveAll(e =>
                e.Duration is "this_turn" or "until_next_own_turn_end");
        }

        // 4. Reset per-turn flags
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.HasAttacked = false;
            resource.EffectUsedThisTurn = false;
            resource.MonetizedAmount = 0;
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            support.EffectUsedThisTurn = false;
        }

        field.IncidentPlayedThisTurn = false;

        // 5. Check if discard is needed
        var hand = state.GetHand(playerNum);
        return hand.Count > GameConstants.HandLimit;
    }

    /// <summary>
    /// Switch active player and advance to the next turn's draw phase.
    /// </summary>
    public static void SwitchActivePlayer(GameState state)
    {
        state.ActivePlayer = state.OpponentOf(state.ActivePlayer);
        state.CurrentTurn++;
        state.CurrentPhase = Phase.Draw;
    }

    /// <summary>
    /// Process deploy countdowns and migration completion at the start of a turn.
    /// </summary>
    public static void ProcessDeployCountdown(GameState state)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);

        // Decrement deploy countdown for resources
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            if (resource.DeployingTurnsLeft > 0)
            {
                resource.DeployingTurnsLeft--;
                if (resource.DeployingTurnsLeft <= 0)
                {
                    resource.FaceUp = true;
                    field.HasHadActiveResource = true;
                }
            }
        }

        // Decrement deploy countdown for support cards
        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (support.DeployingTurnsLeft > 0)
            {
                support.DeployingTurnsLeft--;
            }
        }
    }

    /// <summary>
    /// Complete any pending migrations for the active player.
    /// Returns list of completed migration events.
    /// </summary>
    public static List<(string SourceInstanceID, string TargetInstanceID, long TargetCardID)> ProcessMigrationCompletion(
        GameState state)
    {
        var playerNum = state.ActivePlayer;
        var field = state.GetField(playerNum);
        var completions = new List<(string, string, long)>();

        foreach (var resource in FieldHelpers.AllResources(field))
        {
            if (resource.MigratingFrom is not { } sourceID) continue;

            // Migration completes after 2 full turns
            if (state.CurrentTurn - resource.MigratingOnTurn < 2) continue;

            // Find and remove the source resource
            var src = FieldHelpers.FindResourceByID(field, sourceID);
            if (src is not null)
            {
                FieldHelpers.RemoveResourceFromField(field, sourceID);
                FieldHelpers.AddToTrash(state, playerNum, src.CardID, sourceID);
            }

            completions.Add((sourceID, resource.InstanceID, resource.CardID));
            resource.MigratingFrom = null;
            resource.MigratingOnTurn = 0;
        }

        return completions;
    }

    /// <summary>
    /// Auto-advance through phases that don't require player input.
    /// Returns (gameOver, winReason) if the game ends during auto-advance.
    /// </summary>
    public static (bool GameOver, string? WinReason) AutoAdvancePhases(
        GameState state, Game game, ICardCache cc)
    {
        if (state.CurrentPhase != Phase.Draw) return (false, null);

        // 1. Deploy countdown
        ProcessDeployCountdown(state);

        // 2. Migration completion
        ProcessMigrationCompletion(state);

        // 3. Add per-turn budget
        var playerNum = state.ActivePlayer;
        long budget = state.GetBudget(playerNum);
        state.SetBudget(playerNum, budget + GameConstants.PerTurnBudget);

        // 4. Draw card
        try
        {
            ProcessDrawPhase(state);
        }
        catch (GameRuleException ex) when (ex.Message == "repository_out")
        {
            // Repository empty = loss for active player
            return (true, WinReason.RepositoryOut.ToWireString());
        }

        // 5. Advance to main phase
        state.CurrentPhase = Phase.Main;

        // 6. Check win conditions after draw
        var (winnerNum, reason, gameOver) = WinConditionChecker.Check(state, game);
        if (gameOver)
            return (true, reason);

        return (false, null);
    }
}

using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes monetize actions that distribute insight yield from backend compute resources into budget.
/// </summary>
public static class MonetizeProcessor
{
    /// <summary>
    /// Distributes insight from backend compute resources to the player's budget.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number performing the action.</param>
    /// <param name="req">The monetize request containing distribution details.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <returns>The action result containing the monetize event and state update flag.</returns>
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        MonetizeRequest req, ICardCache cc)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn))
        {
            throw new GameRuleException("cannot distribute yield on first turn");
        }

        if (!req.Distributions.Any())
        {
            throw new GameRuleException("no distributions provided");
        }

        var field = state.GetField(playerNum);
        long insightPool = state.GetInsightPool(playerNum);
        long budget = state.GetBudget(playerNum);

        // Validate all distributions before mutating any state
        var validated = req.Distributions
            .Select(dist => (dist, ValidateDistribution(field, dist, cc)))
            .ToList();

        long totalDistributed = req.Distributions.Sum(d => d.Amount);
        if (totalDistributed > insightPool)
        {
            throw new GameRuleException($"total distribution {totalDistributed} exceeds insight pool {insightPool}");
        }

        // Apply mutations after all validation passes
        foreach (var (dist, (resource, card)) in validated)
        {
            resource.MonetizedAmount += dist.Amount;

            // Elastic scaling
            if (card.Elastic && card.ElasticIncrement > 0)
            {
                StatCalculator.ApplyElasticBonus(resource, card);
            }
        }

        state.SetInsightPool(playerNum, insightPool - totalDistributed);
        state.SetBudget(playerNum, budget + totalDistributed);

        var playerId = game.GetPlayerID(playerNum);
        return new ActionResult
        {
            Events =
            [
                new GameEvent
                {
                    GameID = game.GameID,
                    EventType = WireActionTypes.Monetize,
                    PlayerID = playerId,
                    EventData = new MonetizeEventData
                    {
                        TotalAmount = totalDistributed,
                    }.ToDictionary()
                }
            ],
            StateUpdated = true,
        };
    }

    private static (ResourceInstance Resource, CardDefinition Card) ValidateDistribution(
        Field field, MonetizeDistribution dist, ICardCache cc)
    {
        if (dist.Amount <= 0)
        {
            throw new GameRuleException("distribution amount must be positive");
        }

        var resource = FieldHelpers.FindResourceByID(field, dist.InstanceID)
            ?? throw new GameRuleException($"resource {dist.InstanceID} not found");

        if (FieldHelpers.FindResourceZone(field, dist.InstanceID) != Zone.Backend)
        {
            throw new GameRuleException("can only distribute yield from backend resources");
        }

        var card = cc.MustGet(resource.CardID);
        if (!card.IsComputeType)
        {
            throw new GameRuleException("can only distribute yield from compute resources");
        }

        long effectiveTP = StatCalculator.CalculateEffectiveTP(resource, field, cc);
        long remaining = effectiveTP - resource.MonetizedAmount;
        if (dist.Amount > remaining)
        {
            throw new GameRuleException($"distribution amount {dist.Amount} exceeds remaining capacity {remaining}");
        }

        return (resource, card);
    }
}

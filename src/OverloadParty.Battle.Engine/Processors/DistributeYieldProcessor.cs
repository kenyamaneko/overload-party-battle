using System.Linq;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class DistributeYieldProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        DistributeYieldRequest req, ICardCache cc)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn))
            throw new GameRuleException("cannot distribute yield on first turn");

        if (!req.Distributions.Any())
            throw new GameRuleException("no distributions provided");

        var field = state.GetField(playerNum);
        long insightPool = state.GetInsightPool(playerNum);
        long budget = state.GetBudget(playerNum);
        long totalDistributed = 0;

        foreach (var dist in req.Distributions)
        {
            if (dist.Amount <= 0)
                throw new GameRuleException("distribution amount must be positive");

            var result = FieldHelpers.FindResourceByID(field, dist.InstanceID);
            if (result is null)
                throw new GameRuleException($"resource {dist.InstanceID} not found");

            var (resource, zone) = result.Value;
            if (zone != Zone.Backend)
                throw new GameRuleException("can only distribute yield from backend resources");

            var card = cc.MustGet(resource.CardID);
            if (!card.IsComputeType)
                throw new GameRuleException("can only distribute yield from compute resources");

            // Throughput limit
            long effectiveTP = StatCalculator.CalculateEffectiveTP(resource, field, cc);
            long remaining = effectiveTP - resource.MonetizedAmount;

            if (dist.Amount > remaining)
                throw new GameRuleException($"distribution amount {dist.Amount} exceeds remaining capacity {remaining}");

            resource.MonetizedAmount += dist.Amount;
            totalDistributed += dist.Amount;

            // Elastic scaling
            if (card.Elastic && card.ElasticIncrement > 0)
                StatCalculator.ApplyElasticBonus(resource, card);
        }

        if (totalDistributed > insightPool)
            throw new GameRuleException($"total distribution {totalDistributed} exceeds insight pool {insightPool}");

        state.SetInsightPool(playerNum, insightPool - totalDistributed);
        state.SetBudget(playerNum, budget + totalDistributed);

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        return new ActionResult
        {
            Events =
            [
                new GameEvent
                {
                    GameID = game.GameID,
                    EventType = "distribute_yield",
                    PlayerID = playerId,
                    EventData = new Dictionary<string, object>
                    {
                        ["totalAmount"] = totalDistributed,
                    }
                }
            ],
            StateUpdated = true,
        };
    }
}

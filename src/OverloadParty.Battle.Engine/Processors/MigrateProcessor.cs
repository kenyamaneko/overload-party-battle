using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class MigrateProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        MigrateRequest req, ICardCache cc)
    {
        var field = state.GetField(playerNum);

        // Find source and target
        var sourceResult = FieldHelpers.FindResourceByID(field, req.SourceInstanceID);
        if (sourceResult is null)
            throw new GameRuleException($"source {req.SourceInstanceID} not found");

        var targetResult = FieldHelpers.FindResourceByID(field, req.TargetInstanceID);
        if (targetResult is null)
            throw new GameRuleException($"target {req.TargetInstanceID} not found");

        var source = sourceResult.Value.Resource;
        var target = targetResult.Value.Resource;

        // Validations
        if (!source.FaceUp)
            throw new GameRuleException("source must be face-up");
        if (!target.FaceUp)
            throw new GameRuleException("target must be face-up");

        if (source.MigrationTarget is not null)
            throw new GameRuleException("source is already migrating");
        if (source.MigratingFrom is not null)
            throw new GameRuleException("source is already a migration destination");
        if (target.MigratingFrom is not null)
            throw new GameRuleException("target is already a migration destination");
        if (target.MigrationTarget is not null)
            throw new GameRuleException("target is already migrating");

        // Deploy turns validation: target must be >= source
        var sourceCard = cc.MustGet(source.CardID);
        var targetCard = cc.MustGet(target.CardID);
        if (targetCard.DeployTurns < sourceCard.DeployTurns)
            throw new GameRuleException("target deploy turns must be >= source deploy turns");

        // Set migration relationship
        target.MigratingFrom = source.InstanceID;
        source.MigrationTarget = target.InstanceID;
        target.MigratingOnTurn = state.CurrentTurn;

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;
        return new ActionResult
        {
            Events =
            [
                new GameEvent
                {
                    GameID = game.GameID,
                    EventType = "migrate",
                    PlayerID = playerId,
                    EventData = new Dictionary<string, object>
                    {
                        ["sourceInstanceId"] = req.SourceInstanceID,
                        ["targetInstanceId"] = req.TargetInstanceID,
                        ["sourceCardId"] = source.CardID,
                        ["targetCardId"] = target.CardID,
                    }
                }
            ],
            StateUpdated = true,
        };
    }
}

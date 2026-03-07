using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Processors;

public static class MigrateProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        MigrateRequest req, ICardCache cc)
    {
        var field = state.GetField(playerNum);

        var (source, target) = ValidateMigration(field, req, cc);

        // Set migration relationship
        target.MigratingFrom = source.InstanceID;
        source.MigrationTarget = target.InstanceID;
        target.MigratingOnTurn = state.CurrentTurn;

        var playerId = game.GetPlayerID(playerNum);
        return new ActionResult
        {
            Events =
            [
                new GameEvent
                {
                    GameID = game.GameID,
                    EventType = WireActionTypes.Migrate,
                    PlayerID = playerId,
                    EventData = new MigrateEventData
                    {
                        SourceInstanceId = req.SourceInstanceID,
                        TargetInstanceId = req.TargetInstanceID,
                        SourceCardId = source.CardID,
                        TargetCardId = target.CardID,
                    }.ToDictionary()
                }
            ],
            StateUpdated = true,
        };
    }

    private static (ResourceInstance Source, ResourceInstance Target) ValidateMigration(
        Field field, MigrateRequest req, ICardCache cc)
    {
        var source = FieldHelpers.FindResourceByID(field, req.SourceInstanceID)
            ?? throw new GameRuleException($"source {req.SourceInstanceID} not found");
        var target = FieldHelpers.FindResourceByID(field, req.TargetInstanceID)
            ?? throw new GameRuleException($"target {req.TargetInstanceID} not found");

        if (!source.FaceUp)
        {
            throw new GameRuleException("source must be face-up");
        }
        if (!target.FaceUp)
        {
            throw new GameRuleException("target must be face-up");
        }
        if (source.MigrationTarget is not null)
        {
            throw new GameRuleException("source is already migrating");
        }
        if (source.MigratingFrom is not null)
        {
            throw new GameRuleException("source is already a migration destination");
        }
        if (target.MigratingFrom is not null)
        {
            throw new GameRuleException("target is already a migration destination");
        }
        if (target.MigrationTarget is not null)
        {
            throw new GameRuleException("target is already migrating");
        }

        var sourceCard = cc.MustGet(source.CardID);
        var targetCard = cc.MustGet(target.CardID);
        if (targetCard.DeployTurns < sourceCard.DeployTurns)
        {
            throw new GameRuleException("target deploy turns must be >= source deploy turns");
        }

        return (source, target);
    }
}

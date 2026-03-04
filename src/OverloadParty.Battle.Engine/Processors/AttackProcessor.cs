using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

public static class AttackProcessor
{
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        AttackRequest req, ICardCache cc, IEffectRegistry? effects)
    {
        var myField = state.GetField(playerNum);
        var opponentNum = state.OpponentOf(playerNum);
        var oppField = state.GetField(opponentNum);

        // Find and validate attacker
        var attackerResult = FieldHelpers.FindResourceByID(myField, req.AttackerInstanceID);
        if (attackerResult is null)
            throw new GameRuleException($"attacker {req.AttackerInstanceID} not found on field");

        var (attacker, attackerZone) = attackerResult.Value;
        if (attackerZone != Zone.Frontend)
            throw new GameRuleException("attacker must be on frontend");

        var attackerCard = cc.MustGet(attacker.CardID);
        if (!attackerCard.IsComputeType)
            throw new GameRuleException("only compute resources can attack");
        if (!attacker.FaceUp)
            throw new GameRuleException("attacker must be face-up");
        if (attacker.HasAttacked)
            throw new GameRuleException("attacker has already attacked this turn");
        if (FieldHelpers.HasTemporaryEffect(attacker, "cannot_operate"))
            throw new GameRuleException("attacker cannot operate");

        // Find and validate defender
        var defenderResult = FieldHelpers.FindResourceByID(oppField, req.TargetInstanceID);
        if (defenderResult is null)
            throw new GameRuleException($"defender {req.TargetInstanceID} not found on opponent field");

        var (defender, defenderZone) = defenderResult.Value;
        if (!defender.FaceUp)
            throw new GameRuleException("cannot attack face-down resource");

        // Targeting rules: can't attack backend if opponent has frontend resources
        if (defenderZone == Zone.Backend && FieldHelpers.HasFrontendResources(oppField))
            throw new GameRuleException("cannot attack backend while opponent has frontend resources");

        // Calculate damage
        long damage = StatCalculator.CalculateEffectiveTP(attacker, myField, cc);

        var events = new List<GameEvent>();
        bool cancelled = false;

        // Fire reactive effects (opponent's support zone)
        var (reactCancelled, reactiveEvents) = FireReactives(
            state, game, opponentNum, oppField, attacker, defender, cc, effects);
        events.AddRange(reactiveEvents);
        cancelled = reactCancelled;

        var playerId = playerNum == 1 ? game.Player1ID : game.Player2ID;

        if (cancelled)
        {
            // Attack cancelled by reactive, but attacker still used their attack
            attacker.HasAttacked = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = "attack",
                PlayerID = playerId,
                EventData = new Dictionary<string, object>
                {
                    ["attackerId"] = req.AttackerInstanceID,
                    ["targetId"] = req.TargetInstanceID,
                    ["damage"] = 0,
                    ["destroyed"] = false,
                    ["cancelled"] = true,
                }
            });
            return new ActionResult { Events = events, StateUpdated = true };
        }

        // Apply damage
        defender.Damage += damage;
        attacker.HasAttacked = true;

        // Fire OnAttack trigger
        if (effects?.Has(attackerCard.CardNo, TriggerType.OnAttack) == true)
        {
            var handler = effects.Get(attackerCard.CardNo, TriggerType.OnAttack)!;
            var ctx = new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = attacker,
                Target = defender,
                CardCache = cc,
            };
            var result = handler(ctx);
            events.AddRange(result.Events);
        }

        // Check destruction
        bool destroyed = defender.EffectiveAV <= 0;
        long slaPenalty = 0;

        if (destroyed)
        {
            var defCard = cc.MustGet(defender.CardID);
            slaPenalty = defCard.SLAPenalty;

            // Deduct SLA penalty from opponent's budget
            long oppBudget = state.GetBudget(opponentNum);
            state.SetBudget(opponentNum, oppBudget - slaPenalty);

            // Fire OnDestroy triggers
            var destroyEvents = FireOnDestroy(state, game, opponentNum, defender, myField, oppField, cc, effects);
            events.AddRange(destroyEvents);

            // Clear migration links
            FieldHelpers.ClearMigrationOnSourceDestroyed(oppField, defender);

            // Move to trash (host + attachments)
            FieldHelpers.AddToTrash(state, opponentNum, defender.CardID, defender.InstanceID);
            foreach (var att in defender.Attachments)
            {
                FieldHelpers.AddToTrash(state, opponentNum, att.CardID, att.InstanceID);
            }

            // Remove from field
            FieldHelpers.RemoveResourceFromField(oppField, defender.InstanceID);
        }
        else
        {
            // Elastic scaling when attacked (frontend resources)
            var defCard = cc.MustGet(defender.CardID);
            if (defCard.Elastic)
                StatCalculator.ApplyElasticBonus(defender, defCard);
        }

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = "attack",
            PlayerID = playerId,
            EventData = new Dictionary<string, object>
            {
                ["attackerId"] = req.AttackerInstanceID,
                ["targetId"] = req.TargetInstanceID,
                ["damage"] = damage,
                ["destroyed"] = destroyed,
                ["slaPenalty"] = slaPenalty,
            }
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static (bool Cancelled, List<GameEvent> Events) FireReactives(
        GameState state, Game game, long defenderPlayerNum, Field defenderField,
        ResourceInstance attacker, ResourceInstance target,
        ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) return (false, []);

        var allEvents = new List<GameEvent>();

        var reactiveSupports = FieldHelpers.AllSupports(defenderField)
            .Where(s =>
            {
                var card = cc.Get(s.CardID);
                return card is not null && effects.Has(card.CardNo, TriggerType.Reactive);
            })
            .OrderBy(s => s.DeployOrder)
            .ToList();

        foreach (var support in reactiveSupports)
        {
            var supCard = cc.MustGet(support.CardID);
            var handler = effects.Get(supCard.CardNo, TriggerType.Reactive);
            if (handler is null) continue;

            var ctx = new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = defenderPlayerNum,
                SupSource = support,
                Source = attacker,
                Target = target,
                CardCache = cc,
            };

            var result = handler(ctx);
            allEvents.AddRange(result.Events);

            if (result.CancelAction)
            {
                if (support.FaceDown)
                    support.FaceDown = false;
                return (true, allEvents);
            }
        }

        return (false, allEvents);
    }

    private static List<GameEvent> FireOnDestroy(
        GameState state, Game game, long ownerNum,
        ResourceInstance destroyed, Field attackerField, Field ownerField,
        ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) return [];

        var allEvents = new List<GameEvent>();

        // Collect all resources with OnDestroy triggers
        var triggers = new List<(ResourceInstance Resource, long CardNo, long DeployOrder)>();

        // The destroyed card itself
        var destroyedCard = cc.MustGet(destroyed.CardID);
        if (effects.Has(destroyedCard.CardNo, TriggerType.OnDestroy))
            triggers.Add((destroyed, destroyedCard.CardNo, destroyed.DeployOrder));

        // Allied resources (excluding the destroyed one)
        foreach (var res in FieldHelpers.AllFaceUpResources(ownerField))
        {
            if (res.InstanceID == destroyed.InstanceID) continue;
            var resCard = cc.MustGet(res.CardID);
            if (effects.Has(resCard.CardNo, TriggerType.OnDestroy))
                triggers.Add((res, resCard.CardNo, res.DeployOrder));
        }

        // Sort by deploy order (earliest first)
        triggers.Sort((a, b) => a.DeployOrder.CompareTo(b.DeployOrder));

        foreach (var (resource, cardNo, _) in triggers)
        {
            var handler = effects.Get(cardNo, TriggerType.OnDestroy);
            if (handler is null) continue;

            var ctx = new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = ownerNum,
                Source = resource,
                Target = destroyed,
                CardCache = cc,
            };

            var result = handler(ctx);
            allEvents.AddRange(result.Events);
        }

        return allEvents;
    }
}

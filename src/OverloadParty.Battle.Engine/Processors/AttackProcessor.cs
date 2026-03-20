using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Processes attack actions where a frontend compute resource deals damage to an opponent's resource.
/// </summary>
public static class AttackProcessor
{
    /// <summary>
    /// Executes an attack from a player's resource against an opponent's resource.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number performing the attack.</param>
    /// <param name="req">The attack request containing attacker and target instance IDs.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">The optional effect registry for triggering reactive and on-attack effects.</param>
    /// <returns>The action result containing attack events and state update flag.</returns>
    public static ActionResult Process(
        GameState state, Game game, long playerNum,
        AttackRequest req, ICardCache cc, IEffectRegistry? effects)
    {
        var myField = state.GetField(playerNum);
        var opponentNum = state.OpponentOf(playerNum);
        var oppField = state.GetField(opponentNum);

        var (attacker, attackerCard) = ValidateAttacker(myField, req.AttackerInstanceID, cc);
        var defender = ValidateDefender(oppField, req.TargetInstanceID);

        // Calculate damage
        long damage = StatCalculator.CalculateEffectiveTP(attacker, myField, cc);

        var events = new List<GameEvent>();

        // Fire reactive effects (opponent's support zone)
        var (cancelled, reactiveEvents) = FireReactives(
            state, game, opponentNum, oppField, attacker, defender, cc, effects);
        events.AddRange(reactiveEvents);

        var playerId = game.GetPlayerID(playerNum);

        if (cancelled)
        {
            // Attack cancelled by reactive, but attacker still used their attack
            attacker.HasAttacked = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = WireActionTypes.Attack,
                PlayerID = playerId,
                EventData = new AttackEventData
                {
                    AttackerId = req.AttackerInstanceID,
                    TargetId = req.TargetInstanceID,
                    Damage = 0,
                    Destroyed = false,
                    Cancelled = true,
                }.ToDictionary(),
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

            // Fire OnDestroy triggers
            var destroyEvents = FireOnDestroy(state, game, opponentNum, defender, myField, oppField, cc, effects);
            events.AddRange(destroyEvents);

            ResourceHelpers.DestroyResource(state, opponentNum, oppField, defender, cc);
        }
        else
        {
            // Elastic scaling when attacked (frontend resources)
            var defCard = cc.MustGet(defender.CardID);
            if (defCard.Elastic)
            {
                StatCalculator.ApplyElasticBonus(defender, defCard);
            }
        }

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = WireActionTypes.Attack,
            PlayerID = playerId,
            EventData = new AttackEventData
            {
                AttackerId = req.AttackerInstanceID,
                TargetId = req.TargetInstanceID,
                Damage = damage,
                Destroyed = destroyed,
                SlaPenalty = slaPenalty,
            }.ToDictionary(),
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static (ResourceInstance Attacker, CardDefinition Card) ValidateAttacker(
        Field field, string instanceId, ICardCache cc)
    {
        var attacker = FieldHelpers.FindResourceByID(field, instanceId)
            ?? throw new GameRuleException($"attacker {instanceId} not found on field");

        if (FieldHelpers.FindResourceZone(field, instanceId) != Zone.Frontend)
        {
            throw new GameRuleException("attacker must be on frontend");
        }

        var card = cc.MustGet(attacker.CardID);
        if (!card.IsComputeType)
        {
            throw new GameRuleException("only compute resources can attack");
        }
        if (!attacker.FaceUp)
        {
            throw new GameRuleException("attacker must be face-up");
        }
        if (attacker.HasAttacked)
        {
            throw new GameRuleException("attacker has already attacked this turn");
        }
        if (FieldHelpers.HasTemporaryEffect(attacker, EffectTypes.CannotOperate))
        {
            throw new GameRuleException("attacker cannot operate");
        }

        return (attacker, card);
    }

    private static ResourceInstance ValidateDefender(Field oppField, string instanceId)
    {
        var defender = FieldHelpers.FindResourceByID(oppField, instanceId)
            ?? throw new GameRuleException($"defender {instanceId} not found on opponent field");

        if (!defender.FaceUp)
        {
            throw new GameRuleException("cannot attack face-down resource");
        }

        if (FieldHelpers.FindResourceZone(oppField, instanceId) == Zone.Backend
            && FieldHelpers.HasFrontendResources(oppField))
        {
            throw new GameRuleException("cannot attack backend while opponent has frontend resources");
        }

        return defender;
    }

    private static (bool Cancelled, List<GameEvent> Events) FireReactives(
        GameState state, Game game, long defenderPlayerNum, Field defenderField,
        ResourceInstance attacker, ResourceInstance target,
        ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) { return (false, []); }

        var allEvents = new List<GameEvent>();

        // リアクティブは1つだけ発動する（セットが最も早いもの）
        var reactive = FieldHelpers.AllSupports(defenderField)
            .Where(s => effects.Has(cc.MustGet(s.CardID).CardNo, TriggerType.Reactive))
            .MinBy(s => s.DeployOrder);

        if (reactive is null) { return (false, allEvents); }

        var supCard = cc.MustGet(reactive.CardID);
        var handler = effects.Get(supCard.CardNo, TriggerType.Reactive)!;
        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = defenderPlayerNum,
            SupSource = reactive,
            Source = attacker,
            Target = target,
            CardCache = cc,
        };

        var result = handler(ctx);
        allEvents.AddRange(result.Events);

        // リアクティブは伏せた状態でセットされるため、発動時に表向きにする
        if (!reactive.FaceUp) { reactive.FaceUp = true; }

        return (result.CancelAction, allEvents);
    }

    private static List<GameEvent> FireOnDestroy(
        GameState state, Game game, long ownerNum,
        ResourceInstance destroyed, Field attackerField, Field ownerField,
        ICardCache cc, IEffectRegistry? effects)
    {
        if (effects is null) { return []; }

        var allEvents = new List<GameEvent>();

        var triggers = new List<(ResourceInstance Resource, long CardNo, long DeployOrder)>();

        var destroyedCard = cc.MustGet(destroyed.CardID);
        if (effects.Has(destroyedCard.CardNo, TriggerType.OnDestroy))
        {
            triggers.Add((destroyed, destroyedCard.CardNo, destroyed.DeployOrder));
        }

        // Allied resources (excluding the destroyed one)
        foreach (var res in FieldHelpers.AllFaceUpResources(ownerField))
        {
            if (res.InstanceID == destroyed.InstanceID) { continue; }
            var resCard = cc.MustGet(res.CardID);
            if (effects.Has(resCard.CardNo, TriggerType.OnDestroy))
            {
                triggers.Add((res, resCard.CardNo, res.DeployOrder));
            }
        }

        // Sort by deploy order (earliest first)
        triggers.Sort((a, b) => a.DeployOrder.CompareTo(b.DeployOrder));

        foreach (var (resource, cardNo, _) in triggers)
        {
            var handler = effects.Get(cardNo, TriggerType.OnDestroy);
            if (handler is null) { continue; }

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

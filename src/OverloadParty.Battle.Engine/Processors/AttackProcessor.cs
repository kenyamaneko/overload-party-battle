using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// 攻撃アクションの検証と適用を担う。
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
        BattleGameState state, Game game, long playerNum,
        AttackRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var myField = state.GetField(playerNum);
        var opponentNum = state.OpponentOf(playerNum);
        var oppField = state.GetField(opponentNum);

        var (attacker, attackerCard) = ValidateAttacker(myField, req.AttackerInstanceID, cc);
        var defender = ValidateDefender(oppField, req.TargetInstanceID, cc);

        // ダメージを算出
        long rawDamage = StatCalculator.CalculateEffectiveTP(attacker, myField, cc);

        var events = new List<GameEvent>();

        // 攻撃宣言時のリアクティブを発動（相手のサポートゾーン、ダメージ適用前）
        var (cancelled, reactiveEvents) = FireOnAttackDeclared(
            state, game, playerNum, opponentNum, oppField, attacker, defender, rawDamage, cc, effects);
        events.AddRange(reactiveEvents);

        if (cancelled)
        {
            // リアクティブでキャンセルされたが、攻撃者の攻撃権は消費される
            attacker.HasAttacked = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = ActionTypes.Attack,
                PlayerNum = playerNum,
                EventData = new AttackEventData
                {
                    AttackerId = req.AttackerInstanceID,
                    TargetId = req.TargetInstanceID,
                    Damage = 0,
                    Destroyed = false,
                    Cancelled = true,
                },
            });
            return new ActionResult { Events = events, StateUpdated = true };
        }

        // ダメージを適用（防御者の attack_damage_reduction バフで軽減）し、on_damaged を発火
        long damage = FieldHelpers.ApplyReduction(
            defender.TemporaryEffects, BuffTypes.AttackDamageReduction, rawDamage);
        events.AddRange(ResourceHelpers.ApplyDamage(state, game, cc, effects, defender, opponentNum, damage));
        attacker.HasAttacked = true;
        attacker.LastAttackTurn = state.CurrentTurn;

        // OnAttack トリガーを発動
        if (effects.Has(attackerCard.CardId, TriggerType.OnAttack))
        {
            var handler = effects.Get(attackerCard.CardId, TriggerType.OnAttack)!;
            var ctx = new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = attacker,
                Target = defender,
                CardCache = cc,
                Effects = effects,
            };
            var result = handler(ctx);
            events.AddRange(result.Events);
        }

        // 防御者とそのアタッチメントの OnHit 効果を発動
        var onHitEvents = FireOnHit(state, game, opponentNum, defender, cc, effects);
        events.AddRange(onHitEvents);

        bool destroyed = defender.EffectiveAV <= 0;
        long slaPenalty = 0;

        if (destroyed)
        {
            var defCard = cc.MustGet(defender.CardID);
            slaPenalty = defCard.SLAPenalty;

            // OnDestroy トリガーを発動
            var destroyEvents = FireOnDestroy(state, game, opponentNum, defender, oppField, cc, effects);
            events.AddRange(destroyEvents);

            ResourceHelpers.DestroyResource(state, opponentNum, oppField, defender, cc);
            FieldChangeTrigger.Fire(state, game, cc, effects);
        }
        else
        {
            // 攻撃を受けたときの Elastic スケーリング
            var defCard = cc.MustGet(defender.CardID);
            if (defCard.Elastic)
            {
                StatCalculator.ApplyElasticBonus(defender, defCard);
            }
        }

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = ActionTypes.Attack,
            PlayerNum = playerNum,
            EventData = new AttackEventData
            {
                AttackerId = req.AttackerInstanceID,
                TargetId = req.TargetInstanceID,
                Damage = damage,
                Destroyed = destroyed,
                SlaPenalty = slaPenalty,
            },
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static (DeployedResource Attacker, CardDefinition Card) ValidateAttacker(
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
        if (FieldHelpers.HasTemporaryEffect(attacker, BuffTypes.Dormant))
        {
            throw new GameRuleException("dormant resource cannot attack");
        }

        return (attacker, card);
    }

    private static DeployedResource ValidateDefender(Field oppField, string instanceId, ICardCache cc)
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

        if (FieldHelpers.IsTargetShielded(defender, oppField, cc))
        {
            throw new GameRuleException("Target is protected by target_shield");
        }

        return defender;
    }

    /// <summary>
    /// ダメージ適用前に防御側サポートゾーンの on_attack_declared を発火します
    /// </summary>
    private static (bool Cancelled, List<GameEvent> Events) FireOnAttackDeclared(
        BattleGameState state, Game game, long attackerNum, long defenderNum, Field defenderField,
        DeployedResource attacker, DeployedResource target, long damage,
        ICardCache cc, IEffectRegistry effects)
    {

        var candidates = FieldHelpers.AllSupports(defenderField)
            .Select(s => EventTriggerCandidate.ForSupport(s, defenderNum))
            .ToList();

        return EventTriggerFiring.Fire(
            state, effects, cc, TriggerType.OnAttackDeclared, candidates,
            candidate => new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = defenderNum,
                SupSource = candidate.Support,
                Source = attacker,
                Target = target,
                EventOwnerNum = attackerNum,
                EventDamage = damage,
                CardCache = cc,
                Effects = effects,
            });
    }

    /// <summary>
    /// 所有者のフィールドリソースとサポートゾーンの on_destroy を発火します
    /// </summary>
    private static List<GameEvent> FireOnDestroy(
        BattleGameState state, Game game, long ownerNum,
        DeployedResource destroyed, Field ownerField,
        ICardCache cc, IEffectRegistry effects)
    {

        var candidates = new List<EventTriggerCandidate>();

        if (effects.Has(destroyed.CardID, TriggerType.OnDestroy))
        {
            candidates.Add(EventTriggerCandidate.ForResource(destroyed, ownerNum));
        }

        foreach (var res in FieldHelpers.AllFaceUpResources(ownerField))
        {
            if (res.InstanceID == destroyed.InstanceID) { continue; }
            candidates.Add(EventTriggerCandidate.ForResource(res, ownerNum));
        }

        // サポートゾーンの伏せ Reactive も on_destroy の走査対象。
        foreach (var sup in FieldHelpers.AllSupports(ownerField))
        {
            candidates.Add(EventTriggerCandidate.ForSupport(sup, ownerNum));
        }

        var (_, events) = EventTriggerFiring.Fire(
            state, effects, cc, TriggerType.OnDestroy, candidates,
            candidate => new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = ownerNum,
                Source = candidate.Resource,
                SupSource = candidate.Support,
                Target = destroyed,
                EventOwnerNum = ownerNum,
                CardCache = cc,
                Effects = effects,
            });

        return events;
    }

    private static List<GameEvent> FireOnHit(
        BattleGameState state, Game game, long defenderPlayerNum,
        DeployedResource defender, ICardCache cc, IEffectRegistry effects)
    {

        var allEvents = new List<GameEvent>();

        // Fire OnHit for the defender card itself
        if (effects.Has(defender.CardID, TriggerType.OnHit))
        {
            var handler = effects.Get(defender.CardID, TriggerType.OnHit)!;
            var result = handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = defenderPlayerNum,
                Source = defender,
                Target = defender,
                CardCache = cc,
                Effects = effects,
            });
            if (!result.GuardFailed) { allEvents.AddRange(result.Events); }
        }

        // Fire OnHit for the defender's attachments
        var oppField = state.GetField(defenderPlayerNum);
        foreach (var att in oppField.Support.Where(a => a.TargetInstanceID == defender.InstanceID))
        {
            if (effects.Has(att.CardID, TriggerType.OnHit))
            {
                var handler = effects.Get(att.CardID, TriggerType.OnHit)!;
                var result = handler(new EffectContext
                {
                    State = state,
                    Game = game,
                    PlayerNum = defenderPlayerNum,
                    Source = defender,
                    Target = defender,
                    CardCache = cc,
                    Effects = effects,
                });
                if (!result.GuardFailed) { allEvents.AddRange(result.Events); }
            }
        }

        return allEvents;
    }
}

using System.Linq;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// UseEffectProcessor はリソースおよびサポートカードの起動効果アクションを処理します
/// </summary>
public static class UseEffectProcessor
{
    /// <summary>
    /// プレイヤーのフィールド上のリソース / サポートカードの起動効果を発動します。
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">起動効果を発動するプレイヤー番号。</param>
    /// <param name="req">発動元のインスタンスと任意の対象を含むリクエスト。</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">The optional effect registry containing effect handlers.</param>
    /// <returns>The action result containing effect events and state update flag.</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum,
        UseEffectRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var field = state.GetField(playerNum);

        // まずリソースとして検索
        var resource = FieldHelpers.FindResourceByID(field, req.InstanceID);
        if (resource is not null)
        {
            return IgniteResource(state, game, playerNum, resource, req, cc, effects);
        }

        // サポートゾーンを検索
        var support = FieldHelpers.FindSupportByID(field, req.InstanceID);
        if (support is not null)
        {
            return IgniteSupport(state, game, playerNum, field, support, req, cc, effects);
        }

        throw new GameRuleException($"resource {req.InstanceID} not found on field");
    }

    private static ActionResult IgniteResource(
        BattleGameState state, Game game, long playerNum,
        DeployedResource source,
        UseEffectRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var card = ValidateResourceActivation(source, cc, effects);

        DeployedResource? target = null;
        if (req.TargetInstanceID is { } targetId)
        {
            // 自分のフィールドを先に検索、次に相手のフィールド
            target = FieldHelpers.FindResourceByID(state.GetField(playerNum), targetId)
                  ?? FieldHelpers.FindResourceByID(state.GetField(state.OpponentOf(playerNum)), targetId);
        }

        var handler = effects.Get(card.CardId, TriggerType.Ignition)!;
        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = playerNum,
            Source = source,
            Target = target,
            CardCache = cc,
            ChoiceData = req.ChoiceData,
            Effects = effects,
        };

        var result = handler(ctx);
        source.EffectUsedThisTurn = true;

        var events = new List<GameEvent>(result.Events);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = ActionTypes.UseEffect,
            PlayerNum = playerNum,
            EventData = new UseEffectEventData
            {
                CardId = card.CardId,
                SourceId = req.InstanceID,
                TargetId = req.TargetInstanceID,
            },
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static ActionResult IgniteSupport(
        BattleGameState state, Game game, long playerNum,
        Field field, DeployedSupport support,
        UseEffectRequest req, ICardCache cc, IEffectRegistry effects)
    {
        var card = cc.MustGet(support.CardID);

        if (!effects.Has(card.CardId, TriggerType.Ignition))
        {
            throw new GameRuleException($"support card {card.CardId} has no ignition effect");
        }

        var handler = effects.Get(card.CardId, TriggerType.Ignition)!;
        var ctx = new EffectContext
        {
            State = state,
            Game = game,
            PlayerNum = playerNum,
            SupSource = support,
            CardCache = cc,
            ChoiceData = req.ChoiceData,
            Effects = effects,
        };

        var result = handler(ctx);
        support.EffectUsedThisTurn = true;

        var events = new List<GameEvent>(result.Events);
        events.Insert(0, new GameEvent
        {
            GameID = game.GameID,
            EventType = ActionTypes.UseEffect,
            PlayerNum = playerNum,
            EventData = new UseEffectEventData
            {
                CardId = card.CardId,
                SourceId = req.InstanceID,
            },
        });

        return new ActionResult { Events = events, StateUpdated = true };
    }

    private static CardDefinition ValidateResourceActivation(
        DeployedResource source, ICardCache cc, IEffectRegistry effects)
    {
        var card = cc.MustGet(source.CardID);

        if (!effects.Has(card.CardId, TriggerType.Ignition))
        {
            throw new GameRuleException($"card {card.CardId} has no ignition effect");
        }
        if (source.EffectUsedThisTurn)
        {
            throw new GameRuleException("effect already used this turn");
        }

        return card;
    }
}

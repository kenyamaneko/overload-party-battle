using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// フィールド状態からパッシブ効果由来のバフを洗い替える。
/// </summary>
public static class PassiveRecalculator
{
    /// <summary>
    /// 両プレイヤーのフィールドから continuous エントリを全て消し、パッシブ効果の発動条件を
    /// 再評価して導出し直す。冪等でありイベントは発しない。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    public static void Recalculate(BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects)
    {
        WipeContinuousEffects(state.GetField(1));
        WipeContinuousEffects(state.GetField(2));

        var owners = new List<PassiveOwner>();
        owners.AddRange(EnumerateOwners(state, 1, effects));
        owners.AddRange(EnumerateOwners(state, 2, effects));

        // count_multiplier は枚数評価に乗るため、per-count な application より先に導出する。
        ApplyPass(owners, state, game, cc, effects, countMultiplierPass: true);
        ApplyPass(owners, state, game, cc, effects, countMultiplierPass: false);
    }

    private static void WipeContinuousEffects(Field field)
    {
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.TemporaryEffects.RemoveAll(e => e.Duration == EffectDurations.Continuous);
        }
    }

    /// <summary>パッシブ効果を持ちうるフィールド上のカード 1 枚と、発動条件評価に使う情報。</summary>
    private sealed class PassiveOwner
    {
        public required string CardId { get; init; }
        public required string OwnInstanceId { get; init; }
        public required long OwnerPlayerNum { get; init; }
        public DeployedResource? Source { get; init; }
        public DeployedSupport? SupSource { get; init; }
    }

    private static List<PassiveOwner> EnumerateOwners(BattleGameState state, long playerNum, IEffectRegistry effects)
    {
        var field = state.GetField(playerNum);
        var owners = new List<(long DeployOrder, PassiveOwner Owner)>();

        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (effects.GetPassives(resource.CardID).Count > 0)
            {
                owners.Add((resource.DeployOrder, new PassiveOwner
                {
                    CardId = resource.CardID,
                    OwnInstanceId = resource.InstanceID,
                    OwnerPlayerNum = playerNum,
                    Source = resource,
                }));
            }

            foreach (var attachment in field.Support.Where(a => a.TargetInstanceID == resource.InstanceID))
            {
                if (effects.GetPassives(attachment.CardID).Count > 0)
                {
                    owners.Add((resource.DeployOrder, new PassiveOwner
                    {
                        CardId = attachment.CardID,
                        OwnInstanceId = attachment.InstanceID,
                        OwnerPlayerNum = playerNum,
                        Source = resource,
                        SupSource = attachment,
                    }));
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (!support.FaceUp || support.DeployingTurnsLeft > 0) { continue; }
            if (effects.GetPassives(support.CardID).Count > 0)
            {
                owners.Add((support.DeployOrder, new PassiveOwner
                {
                    CardId = support.CardID,
                    OwnInstanceId = support.InstanceID,
                    OwnerPlayerNum = playerNum,
                    SupSource = support,
                }));
            }
        }

        return owners.OrderBy(o => o.DeployOrder).Select(o => o.Owner).ToList();
    }

    private static void ApplyPass(
        List<PassiveOwner> owners, BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects,
        bool countMultiplierPass)
    {
        foreach (var owner in owners)
        {
            foreach (var def in effects.GetPassives(owner.CardId))
            {
                var ctx = new EffectContext
                {
                    State = state,
                    Game = game,
                    PlayerNum = owner.OwnerPlayerNum,
                    Source = owner.Source,
                    SupSource = owner.SupSource,
                    CardCache = cc,
                    Effects = effects,
                };

                if (!def.Guards.All(guard => guard.Check(ctx))) { continue; }

                var opCtx = new OpContext(ctx);
                foreach (var application in def.Applications)
                {
                    bool isCountMultiplier = application.EffectType == BuffTypes.CountMultiplier;
                    if (isCountMultiplier != countMultiplierPass) { continue; }

                    Apply(opCtx, application, owner.OwnInstanceId);
                }
            }
        }
    }

    private static void Apply(OpContext ctx, PassiveBuffApplication application, string ownInstanceId)
    {
        long amount = application.Amount.Resolve(ctx);
        var targets = application.Selector.Select(ctx);

        foreach (var target in targets)
        {
            target.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = application.EffectType,
                Value = amount,
                Duration = EffectDurations.Continuous,
                SourceID = ownInstanceId,
                Mode = application.Mode,
            });
        }
    }
}

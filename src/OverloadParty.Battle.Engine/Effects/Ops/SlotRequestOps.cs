using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// 条件に合うデッキのカードを、配置先の空きがあるときにかぎり <see cref="AwaitingSlotSelect"/> として積みます。
/// </summary>
public class RequestSlotFromRepoOp : IEffectOp
{
    /// <summary>Optional filter to restrict which cards can be deployed.</summary>
    public Func<CardDefinition, bool>? Filter { get; init; }

    /// <summary>Override AV for the deployed resource, or 0 to use default.</summary>
    public long OverrideAV { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        var repo = ctx.State.GetRepository(ctx.PlayerNum);
        var reserved = SlotSelectQueue.ReservedCardInstanceIDs(ctx.State, ctx.PlayerNum);

        var match = repo.FirstOrDefault(candidate =>
        {
            if (reserved.Contains(candidate.InstanceID)) { return false; }
            var definition = ctx.CardCache.Get(candidate.CardID);
            return definition is not null && (Filter is null || Filter(definition));
        });

        if (match is null) { return; }

        // 配置先がなければ発動条件の不成立として不発にする。
        var card = ctx.CardCache.MustGet(match.CardID);
        var field = ctx.GetField(ctx.PlayerNum);
        if (ResourceHelpers.BuildValidZones(field, card).Count == 0)
        {
            ctx.Result.HasGuardFailed = true;
            return;
        }

        // カードはスロットが決まるまでデッキに残す。取り出さなければ不発時に戻す必要がなく、
        // デッキの並びもインスタンス ID も動かない。
        ctx.State.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = ctx.PlayerNum,
            SourceZone = SlotSelectSources.Repository,
            CardInstanceID = match.InstanceID,
            OverrideAV = OverrideAV,
        });
    }
}

/// <summary>
/// プレイヤーが選んだ手札のカードを、配置先の空きがあるときにかぎり <see cref="AwaitingSlotSelect"/> として積みます。
/// </summary>
public class RequestSlotFromHandOp : IEffectOp
{
    /// <summary>Optional filter to restrict which cards can be deployed.</summary>
    public Func<CardDefinition, bool>? Filter { get; init; }

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        string? choiceCardId = ctx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();

        if (choiceCardId is null)
        {
            // reactive 経路では選択待ち。直接呼び出し経路では仕様外なので throw。
            if (ctx.SupSource is null)
            {
                throw new GameRuleException("No card chosen for deploy from hand");
            }
            var candidates = EnumerateDeployableHandCards(ctx);
            if (candidates.Count == 0)
            {
                // passive trigger 経路で発火条件不成立。HasGuardFailed として正常な不発に扱う。
                ctx.Result.HasGuardFailed = true;
                return;
            }
            ctx.SuspendForChoice("cardId", ChoiceKinds.HandCard, candidates, ctx.PlayerNum);
            return;
        }

        if (Filter is not null)
        {
            var card = ctx.CardCache.MustGet(choiceCardId);
            if (!Filter(card))
            {
                throw new GameRuleException($"Card {choiceCardId} does not match filter");
            }
        }

        SlotRequestHelpers.DeployFromHand(ctx, choiceCardId);
    }

    /// <summary>
    /// 手札から Filter を満たすカードの CardID を重複排除して返します。
    /// </summary>
    private List<string> EnumerateDeployableHandCards(OpContext ctx)
    {
        // SlotRequestHelpers.DeployFromHand は CardID で手札先頭を引くため、
        // 同名カードの 2 枚目以降は候補として区別しなくてよい。
        var reserved = SlotSelectQueue.ReservedCardInstanceIDs(ctx.State, ctx.PlayerNum);

        return ctx.State.GetHand(ctx.PlayerNum)
            .Where(c =>
            {
                if (reserved.Contains(c.InstanceID)) { return false; }
                var card = ctx.CardCache.Get(c.CardID);
                return card is not null && (Filter is null || Filter(card));
            })
            .Select(c => c.CardID)
            .Distinct()
            .ToList();
    }
}

/// <summary>
/// Deploys the same card as the current target from the repository
/// by delegating to <see cref="RequestSlotFromRepoOp"/>.
/// </summary>
public class RequestSlotFromRepoSameCardOp(long overrideAV = 0) : IEffectOp
{
    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (ctx.Target is null)
        {
            throw new GameRuleException("No target for same card deploy");
        }

        var inner = new RequestSlotFromRepoOp
        {
            Filter = card => card.CardId == ctx.Target.CardID,
            OverrideAV = overrideAV,
        };
        inner.Execute(ctx);
    }
}

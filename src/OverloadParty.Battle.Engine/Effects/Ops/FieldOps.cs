using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public class DestroyCheckOp(PlayerRef player) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        long playerNum = player == PlayerRef.Self ? ctx.PlayerNum : ctx.OpponentNum;
        var field = ctx.GetField(playerNum);

        // Check all resources and destroy those with AV <= 0
        var toDestroy = new List<ResourceInstance>();
        foreach (var res in FieldHelpers.AllResources(field))
        {
            if (res.EffectiveAV <= 0)
                toDestroy.Add(res);
        }

        foreach (var res in toDestroy)
        {
            var card = ctx.CardCache.MustGet(res.CardID);

            // Apply SLA penalty
            long oppNum = ctx.State.OpponentOf(playerNum);
            long oppBudget = ctx.State.GetBudget(oppNum);
            // Note: SLA penalty is applied to the owner of the destroyed resource
            long ownerBudget = ctx.State.GetBudget(playerNum);
            ctx.State.SetBudget(playerNum, ownerBudget - card.SLAPenalty);

            // Clear migration links
            FieldHelpers.ClearMigrationOnSourceDestroyed(field, res);

            // Move to trash (host + attachments)
            FieldHelpers.AddToTrash(ctx.State, playerNum, res.CardID, res.InstanceID);
            foreach (var att in res.Attachments)
            {
                FieldHelpers.AddToTrash(ctx.State, playerNum, att.CardID, att.InstanceID);
            }

            FieldHelpers.RemoveResourceFromField(field, res.InstanceID);
        }
    }
}

public class ScaleToRankOp(string rank) : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        if (ctx.Source is null) return;

        var targetRank = EnumExtensions.ParseRank(rank);
        ctx.Source.Rank = targetRank;

        // Recalculate stats
        ctx.Source.MaxAV = StatCalculator.CalculateMaxAV(ctx.Source, ctx.CardCache);

        var card = ctx.CardCache.MustGet(ctx.Source.CardID);
        if (!card.Elastic)
        {
            if (card.IsComputeType)
            {
                long newTP = StatCalculator.RecalculateMaxTP(ctx.Source, card);
                ctx.Source.MaxTP = newTP;
                ctx.Source.CurrentTP = newTP;
            }
            if (card.IsDataType)
            {
                long newYield = StatCalculator.RecalculateMaxYield(ctx.Source, card);
                ctx.Source.MaxYield = newYield;
                ctx.Source.CurrentYield = newYield;
            }
        }
    }
}

public class RevealTrapOp : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        var oppField = ctx.OpponentField;
        var faceDown = oppField.Support.FirstOrDefault(s => s.FaceDown);
        if (faceDown is not null)
            faceDown.FaceDown = false;
    }
}

public class DestroyPlatformOp : IEffectOp
{
    public void Execute(OpContext ctx)
    {
        var oppField = ctx.OpponentField;

        // Get choice from ChoiceData if available
        var instanceId = ctx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();

        var target = oppField.Support.FirstOrDefault(s =>
        {
            var card = ctx.CardCache.Get(s.CardID);
            if (card?.CardType != "Platform") return false;
            return instanceId is null || s.InstanceID == instanceId;
        });

        if (target is not null)
        {
            FieldHelpers.AddToTrash(ctx.State, ctx.OpponentNum, target.CardID, target.InstanceID);
            oppField.Support.Remove(s => s.InstanceID == target.InstanceID);
        }
    }
}

using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects.Ops;

public class DealDamageOp(ISelector sel, IAmountResolver value) : IEffectOp
{
    public ISelector Selector => sel;

    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            target.Damage += amount;
        }
    }
}

public class IncidentDamageOp(ISelector sel, IAmountResolver value, IAmountResolver? budgetPenalty = null) : IEffectOp
{
    public ISelector Selector => sel;

    private const long IncidentReductionAmount = 200;
    // Cards that provide incident damage reduction
    private static readonly HashSet<long> IncidentReductionAttachments = [39, 16, 87];
    private const long ISMSCertCardNo = 96;
    private const long AutoPatchCardNo = 24;

    public void Execute(OpContext ctx)
    {
        long baseDamage = value.Resolve(ctx);
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            long reduction = CalculateReduction(target, ctx);
            long actualDamage = Math.Max(0, baseDamage - reduction);
            target.Damage += actualDamage;
        }

        // Budget penalty if specified
        if (budgetPenalty is not null)
        {
            long penalty = budgetPenalty.Resolve(ctx);
            long oppBudget = ctx.State.GetBudget(ctx.OpponentNum);
            ctx.State.SetBudget(ctx.OpponentNum, oppBudget - penalty);
        }
    }

    private long CalculateReduction(ResourceInstance target, OpContext ctx)
    {
        long reduction = 0;

        // Check attachments on target
        foreach (var att in target.Attachments)
        {
            if (IncidentReductionAttachments.Contains(att.CardID))
                reduction += IncidentReductionAmount;
        }

        // Check for ISMS Certification in support zone
        var ownerNum = FindOwner(target, ctx);
        var field = ctx.GetField(ownerNum);
        foreach (var (support, _) in FieldHelpers.AllSupports(field))
        {
            if (support.CardID == ISMSCertCardNo && !support.FaceDown)
                reduction += IncidentReductionAmount;
        }

        // Check if target itself is Auto Patch
        if (target.CardID == AutoPatchCardNo)
            reduction += IncidentReductionAmount;

        return reduction;
    }

    private long FindOwner(ResourceInstance target, OpContext ctx)
    {
        // Check own field first
        if (FieldHelpers.FindResourceByID(ctx.MyField, target.InstanceID) is not null)
            return ctx.PlayerNum;
        return ctx.OpponentNum;
    }
}

public class HealDamageOp(ISelector sel, IAmountResolver value) : IEffectOp
{
    public ISelector Selector => sel;

    public void Execute(OpContext ctx)
    {
        long amount = value.Resolve(ctx);
        var targets = sel.Select(ctx);

        foreach (var target in targets)
        {
            if (amount == 0)
            {
                // Full heal
                target.Damage = 0;
            }
            else
            {
                target.Damage = Math.Max(0, target.Damage - amount);
            }
        }
    }
}

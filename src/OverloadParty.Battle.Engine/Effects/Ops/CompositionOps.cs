namespace OverloadParty.Battle.Engine.Effects.Ops;

/// <summary>
/// 名前付きのブロックを実行し、guard 述語が成立した場合だけ ops を流す。
/// 成否を <see cref="OpContext.GroupResults"/> に記録し、後続の独立ブロックが続けて動けるようにする。
/// </summary>
public class EffectGroupOp(string groupId, BuiltBlock block) : IEffectOp
{
    /// <summary>このグループの識別子。</summary>
    public string GroupId => groupId;

    /// <summary>このグループの guards / ops を保持する block。</summary>
    public BuiltBlock Block => block;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        foreach (var guard in block.Guards)
        {
            if (!guard.Check(ctx.Ctx))
            {
                ctx.GroupResults[groupId] = false;
                return;
            }
        }
        foreach (var op in block.Ops)
        {
            op.Execute(ctx);
            if (ctx.Result.PendingChoice is not null) { return; }
        }
        ctx.GroupResults[groupId] = true;
    }
}

/// <summary>
/// 親グループが成功している場合に限り、従属ブロックの guards / ops を実行する。
/// 従属ブロックの guard 不成立は静かにスキップする (例外を経由しない)。
/// </summary>
public class DependentEffectOp(string parentGroupId, BuiltBlock block) : IEffectOp
{
    /// <summary>親グループの識別子。</summary>
    public string ParentGroupId => parentGroupId;

    /// <summary>従属ブロックの guards / ops。</summary>
    public BuiltBlock Block => block;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        if (!ctx.GroupResults.GetValueOrDefault(parentGroupId)) { return; }

        foreach (var guard in block.Guards)
        {
            if (!guard.Check(ctx.Ctx)) { return; }
        }
        foreach (var op in block.Ops)
        {
            op.Execute(ctx);
            if (ctx.Result.PendingChoice is not null) { return; }
        }
    }
}

/// <summary>
/// 指定グループの成功を <see cref="OpContext.GroupResults"/> に記録する。
/// root ブロック (例外隔離なし) の成功を後続の DependentEffectOp に伝えるために使う。
/// </summary>
public class MarkGroupSucceededOp(string groupId) : IEffectOp
{
    /// <summary>記録対象のグループ識別子。</summary>
    public string GroupId => groupId;

    /// <inheritdoc />
    public void Execute(OpContext ctx)
    {
        ctx.GroupResults[groupId] = true;
    }
}

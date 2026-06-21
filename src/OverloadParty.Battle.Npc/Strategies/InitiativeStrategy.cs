using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// プロダクトの施策 (ルーチン / スペシャル) の使用判断。
/// </summary>
internal sealed class InitiativeStrategy
{
    private readonly IInitiativeCatalog _initiatives;
    private readonly IEffectRegistry _effects;
    private readonly ICardCache _cc;

    /// <summary>施策カタログと効果レジストリを束ねた施策戦略を構築します。</summary>
    /// <param name="initiatives">施策 ID から施策定義を解決するカタログ。</param>
    /// <param name="effects">効果のターゲット情報を引く効果レジストリ。</param>
    /// <param name="cc">カード定義の参照元。</param>
    public InitiativeStrategy(IInitiativeCatalog initiatives, IEffectRegistry effects, ICardCache cc)
    {
        _initiatives = initiatives;
        _effects = effects;
        _cc = cc;
    }

    /// <summary>
    /// 設定で有効化され使用条件を満たす施策のアクション列を優先度順に決定します。
    /// </summary>
    /// <param name="ctx">現在の判断コンテキスト。</param>
    /// <param name="available">エンジンが提示した実行可能アクション。</param>
    /// <param name="activeConfig">フェーズ overlay 適用後の AI 設定。</param>
    /// <returns>使用する施策アクション列。</returns>
    public List<NpcAction> Decide(
        DecisionContext ctx, List<GD.AvailableAction> available, AiConfig activeConfig)
    {
        var config = activeConfig.Initiative;
        if (config is null)
        {
            return [];
        }

        return ActionFilter.FilterByType(available, ActionTypes.UseInitiative)
            .Select(a => (Action: a, Kind: ResolveKindConfig(config, a.Kind)))
            .Where(x => x.Kind is not null)
            .Select(x => BuildCandidate(x.Action, x.Kind!, ctx, activeConfig))
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .OrderByDescending(c => c.Priority)
            .Select(c => new NpcAction
            {
                ActionType = ActionTypes.UseInitiative,
                Data = new UseInitiativeRequest
                {
                    Kind = c.Action.Kind,
                    ChoiceData = c.ChoiceData,
                },
            })
            .ToList();
    }

    /// <summary>
    /// 使用条件と効果のターゲット可否を評価し、使用する場合のみ候補を返します。
    /// 選択対象を要する効果で候補が無い場合は使用しないため null を返します。
    /// </summary>
    /// <param name="action">対象の施策アクション。</param>
    /// <param name="kindConfig">区分の使用設定。</param>
    /// <param name="ctx">現在の判断コンテキスト。</param>
    /// <param name="activeConfig">フェーズ overlay 適用後の AI 設定。</param>
    /// <returns>使用する施策の候補。使用しない場合は null。</returns>
    private (GD.AvailableAction Action, int Priority, Dictionary<string, object>? ChoiceData)? BuildCandidate(
        GD.AvailableAction action, InitiativeKindConfig kindConfig, DecisionContext ctx, AiConfig activeConfig)
    {
        if (kindConfig.MinInsight is { } minInsight && ctx.InsightPool < minInsight)
        {
            return null;
        }

        if (!GuardChecker.Check(kindConfig.Condition, ctx, _cc))
        {
            return null;
        }

        var initiative = _initiatives.GetById(action.CardID)
            ?? throw new InvalidOperationException($"initiative '{action.CardID}' not found in catalog");
        var info = _effects.GetEffectInfo(initiative.EffectSourceId, TriggerType.Ignition);

        Dictionary<string, object>? choiceData = null;
        if (info is not null && info.TargetType == EffectTargetType.Choice)
        {
            var target = PriorityResolver.SelectTarget(info, ctx, activeConfig.TargetSelection, _cc);
            if (target is null)
            {
                return null;
            }
            choiceData = new Dictionary<string, object> { ["instanceId"] = target };
        }

        return (action, kindConfig.Priority, choiceData);
    }

    /// <summary>区分文字列に対応する使用設定を返します。設定が無い区分は null を返します。</summary>
    /// <param name="config">施策の使用判断設定。</param>
    /// <param name="kind">施策の区分 (ルーチン / スペシャル)。</param>
    /// <returns>区分の使用設定。未設定なら null。</returns>
    private static InitiativeKindConfig? ResolveKindConfig(InitiativeConfig config, string kind) => kind switch
    {
        InitiativeKinds.Routine => config.Routine,
        InitiativeKinds.Special => config.Special,
        _ => throw new InvalidOperationException($"unknown initiative kind '{kind}'"),
    };
}

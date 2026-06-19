using OverloadParty.Battle.Models;
using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// 施策効果を EffectRegistry に登録するヘルパー。施策効果は常に起動効果として発動する。
/// </summary>
public static class InitiativeEffects
{
    /// <summary>
    /// 各施策効果を起動効果トリガーに固定し EffectRegistry に登録します。
    /// </summary>
    /// <param name="initiatives">登録対象の施策群。</param>
    /// <param name="registry">登録先の効果レジストリ。</param>
    /// <param name="customRegistry">カスタム効果のレジストリ。</param>
    public static void LoadIntoRegistry(
        IEnumerable<Initiative> initiatives,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        var sources = initiatives.ToList();
        foreach (var initiative in sources)
        {
            initiative.Effect.Trigger = TriggerTypes.Ignition;
        }

        EffectYamlLoader.LoadEffectSources(sources, registry, customRegistry);
    }
}

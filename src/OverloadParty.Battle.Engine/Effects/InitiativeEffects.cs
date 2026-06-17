using OverloadParty.Battle.Models;
using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// 施策効果をカード効果と同じ EffectRegistry に登録するヘルパー。
/// </summary>
public static class InitiativeEffects
{
    /// <summary>施策 handler を引くための合成カード ID を返します。</summary>
    /// <param name="initiativeId">施策 ID。</param>
    /// <returns>EffectRegistry の検索キーとなる合成カード ID。</returns>
    public static string HandlerCardId(string initiativeId) => $"initiative:{initiativeId}";

    /// <summary>
    /// プロダクト群の各施策効果を起動効果として EffectRegistry に登録します。
    /// </summary>
    /// <param name="initiatives">登録対象の施策群。</param>
    /// <param name="registry">登録先の効果レジストリ。</param>
    /// <param name="customRegistry">カスタム効果のレジストリ。</param>
    public static void LoadIntoRegistry(
        IEnumerable<Initiative> initiatives,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        var syntheticCards = new List<CardDefinition>();
        foreach (var initiative in initiatives)
        {
            initiative.Effect.Trigger = TriggerTypes.Ignition;
            syntheticCards.Add(new CardDefinition
            {
                CardId = HandlerCardId(initiative.InitiativeId),
                Effects = [initiative.Effect],
            });
        }

        EffectYamlLoader.LoadFromCards(syntheticCards, registry, customRegistry);
    }
}

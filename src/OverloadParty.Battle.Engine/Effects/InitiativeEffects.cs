using OverloadParty.Battle.Models;
using OverloadParty.GameLogicConstants;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// プロダクトの施策効果を、カード効果と同じ EffectRegistry に登録するヘルパー。
/// 施策ごとに合成カード定義 (CardId = HandlerCardId、trigger = ignition) を作り、
/// 既存の YAML ローダで handler を構築する。
/// </summary>
public static class InitiativeEffects
{
    /// <summary>施策 handler を引くための合成カード ID を返します。</summary>
    /// <param name="faction">プロダクトの陣営。</param>
    /// <param name="kind">施策の区分 (ルーチン / スペシャル)。</param>
    /// <returns>EffectRegistry の検索キーとなる合成カード ID。</returns>
    public static string HandlerCardId(string faction, string kind) => $"initiative:{faction}:{kind}";

    /// <summary>
    /// プロダクト群の各施策効果を起動効果として EffectRegistry に登録します。
    /// </summary>
    /// <param name="products">登録対象のプロダクト群。</param>
    /// <param name="registry">登録先の効果レジストリ。</param>
    /// <param name="customRegistry">カスタム効果のレジストリ。</param>
    public static void LoadIntoRegistry(
        IEnumerable<Product> products,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        var syntheticCards = new List<CardDefinition>();
        foreach (var product in products)
        {
            foreach (var initiative in product.Initiatives)
            {
                initiative.Effect.Trigger = TriggerTypes.Ignition;
                syntheticCards.Add(new CardDefinition
                {
                    CardId = HandlerCardId(product.Faction, initiative.Kind),
                    Effects = [initiative.Effect],
                });
            }
        }

        EffectYamlLoader.LoadFromCards(syntheticCards, registry, customRegistry);
    }
}

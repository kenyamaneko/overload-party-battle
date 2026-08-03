using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

[Trait("対象", "マスターデータの効果定義の読み込み")]
public class MasterDataEffectLoadTests
{
    [Fact(DisplayName = "card が配布するカードマスターデータを読み込むと、全カードの効果定義が実装済みの効果として登録される")]
    public void LoadsEveryCardEffectDefinition()
    {
        var failures = CollectLoadFailures(
            MasterData.LoadCards(),
            (card, registry, customEffects) =>
                EffectYamlLoader.LoadEffectSources([card], registry, customEffects));

        failures.Should().BeEmpty();
    }

    [Fact(DisplayName = "card が配布する施策マスターデータを読み込むと、全施策の効果定義が実装済みの効果として登録される")]
    public void LoadsEveryInitiativeEffectDefinition()
    {
        var failures = CollectLoadFailures(
            MasterData.LoadInitiatives(),
            (initiative, registry, customEffects) =>
                InitiativeEffects.LoadIntoRegistry([initiative], registry, customEffects));

        failures.Should().BeEmpty();
    }

    private static List<string> CollectLoadFailures<TSource>(
        IReadOnlyList<TSource> sources,
        Action<TSource, EffectRegistry, CustomEffectRegistry> load)
        where TSource : IEffectSource
    {
        var failures = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                load(source, new EffectRegistry(), new CustomEffectRegistry());
            }
            catch (InvalidOperationException ex)
            {
                failures.Add($"{source.EffectSourceId}: {ex.Message}");
            }
        }
        return failures;
    }
}

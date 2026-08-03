using System.Text.Json;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

[Trait("対象", "カスタム効果の読み込み失敗")]
public class CustomEffectLoadFailureTests
{
    [Fact(DisplayName = "実装されていないカスタム効果名を持つカードを読み込むと、その効果名を示すエラーになる")]
    public void ReportsUnregisteredCustomName()
    {
        var act = () => LoadCustomEffect("unimplemented_custom", metaJson: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown custom effect*unimplemented_custom*");
    }

    [Fact(DisplayName = "peek を持たない meta で keep_one_from_deck_top のカードを読み込むと、欠けているキー peek を示すエラーになる")]
    public void ReportsMissingRequiredMetaKey()
    {
        var act = () => LoadCustomEffect("keep_one_from_deck_top", metaJson: """{ "categories": ["draw"] }""");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*keep_one_from_deck_top*meta key: peek*");
    }

    private static void LoadCustomEffect(string customName, string? metaJson)
    {
        var card = new CardDefinition
        {
            CardId = "TST-0001",
            CardName = "T",
            CardType = CardTypes.Compute,
            Faction = "Tuners",
            DeployTurns = 0,
            Effects =
            [
                new EffectDef
                {
                    Trigger = "ignition",
                    Custom = customName,
                    Meta = metaJson is null
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metaJson),
                },
            ],
        };

        EffectYamlLoader.LoadEffectSources([card], new EffectRegistry(), new CustomEffectRegistry());
    }
}

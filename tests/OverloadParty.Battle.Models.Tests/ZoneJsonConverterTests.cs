using System.Text.Json;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Models.Json;

namespace OverloadParty.Battle.Tests.Models;

public class ZoneJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ZoneJsonConverterFactory() },
    };

    [Trait("対象", "ゾーンの JSON 変換")]
    public class Deserialization
    {
        [Fact(DisplayName = "ゾーンの JSON が null リテラルのとき、null として復元される")]
        public void NullLiteral_RestoresAsNull()
        {
            var restored = JsonSerializer.Deserialize<Zone<DeployedResource>>("null", Options);

            restored.Should().BeNull();
        }

        [Fact(DisplayName = "空きスロットを含むゾーンは、位置を保って往復する")]
        public void SlotsWithGaps_RoundTripPreservesPositions()
        {
            var zone = new Zone<DeployedResource>(3);
            zone[1] = TestFactory.MakeResource(instanceId: "inst_mid");

            var json = JsonSerializer.Serialize(zone, Options);
            var restored = JsonSerializer.Deserialize<Zone<DeployedResource>>(json, Options);

            restored.Should().NotBeNull();
            restored![0].Should().BeNull();
            restored[2].Should().BeNull();
            restored[1].Should().NotBeNull();
            restored[1]!.InstanceID.Should().Be("inst_mid");
        }
    }
}

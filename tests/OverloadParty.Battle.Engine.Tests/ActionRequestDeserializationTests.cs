using System.Text.Json;
using OverloadParty.Battle.Engine.Processors;

namespace OverloadParty.Battle.Tests.Engine;

public class ActionRequestDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Trait("対象", "カードプレイ要求のデシリアライズ")]
    public class PlayCardRequestDeserialization
    {
        [Fact(DisplayName = "position をネストした JSON から zone frontend と index 0 を読み取る")]
        public void PlayCardRequest_NestedPosition_DeserializesCorrectly()
        {
            var json = """{ "cardInstanceId": "h1", "position": { "zone": "frontend", "index": 0 } }""";

            var req = JsonSerializer.Deserialize<PlayCardRequest>(json, JsonOpts)!;

            req.CardInstanceID.Should().Be("h1");
            req.Zone.Should().Be("frontend");
            req.Index.Should().Be(0);
        }

        [Fact(DisplayName = "zone と index を直接置いた平坦な JSON から zone frontend と index 0 を読み取る")]
        public void PlayCardRequest_FlatFormat_DeserializesCorrectly()
        {
            var json = """{ "cardInstanceId": "h1", "zone": "frontend", "index": 0 }""";

            var req = JsonSerializer.Deserialize<PlayCardRequest>(json, JsonOpts)!;

            req.CardInstanceID.Should().Be("h1");
            req.Zone.Should().Be("frontend");
            req.Index.Should().Be(0);
        }

        [Fact(DisplayName = "JsonElement 経由でも position をネストした JSON から zone backend と index 2 を読み取る")]
        public void PlayCardRequest_NestedPosition_ViaJsonElement()
        {
            var json = """{ "cardInstanceId": "h1", "position": { "zone": "backend", "index": 2 } }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<PlayCardRequest>(JsonOpts)!;

            req.CardInstanceID.Should().Be("h1");
            req.Zone.Should().Be("backend");
            req.Index.Should().Be(2);
        }
    }

    [Trait("対象", "スケールアップ要求のデシリアライズ")]
    public class ScaleUpRequestDeserialization
    {
        [Fact(DisplayName = "componentInstanceId をインスタンス ID にマッピングする")]
        public void ScaleUpRequest_ComponentInstanceId_MapsToInstanceID()
        {
            var json = """{ "componentInstanceId": "inst_1", "targetRank": "medium" }""";

            var req = JsonSerializer.Deserialize<ScaleUpRequest>(json, JsonOpts)!;

            req.InstanceID.Should().Be("inst_1");
            req.TargetRank.Should().Be("medium");
        }

        [Fact(DisplayName = "instanceId をインスタンス ID にマッピングする")]
        public void ScaleUpRequest_DirectInstanceId_MapsToInstanceID()
        {
            var json = """{ "instanceId": "inst_1", "targetRank": "medium" }""";

            var req = JsonSerializer.Deserialize<ScaleUpRequest>(json, JsonOpts)!;

            req.InstanceID.Should().Be("inst_1");
            req.TargetRank.Should().Be("medium");
        }

        [Fact(DisplayName = "JsonElement 経由で componentInstanceId をインスタンス ID にマッピングする")]
        public void ScaleUpRequest_ViaJsonElement()
        {
            var json = """{ "componentInstanceId": "inst_2", "targetRank": "large" }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<ScaleUpRequest>(JsonOpts)!;

            req.InstanceID.Should().Be("inst_2");
            req.TargetRank.Should().Be("large");
        }
    }

    [Trait("対象", "収益化要求のデシリアライズ")]
    public class MonetizeRequestDeserialization
    {
        [Fact(DisplayName = "distributions の componentInstanceId と amount を順序どおり読み取る")]
        public void MonetizeRequest_WithComponentInstanceId_DeserializesCorrectly()
        {
            var json = """
            {
                "distributions": [
                    { "componentInstanceId": "db_1", "amount": 200 },
                    { "componentInstanceId": "db_2", "amount": 300 }
                ]
            }
            """;

            var req = JsonSerializer.Deserialize<MonetizeRequest>(json, JsonOpts)!;

            req.Distributions.Should().HaveCount(2);
            req.Distributions.Select(d => d.InstanceID).Should().ContainInOrder("db_1", "db_2");
            req.Distributions.Select(d => d.Amount).Should().ContainInOrder(200L, 300L);
        }

        [Fact(DisplayName = "JsonElement 経由で distributions の componentInstanceId を読み取る")]
        public void MonetizeRequest_ViaJsonElement()
        {
            var json = """
            {
                "distributions": [
                    { "componentInstanceId": "db_1", "amount": 100 }
                ]
            }
            """;

            var req = JsonDocument.Parse(json).RootElement.Deserialize<MonetizeRequest>(JsonOpts)!;

            req.Distributions.Should().ContainSingle()
                .Which.InstanceID.Should().Be("db_1");
        }
    }

    [Trait("対象", "攻撃要求のデシリアライズ")]
    public class AttackRequestDeserialization
    {
        [Fact(DisplayName = "attackerInstanceId と targetInstanceId を読み取る")]
        public void AttackRequest_DeserializesCorrectly()
        {
            var json = """{ "attackerInstanceId": "a1", "targetInstanceId": "t1" }""";

            var req = JsonSerializer.Deserialize<AttackRequest>(json, JsonOpts)!;

            req.AttackerInstanceID.Should().Be("a1");
            req.TargetInstanceID.Should().Be("t1");
        }

        [Fact(DisplayName = "JsonElement 経由で attackerInstanceId と targetInstanceId を読み取る")]
        public void AttackRequest_ViaJsonElement()
        {
            var json = """{ "attackerInstanceId": "a1", "targetInstanceId": "t1" }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<AttackRequest>(JsonOpts)!;

            req.AttackerInstanceID.Should().Be("a1");
            req.TargetInstanceID.Should().Be("t1");
        }
    }

    [Trait("対象", "起動効果使用要求のデシリアライズ")]
    public class UseIgnitionRequestDeserialization
    {
        [Fact(DisplayName = "instanceId を読み取る")]
        public void UseIgnitionRequest_DeserializesCorrectly()
        {
            var json = """{ "instanceId": "e1" }""";

            var req = JsonSerializer.Deserialize<UseIgnitionRequest>(json, JsonOpts)!;

            req.InstanceID.Should().Be("e1");
        }

        [Fact(DisplayName = "JsonElement 経由で instanceId と targetInstanceId を読み取る")]
        public void UseIgnitionRequest_ViaJsonElement()
        {
            var json = """{ "instanceId": "e1", "targetInstanceId": "t1" }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<UseIgnitionRequest>(JsonOpts)!;

            req.InstanceID.Should().Be("e1");
            req.TargetInstanceID.Should().Be("t1");
        }
    }

    [Trait("対象", "手札破棄要求のデシリアライズ")]
    public class DiscardHandRequestDeserialization
    {
        [Fact(DisplayName = "cardInstanceIds の配列を読み取る")]
        public void DiscardHandRequest_DeserializesCorrectly()
        {
            var json = """{ "cardInstanceIds": ["c1", "c2"] }""";

            var req = JsonSerializer.Deserialize<DiscardHandRequest>(json, JsonOpts)!;

            req.CardInstanceIDs.Should().Equal("c1", "c2");
        }

        [Fact(DisplayName = "JsonElement 経由で cardInstanceIds の配列を読み取る")]
        public void DiscardHandRequest_ViaJsonElement()
        {
            var json = """{ "cardInstanceIds": ["c1", "c2", "c3"] }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<DiscardHandRequest>(JsonOpts)!;

            req.CardInstanceIDs.Should().Equal("c1", "c2", "c3");
        }
    }
}

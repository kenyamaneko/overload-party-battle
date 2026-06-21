using System.Text.Json;
using OverloadParty.Battle.Engine.Processors;

namespace OverloadParty.Battle.Tests.Engine;

public class ActionRequestDeserializationTests
{
    /// <summary>Shared setup for action-request deserialization tests (camelCase, case-insensitive JSON options).</summary>
    public abstract class Base
    {
        protected static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
    }

    /// <summary>Tests for deserializing PlayCardRequest in nested and flat formats.</summary>
    public class PlayCardRequestDeserialization : Base
    {
        [Fact]
        public void PlayCardRequest_NestedPosition_DeserializesCorrectly()
        {
            var json = """{ "cardInstanceId": "h1", "position": { "zone": "frontend", "index": 0 } }""";

            var req = JsonSerializer.Deserialize<PlayCardRequest>(json, JsonOpts)!;

            req.CardInstanceID.Should().Be("h1");
            req.Zone.Should().Be("frontend");
            req.Index.Should().Be(0);
        }

        [Fact]
        public void PlayCardRequest_FlatFormat_DeserializesCorrectly()
        {
            var json = """{ "cardInstanceId": "h1", "zone": "frontend", "index": 0 }""";

            var req = JsonSerializer.Deserialize<PlayCardRequest>(json, JsonOpts)!;

            req.CardInstanceID.Should().Be("h1");
            req.Zone.Should().Be("frontend");
            req.Index.Should().Be(0);
        }

        [Fact]
        public void PlayCardRequest_NestedPosition_ViaJsonElement()
        {
            var json = """{ "cardInstanceId": "h1", "position": { "zone": "backend", "index": 2 } }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<PlayCardRequest>(JsonOpts)!;

            req.CardInstanceID.Should().Be("h1");
            req.Zone.Should().Be("backend");
            req.Index.Should().Be(2);
        }
    }

    /// <summary>Tests for deserializing ScaleUpRequest from component or direct instance id.</summary>
    public class ScaleUpRequestDeserialization : Base
    {
        [Fact]
        public void ScaleUpRequest_ComponentInstanceId_MapsToInstanceID()
        {
            var json = """{ "componentInstanceId": "inst_1", "targetRank": "medium" }""";

            var req = JsonSerializer.Deserialize<ScaleUpRequest>(json, JsonOpts)!;

            req.InstanceID.Should().Be("inst_1");
            req.TargetRank.Should().Be("medium");
        }

        [Fact]
        public void ScaleUpRequest_DirectInstanceId_MapsToInstanceID()
        {
            var json = """{ "instanceId": "inst_1", "targetRank": "medium" }""";

            var req = JsonSerializer.Deserialize<ScaleUpRequest>(json, JsonOpts)!;

            req.InstanceID.Should().Be("inst_1");
            req.TargetRank.Should().Be("medium");
        }

        [Fact]
        public void ScaleUpRequest_ViaJsonElement()
        {
            var json = """{ "componentInstanceId": "inst_2", "targetRank": "large" }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<ScaleUpRequest>(JsonOpts)!;

            req.InstanceID.Should().Be("inst_2");
            req.TargetRank.Should().Be("large");
        }
    }

    /// <summary>Tests for deserializing MonetizeRequest distributions.</summary>
    public class MonetizeRequestDeserialization : Base
    {
        [Fact]
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

        [Fact]
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

    /// <summary>Tests for deserializing AttackRequest.</summary>
    public class AttackRequestDeserialization : Base
    {
        [Fact]
        public void AttackRequest_DeserializesCorrectly()
        {
            var json = """{ "attackerInstanceId": "a1", "targetInstanceId": "t1" }""";

            var req = JsonSerializer.Deserialize<AttackRequest>(json, JsonOpts)!;

            req.AttackerInstanceID.Should().Be("a1");
            req.TargetInstanceID.Should().Be("t1");
        }

        [Fact]
        public void AttackRequest_ViaJsonElement()
        {
            var json = """{ "attackerInstanceId": "a1", "targetInstanceId": "t1" }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<AttackRequest>(JsonOpts)!;

            req.AttackerInstanceID.Should().Be("a1");
            req.TargetInstanceID.Should().Be("t1");
        }
    }

    /// <summary>Tests for deserializing UseEffectRequest.</summary>
    public class UseEffectRequestDeserialization : Base
    {
        [Fact]
        public void UseEffectRequest_DeserializesCorrectly()
        {
            var json = """{ "instanceId": "e1" }""";

            var req = JsonSerializer.Deserialize<UseEffectRequest>(json, JsonOpts)!;

            req.InstanceID.Should().Be("e1");
        }

        [Fact]
        public void UseEffectRequest_ViaJsonElement()
        {
            var json = """{ "instanceId": "e1", "targetInstanceId": "t1" }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<UseEffectRequest>(JsonOpts)!;

            req.InstanceID.Should().Be("e1");
            req.TargetInstanceID.Should().Be("t1");
        }
    }

    /// <summary>Tests for deserializing DiscardHandRequest card id lists.</summary>
    public class DiscardHandRequestDeserialization : Base
    {
        [Fact]
        public void DiscardHandRequest_DeserializesCorrectly()
        {
            var json = """{ "cardInstanceIds": ["c1", "c2"] }""";

            var req = JsonSerializer.Deserialize<DiscardHandRequest>(json, JsonOpts)!;

            req.CardInstanceIDs.Should().Equal("c1", "c2");
        }

        [Fact]
        public void DiscardHandRequest_ViaJsonElement()
        {
            var json = """{ "cardInstanceIds": ["c1", "c2", "c3"] }""";

            var req = JsonDocument.Parse(json).RootElement.Deserialize<DiscardHandRequest>(JsonOpts)!;

            req.CardInstanceIDs.Should().Equal("c1", "c2", "c3");
        }
    }
}

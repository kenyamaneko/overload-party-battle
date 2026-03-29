using System.Text.Json;
using OverloadParty.Battle.Engine.Processors;

namespace OverloadParty.Battle.Tests.Tests.Service;

/// <summary>
/// Tests the NPC action data deserialization pipeline.
/// Mirrors the pattern used by GameService.DeserializeNpcActionData:
/// Dictionary&lt;string, object&gt; → JsonElement → typed request.
/// </summary>
public class GameServiceDeserializationTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static T SerializeAndDeserialize<T>(Dictionary<string, object> data)
    {
        var json = JsonSerializer.SerializeToElement(data, Opts);
        return json.Deserialize<T>(Opts)!;
    }

    // ─── play_card with nested position ──────────────────────

    [Fact]
    public void PlayCard_WithNestedPosition_DeserializesToPlayCardRequest()
    {
        var data = new Dictionary<string, object>
        {
            ["cardInstanceId"] = "h1",
            ["position"] = new { zone = "frontend", index = 0 },
        };

        var result = SerializeAndDeserialize<PlayCardRequest>(data);

        result.CardInstanceID.Should().Be("h1");
        result.Zone.Should().Be("frontend");
        result.Index.Should().Be(0);
    }

    [Fact]
    public void PlayCard_WithNestedPosition_BackendIndex2()
    {
        var data = new Dictionary<string, object>
        {
            ["cardInstanceId"] = "h2",
            ["position"] = new { zone = "backend", index = 2 },
        };

        var result = SerializeAndDeserialize<PlayCardRequest>(data);

        result.Zone.Should().Be("backend");
        result.Index.Should().Be(2);
    }

    // ─── attack ──────────────────────────────────────────────

    [Fact]
    public void Attack_DeserializesToAttackRequest()
    {
        var data = new Dictionary<string, object>
        {
            ["attackerInstanceId"] = "a1",
            ["targetInstanceId"] = "t1",
        };

        var result = SerializeAndDeserialize<AttackRequest>(data);

        result.AttackerInstanceID.Should().Be("a1");
        result.TargetInstanceID.Should().Be("t1");
    }

    // ─── scale_up ────────────────────────────────────────────

    [Fact]
    public void ScaleUp_WithComponentInstanceId_MapsToInstanceID()
    {
        var data = new Dictionary<string, object>
        {
            ["componentInstanceId"] = "inst_1",
            ["targetRank"] = "medium",
        };

        var result = SerializeAndDeserialize<ScaleUpRequest>(data);

        result.InstanceID.Should().Be("inst_1");
        result.TargetRank.Should().Be("medium");
    }

    [Fact]
    public void ScaleUp_WithInstanceFamily()
    {
        var data = new Dictionary<string, object>
        {
            ["componentInstanceId"] = "inst_2",
            ["targetRank"] = "large",
            ["instanceFamily"] = "C",
        };

        var result = SerializeAndDeserialize<ScaleUpRequest>(data);

        result.InstanceID.Should().Be("inst_2");
        result.TargetRank.Should().Be("large");
        result.InstanceFamily.Should().Be("C");
    }

    // ─── monetize ────────────────────────────────────────────

    [Fact]
    public void Monetize_WithComponentInstanceId_MapsToInstanceID()
    {
        var data = new Dictionary<string, object>
        {
            ["distributions"] = new[]
            {
                new { componentInstanceId = "inst_back_1", amount = 200 },
                new { componentInstanceId = "inst_back_2", amount = 100 },
            },
        };

        var result = SerializeAndDeserialize<MonetizeRequest>(data);

        result.Distributions.Should().HaveCount(2);
        result.Distributions[0].InstanceID.Should().Be("inst_back_1");
        result.Distributions[0].Amount.Should().Be(200);
        result.Distributions[1].InstanceID.Should().Be("inst_back_2");
        result.Distributions[1].Amount.Should().Be(100);
    }

    // ─── use_effect ─────────────────────────────────────

    [Fact]
    public void UseEffect_DeserializesToUseEffectRequest()
    {
        var data = new Dictionary<string, object>
        {
            ["instanceId"] = "e1",
        };

        var result = SerializeAndDeserialize<UseEffectRequest>(data);

        result.InstanceID.Should().Be("e1");
    }

    [Fact]
    public void UseEffect_WithTargetInstanceId()
    {
        var data = new Dictionary<string, object>
        {
            ["instanceId"] = "e2",
            ["targetInstanceId"] = "target_1",
        };

        var result = SerializeAndDeserialize<UseEffectRequest>(data);

        result.InstanceID.Should().Be("e2");
        result.TargetInstanceID.Should().Be("target_1");
    }

    // ─── discard_hand ────────────────────────────────────────

    [Fact]
    public void DiscardHand_DeserializesToDiscardHandRequest()
    {
        var data = new Dictionary<string, object>
        {
            ["cardInstanceIds"] = new[] { "c1", "c2" },
        };

        var result = SerializeAndDeserialize<DiscardHandRequest>(data);

        result.CardInstanceIDs.Should().HaveCount(2);
        result.CardInstanceIDs.Should().Contain("c1");
        result.CardInstanceIDs.Should().Contain("c2");
    }

    [Fact]
    public void DiscardHand_SingleCard()
    {
        var data = new Dictionary<string, object>
        {
            ["cardInstanceIds"] = new[] { "c_only" },
        };

        var result = SerializeAndDeserialize<DiscardHandRequest>(data);

        result.CardInstanceIDs.Should().ContainSingle().Which.Should().Be("c_only");
    }

    // ─── migrate ─────────────────────────────────────────────

    [Fact]
    public void Migrate_DeserializesToMigrateRequest()
    {
        var data = new Dictionary<string, object>
        {
            ["sourceInstanceId"] = "src_1",
            ["targetInstanceId"] = "tgt_1",
        };

        var result = SerializeAndDeserialize<MigrateRequest>(data);

        result.SourceInstanceID.Should().Be("src_1");
        result.TargetInstanceID.Should().Be("tgt_1");
    }
}

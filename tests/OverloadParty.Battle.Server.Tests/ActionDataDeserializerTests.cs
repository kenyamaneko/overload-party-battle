using System.Text.Json;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Server;

/// <summary>action_type ごとの JSON ペイロードが、本番の ActionDataDeserializer.Deserialize を通して対応するリクエスト型に変換されることを検証する。</summary>
public class ActionDataDeserializerTests
{
    /// <summary>Dictionary で組み立てたペイロードを本番の ActionDataDeserializer.Deserialize に通す。</summary>
    /// <param name="actionType">デシリアライズ対象のアクション種別。</param>
    /// <param name="data">JSON ペイロードの元になる Dictionary。</param>
    /// <returns>デシリアライズされたリクエスト。</returns>
    private static T Deserialize<T>(ActionType actionType, Dictionary<string, object> data) =>
        (T)ActionDataDeserializer.Deserialize(actionType, JsonSerializer.SerializeToElement(data));

    [Trait("対象", "play_card のデシリアライズ")]
    public class PlayCard
    {
        [Fact(DisplayName = "入れ子 position を持つ play_card がゾーン frontend・index 0 のカードプレイ要求になる")]
        public void WithNestedPosition_DeserializesToPlayCardRequest()
        {
            var data = new Dictionary<string, object>
            {
                ["cardInstanceId"] = "h1",
                ["position"] = new { zone = "frontend", index = 0 },
            };

            var result = Deserialize<PlayCardRequest>(ActionType.PlayCard, data);

            result.CardInstanceID.Should().Be("h1");
            result.Zone.Should().Be("frontend");
            result.Index.Should().Be(0);
        }

        [Fact(DisplayName = "入れ子 position を持つ play_card がゾーン backend・index 2 のカードプレイ要求になる")]
        public void WithNestedPosition_BackendIndex2()
        {
            var data = new Dictionary<string, object>
            {
                ["cardInstanceId"] = "h2",
                ["position"] = new { zone = "backend", index = 2 },
            };

            var result = Deserialize<PlayCardRequest>(ActionType.PlayCard, data);

            result.Zone.Should().Be("backend");
            result.Index.Should().Be(2);
        }
    }

    [Trait("対象", "attack のデシリアライズ")]
    public class Attack
    {
        [Fact(DisplayName = "attack データが attacker・target の instance id を持つ攻撃要求になる")]
        public void DeserializesToAttackRequest()
        {
            var data = new Dictionary<string, object>
            {
                ["attackerInstanceId"] = "a1",
                ["targetInstanceId"] = "t1",
            };

            var result = Deserialize<AttackRequest>(ActionType.Attack, data);

            result.AttackerInstanceID.Should().Be("a1");
            result.TargetInstanceID.Should().Be("t1");
        }
    }

    [Trait("対象", "scale_up のデシリアライズ")]
    public class ScaleUp
    {
        [Fact(DisplayName = "scale_up の componentInstanceId がスケールアップ要求のインスタンス ID にマップされる")]
        public void WithComponentInstanceId_MapsToInstanceID()
        {
            var data = new Dictionary<string, object>
            {
                ["componentInstanceId"] = "inst_1",
                ["targetRank"] = "medium",
            };

            var result = Deserialize<ScaleUpRequest>(ActionType.ScaleUp, data);

            result.InstanceID.Should().Be("inst_1");
            result.TargetRank.Should().Be("medium");
        }

        [Fact(DisplayName = "instanceFamily を持つ scale_up がインスタンスファミリーを含めてスケールアップ要求になる")]
        public void WithInstanceFamily()
        {
            var data = new Dictionary<string, object>
            {
                ["componentInstanceId"] = "inst_2",
                ["targetRank"] = "large",
                ["instanceFamily"] = "C",
            };

            var result = Deserialize<ScaleUpRequest>(ActionType.ScaleUp, data);

            result.InstanceID.Should().Be("inst_2");
            result.TargetRank.Should().Be("large");
            result.InstanceFamily.Should().Be("C");
        }
    }

    [Trait("対象", "monetize のデシリアライズ")]
    public class Monetize
    {
        [Fact(DisplayName = "monetize の distributions が componentInstanceId をインスタンス ID にマップして変換される")]
        public void WithComponentInstanceId_MapsToInstanceID()
        {
            var data = new Dictionary<string, object>
            {
                ["distributions"] = new[]
                {
                    new { componentInstanceId = "inst_back_1", amount = 200 },
                    new { componentInstanceId = "inst_back_2", amount = 100 },
                },
            };

            var result = Deserialize<MonetizeRequest>(ActionType.Monetize, data);

            result.Distributions.Should().HaveCount(2);
            result.Distributions[0].InstanceID.Should().Be("inst_back_1");
            result.Distributions[0].Amount.Should().Be(200);
            result.Distributions[1].InstanceID.Should().Be("inst_back_2");
            result.Distributions[1].Amount.Should().Be(100);
        }
    }

    [Trait("対象", "use_ignition のデシリアライズ")]
    public class UseIgnition
    {
        [Fact(DisplayName = "instanceId を持つ use_ignition が起動効果使用要求にデシリアライズされる")]
        public void DeserializesToUseIgnitionRequest()
        {
            var data = new Dictionary<string, object>
            {
                ["instanceId"] = "e1",
            };

            var result = Deserialize<UseIgnitionRequest>(ActionType.UseIgnition, data);

            result.InstanceID.Should().Be("e1");
        }

        [Fact(DisplayName = "targetInstanceId を持つ use_ignition が対象インスタンス ID を含めて変換される")]
        public void WithTargetInstanceId()
        {
            var data = new Dictionary<string, object>
            {
                ["instanceId"] = "e2",
                ["targetInstanceId"] = "target_1",
            };

            var result = Deserialize<UseIgnitionRequest>(ActionType.UseIgnition, data);

            result.InstanceID.Should().Be("e2");
            result.TargetInstanceID.Should().Be("target_1");
        }
    }

    [Trait("対象", "discard_hand のデシリアライズ")]
    public class DiscardHand
    {
        [Fact(DisplayName = "複数の cardInstanceIds を持つ discard_hand が全 ID を含めて変換される")]
        public void DeserializesToDiscardHandRequest()
        {
            var data = new Dictionary<string, object>
            {
                ["cardInstanceIds"] = new[] { "c1", "c2" },
            };

            var result = Deserialize<DiscardHandRequest>(ActionType.DiscardHand, data);

            result.CardInstanceIDs.Should().HaveCount(2);
            result.CardInstanceIDs.Should().Contain("c1");
            result.CardInstanceIDs.Should().Contain("c2");
        }

        [Fact(DisplayName = "単一の cardInstanceIds を持つ discard_hand が 1 件で変換される")]
        public void SingleCard()
        {
            var data = new Dictionary<string, object>
            {
                ["cardInstanceIds"] = new[] { "c_only" },
            };

            var result = Deserialize<DiscardHandRequest>(ActionType.DiscardHand, data);

            result.CardInstanceIDs.Should().ContainSingle().Which.Should().Be("c_only");
        }
    }
}

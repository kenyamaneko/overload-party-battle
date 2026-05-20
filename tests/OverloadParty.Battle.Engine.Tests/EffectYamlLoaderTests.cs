using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// EffectYamlLoader が yaml schema を期待通り解釈することを検証する。
/// </summary>
public class EffectYamlLoaderTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement;

    private EffectHandler LoadAndGetHandler(string cardId, string triggerStr, TriggerType trigger, string guardJson, string opsJson)
    {
        var card = new CardDefinition
        {
            CardId = cardId,
            CardName = "T",
            CardType = CardTypes.Compute,
            Faction = "Tuners",
            DeployTurns = 0,
            Effects =
            [
                new EffectDef
                {
                    Trigger = triggerStr,
                    Guard = [Parse(guardJson)],
                    Ops = [Parse(opsJson)],
                },
            ],
        };
        _cc.Add(card);

        var registry = new EffectRegistry();
        var custom = new CustomEffectRegistry();
        EffectYamlLoader.LoadFromCards([card], registry, custom);

        return registry.Get(cardId, trigger)
            ?? throw new InvalidOperationException("handler not registered");
    }

    [Fact]
    public void CountGuard_MaxOnly_AcceptsZeroResources()
    {
        // 「Tuners 3 体以下」(min 省略, max=3) のとき、Tuners が 0 体でも guard 通過することを検証
        var handler = LoadAndGetHandler(
            cardId: "TST-0001",
            triggerStr: "ignition",
            trigger: TriggerType.Ignition,
            guardJson: """
                {
                    "count": {
                        "selector": { "owner": "myself", "faction": "Tuners" },
                        "max": 3
                    }
                }
                """,
            opsJson: """{ "gain_budget": { "target": "myself", "amount": 100 } }""");

        var state = TestFactory.MakeGameState(p1Budget: 1000);
        // フィールドに Tuners 0 体

        var result = handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = new EffectRegistry(),
        });

        result.GuardFailed.Should().BeFalse("max-only count guard should accept 0 resources");
        state.Player1Budget.Should().Be(1100);
    }

    [Fact]
    public void CountGuard_MaxOnly_RejectsCountOverMax()
    {
        var handler = LoadAndGetHandler(
            cardId: "TST-0002",
            triggerStr: "ignition",
            trigger: TriggerType.Ignition,
            guardJson: """
                {
                    "count": {
                        "selector": { "owner": "myself", "faction": "Tuners" },
                        "max": 2
                    }
                }
                """,
            opsJson: """{ "gain_budget": { "target": "myself", "amount": 100 } }""");

        _cc.Add(TestFactory.ComputeCard(cardId: "TN-A", faction: "Tuners"));
        var state = TestFactory.MakeGameState(p1Budget: 1000);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TN-A", instanceId: "t1");
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TN-A", instanceId: "t2");
        state.Player1Field.Frontend[2] = TestFactory.MakeResource(cardId: "TN-A", instanceId: "t3");
        // フィールドに Tuners 3 体 (max=2 を超過)

        var result = handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = new EffectRegistry(),
        });

        result.GuardFailed.Should().BeTrue("max-only count guard should reject when count exceeds max");
        state.Player1Budget.Should().Be(1000);
    }

    [Fact]
    public void CountGuard_MinOnly_RequiresAtLeastMin()
    {
        // min 指定だけの count guard は従来通り min 以上必要 (0 体なら失敗)
        var handler = LoadAndGetHandler(
            cardId: "TST-0003",
            triggerStr: "ignition",
            trigger: TriggerType.Ignition,
            guardJson: """
                {
                    "count": {
                        "selector": { "owner": "myself", "faction": "Tuners" },
                        "min": 2
                    }
                }
                """,
            opsJson: """{ "gain_budget": { "target": "myself", "amount": 100 } }""");

        var state = TestFactory.MakeGameState(p1Budget: 1000);
        // フィールドに Tuners 0 体 (min=2 を満たさない)

        var result = handler(new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = 1,
            CardCache = _cc,
            Effects = new EffectRegistry(),
        });

        result.GuardFailed.Should().BeTrue("min=2 should reject when count is 0");
        state.Player1Budget.Should().Be(1000);
    }
}

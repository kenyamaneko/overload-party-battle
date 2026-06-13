using System.Reflection;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Data;

/// <summary>
/// Reflection-driven tests that protect EventDataSerializer from silently going stale
/// when new event types or record types are added. These are the "update reminder" tests
/// that turn red when the discriminator map is out of sync with either the EventTypes/
/// ActionTypes constants or the set of IEventData implementations.
/// </summary>
public class EventDataSerializerCoverageTests
{
    /// <summary>
    /// ActionTypes that do not carry an EventData payload and therefore don't need
    /// to be registered in EventDataSerializer. Each entry must be justified.
    /// </summary>
    private static readonly HashSet<string> NonEventActionTypes = new()
    {
        // Control actions: no payload emitted by the engine.
        ActionTypes.EndPhase,
        ActionTypes.Forfeit,
        // Reactive / SetReactive are carrier action names; events use play_card / attach_card
        // and reactive_revealed instead. No dedicated EventData type exists.
        ActionTypes.SetReactive,
        ActionTypes.Reactive,
        // 保留中の reactive 選択を解決する操作。effect 再実行で発生するイベント
        // (play_card / attack 等) を返すだけで、resolve 自体の payload は持たない。
        ActionTypes.ResolvePendingChoice,
    };

    public static IEnumerable<string> AllEventTypeStrings() =>
        typeof(EventTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .Concat(
                typeof(ActionTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                    .Select(f => (string)f.GetValue(null)!))
            .Distinct();

    [Fact]
    public void EventDataSerializer_HandlesAllExpectedEventTypes()
    {
        var missing = AllEventTypeStrings()
            .Where(s => !NonEventActionTypes.Contains(s))
            .Where(et => EventDataSerializer.GetTypeForEventType(et) is null)
            .ToList();

        missing.Should().BeEmpty(
            $"EventDataSerializer must handle every event-emitting EventTypes/ActionTypes constant. " +
            $"If a new constant is legitimately not an event payload, add it to NonEventActionTypes " +
            $"with a comment explaining why. Missing: [{string.Join(", ", missing)}]");
    }

    [Fact]
    public void EventDataSerializer_AllIEventDataTypesAreReachable()
    {
        var assemblies = new[]
        {
            typeof(IEventData).Assembly,
            typeof(TurnStartInternalEventData).Assembly,
        };

        var allImplementations = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IEventData).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToHashSet();

        var reachable = EventDataSerializer.GetAllRegisteredTypes().ToHashSet();

        // TurnStartEventData is the viewer-facing wire variant produced by GameService.MapEventData;
        // it never enters the repository boundary so it must NOT be registered.
        var intentionallyUnreachable = new HashSet<Type> { typeof(TurnStartEventData) };

        var orphans = allImplementations
            .Except(reachable)
            .Except(intentionallyUnreachable)
            .ToList();

        orphans.Should().BeEmpty(
            "every IEventData implementation must be registered in EventDataSerializer " +
            "(or explicitly excluded as viewer-only). " +
            $"Orphans: [{string.Join(", ", orphans.Select(t => t.Name))}]");
    }

    /// <summary>
    /// Hand-maintained sample instances used for round-trip verification.
    /// Adding a new IEventData type requires updating this dict — which the companion
    /// test <see cref="RoundTripSamples_CoverAllRegisteredTypes"/> enforces.
    /// </summary>
    private static readonly Dictionary<Type, IEventData> RoundTripSamples = new()
    {
        [typeof(PlayCardEventData)] = new PlayCardEventData { CardId = "TST-0001", Zone = "frontend", Index = 0 },
        [typeof(AttachCardEventData)] = new AttachCardEventData { CardId = "TST-0002", TargetId = "inst_7" },
        [typeof(AttackEventData)] = new AttackEventData { AttackerId = "a", TargetId = "d", Damage = 300, Destroyed = false, SlaPenalty = 0 },
        [typeof(ScaleUpEventData)] = new ScaleUpEventData { InstanceId = "i", TargetRank = "medium", InstanceFamily = "M" },
        [typeof(MonetizeEventData)] = new MonetizeEventData { TotalAmount = 100 },
        [typeof(DiscardHandEventData)] = new DiscardHandEventData { DiscardedCount = 2, DiscardedIds = ["x", "y"] },
        [typeof(UseEffectEventData)] = new UseEffectEventData { CardId = "c", SourceId = "s", TargetId = "t" },
        [typeof(UseInitiativeEventData)] = new UseInitiativeEventData { Faction = "SHE", Kind = "routine", InitiativeName = "R", InsightCost = 400 },
        [typeof(PhaseChangeEventData)] = new PhaseChangeEventData { PreviousPhase = "main", CurrentPhase = "battle" },
        [typeof(PhaseEndEventData)] = new PhaseEndEventData { Phase = "end", NeedsDiscard = false },
        [typeof(TurnEndEventData)] = new TurnEndEventData { Phase = "end", NextTurn = 2, ActivePlayer = 2, CurrentPhase = "draw" },
        [typeof(BattleStartEventData)] = new BattleStartEventData { MatchType = "npc", MyName = "me", MyLevel = 1, OpponentName = "opp", OpponentLevel = 1 },
        [typeof(TurnStartInternalEventData)] = new TurnStartInternalEventData { Turn = 3, ActivePlayer = 1 },
        [typeof(ReactiveRevealedEventData)] = new ReactiveRevealedEventData { InstanceId = "i", CardId = "c" },
        [typeof(GameOverEventData)] = new GameOverEventData { WinnerNum = 1, WinReason = WinReasons.BudgetZero },
        [typeof(SelectSlotEventData)] = new SelectSlotEventData { CardId = "c", InstanceId = "i", Zone = "frontend", Index = 0 },
    };

    [Fact]
    public void RoundTripSamples_CoverAllRegisteredTypes()
    {
        var reachable = EventDataSerializer.GetAllRegisteredTypes().ToHashSet();
        var sampled = RoundTripSamples.Keys.ToHashSet();

        reachable.Except(sampled).Should().BeEmpty(
            "every registered IEventData type must have a round-trip sample in RoundTripSamples. " +
            "Add one when you register a new event type.");
        sampled.Except(reachable).Should().BeEmpty(
            "RoundTripSamples must not contain types that are no longer registered. Remove stale entries.");
    }

    public static IEnumerable<object[]> RegisteredEventTypes() =>
        EventDataSerializer.GetAllRegisteredEventTypes().Select(et => new object[] { et });

    [Theory]
    [MemberData(nameof(RegisteredEventTypes))]
    public void EventDataSerializer_RoundTrip_ProducesSameRuntimeType(string eventType)
    {
        var expectedType = EventDataSerializer.GetTypeForEventType(eventType);
        expectedType.Should().NotBeNull($"{eventType} must be registered");

        var sample = RoundTripSamples[expectedType!];
        var json = EventDataSerializer.Serialize(sample);
        var restored = EventDataSerializer.Deserialize(eventType, json);

        restored.GetType().Should().Be(expectedType);
    }

    [Fact]
    public void EventDataSerializer_Deserialize_UnknownEventType_Throws()
    {
        var act = () => EventDataSerializer.Deserialize("not_a_real_event_type", "{}");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown event type*");
    }

    [Fact]
    public void EventDataSerializer_SerializeToElement_Null_ProducesJsonNull()
    {
        var elem = EventDataSerializer.SerializeToElement(null);
        elem.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public void EventDataSerializer_Serialize_UsesCamelCaseKeys()
    {
        // Guards the wire/JSONB format against an accidental naming-policy change.
        // The previous Dictionary<string, object> format used camelCase keys (cardId, instanceId...)
        // so byte-identical serialization requires the same policy here.
        var json = EventDataSerializer.Serialize(new PlayCardEventData
        {
            CardId = "TST-0001",
            Zone = "frontend",
            Index = 3,
        });
        json.Should().Contain("\"cardId\":\"TST-0001\"");
        json.Should().Contain("\"zone\":\"frontend\"");
        json.Should().Contain("\"index\":3");
    }
}

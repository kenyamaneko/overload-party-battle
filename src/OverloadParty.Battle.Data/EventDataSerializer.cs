using System.Text.Json;
using System.Text.Json.Serialization;
using OverloadParty.Battle.Models;
using OverloadParty.GameLogicConstants;
using OverloadParty.GameState;

namespace OverloadParty.Battle.Data;

/// <summary>
/// Single conversion boundary between persisted JSONB and typed IEventData records.
/// Engine/Service code operates on typed records; only the repository layer touches JSON strings.
/// camelCase naming policy is chosen to match the legacy Dictionary&lt;string, object&gt; format
/// so existing DB rows remain byte-identical and readable after migration.
/// </summary>
public static class EventDataSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<string, Type> _map = new()
    {
        [EventTypes.PlayCard]         = typeof(PlayCardEventData),
        [EventTypes.AttachCard]       = typeof(AttachCardEventData),
        [EventTypes.Attack]           = typeof(AttackEventData),
        [EventTypes.ScaleUp]          = typeof(ScaleUpEventData),
        [EventTypes.Monetize]         = typeof(MonetizeEventData),
        [EventTypes.DiscardHand]      = typeof(DiscardHandEventData),
        [EventTypes.UseEffect]        = typeof(UseEffectEventData),
        [EventTypes.PhaseChange]      = typeof(PhaseChangeEventData),
        [EventTypes.PhaseEnd]         = typeof(PhaseEndEventData),
        [EventTypes.TurnEnd]          = typeof(TurnEndEventData),
        [EventTypes.BattleStart]      = typeof(BattleStartEventData),
        [EventTypes.TurnStart]        = typeof(TurnStartInternalEventData),
        [EventTypes.ReactiveRevealed] = typeof(ReactiveRevealedEventData),
        [EventTypes.GameOver]         = typeof(GameOverEventData),
        [ActionTypes.SelectSlot]      = typeof(SelectSlotEventData),
    };

    /// <summary>Serialize a typed event payload to JSON. Uses the runtime type for polymorphism.</summary>
    public static string Serialize(IEventData data) =>
        JsonSerializer.Serialize(data, data.GetType(), Opts);

    /// <summary>
    /// Serialize a typed event payload (or null) to a JsonElement for the wire envelope.
    /// Uses the runtime type so interface-typed references still emit concrete fields.
    /// Must be used instead of passing <see cref="IEventData"/> directly to JsonSerializer
    /// because System.Text.Json would otherwise serialize only the (empty) interface members.
    /// null is serialized as JSON null (ValueKind=Null), matching legacy behavior when the
    /// old Dictionary&lt;string, object&gt;? field was null.
    /// </summary>
    public static JsonElement SerializeToElement(IEventData? data) =>
        data is null
            ? JsonSerializer.SerializeToElement<object?>(null, Opts)
            : JsonSerializer.SerializeToElement(data, data.GetType(), Opts);

    /// <summary>Deserialize a JSON string into the typed record determined by the event type.</summary>
    public static IEventData Deserialize(string eventType, string json)
    {
        if (!_map.TryGetValue(eventType, out var type))
        {
            throw new InvalidOperationException($"Unknown event type: {eventType}");
        }
        return (IEventData)JsonSerializer.Deserialize(json, type, Opts)!;
    }

    /// <summary>Test/introspection: get the record type for an event type string.</summary>
    public static Type? GetTypeForEventType(string eventType) =>
        _map.TryGetValue(eventType, out var t) ? t : null;

    /// <summary>Test/introspection: all IEventData record types reachable via the discriminator.</summary>
    public static IEnumerable<Type> GetAllRegisteredTypes() => _map.Values;

    /// <summary>Test/introspection: all event-type keys registered in the discriminator.</summary>
    public static IEnumerable<string> GetAllRegisteredEventTypes() => _map.Keys;
}

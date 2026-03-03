using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Mutable pipeline state shared across all ops in an effect execution.
/// Provides cached field access to avoid repeated lookups.
/// </summary>
public class OpContext
{
    public EffectContext Ctx { get; }
    public EffectResult Result { get; } = new();

    private readonly Dictionary<long, Field> _fieldCache = new();

    public OpContext(EffectContext ctx)
    {
        Ctx = ctx;
    }

    public GameState State => Ctx.State;
    public Game Game => Ctx.Game;
    public long PlayerNum => Ctx.PlayerNum;
    public long OpponentNum => State.OpponentOf(PlayerNum);
    public ICardCache CardCache => Ctx.CardCache;

    /// <summary>
    /// Get a player's field. Cached for the duration of this pipeline.
    /// </summary>
    public Field GetField(long playerNum)
    {
        if (!_fieldCache.TryGetValue(playerNum, out var field))
        {
            field = State.GetField(playerNum);
            _fieldCache[playerNum] = field;
        }
        return field;
    }

    public Field MyField => GetField(PlayerNum);
    public Field OpponentField => GetField(OpponentNum);

    /// <summary>
    /// Source resource (the card that triggered the effect).
    /// </summary>
    public ResourceInstance? Source => Ctx.Source;

    /// <summary>
    /// Target resource (may be null if no target specified).
    /// </summary>
    public ResourceInstance? Target => Ctx.Target;

    /// <summary>
    /// Support zone source (for platform/reactive cards).
    /// </summary>
    public SupportInstance? SupSource => Ctx.SupSource;

    /// <summary>
    /// Choice data from the player (for branching effects).
    /// </summary>
    public Dictionary<string, object>? ChoiceData => Ctx.ChoiceData;

    /// <summary>
    /// Add an event to the result.
    /// </summary>
    public void AddEvent(GameEvent evt) => Result.Events.Add(evt);

    /// <summary>
    /// Mark this action as cancelled (for reactive effects).
    /// </summary>
    public void CancelAction() => Result.CancelAction = true;
}

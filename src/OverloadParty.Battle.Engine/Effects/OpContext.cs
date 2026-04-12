using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Mutable pipeline state shared across all ops in an effect execution.
/// Provides cached field access to avoid repeated lookups.
/// </summary>
public class OpContext
{
    /// <summary>The original effect context.</summary>
    public EffectContext Ctx { get; }

    /// <summary>Accumulated result of the pipeline execution.</summary>
    public EffectResult Result { get; } = new();

    private readonly Dictionary<long, Field> _fieldCache = new();

    /// <summary>
    /// Initializes a new <see cref="OpContext"/> wrapping the given effect context.
    /// </summary>
    /// <param name="ctx">The effect context to wrap.</param>
    public OpContext(EffectContext ctx)
    {
        Ctx = ctx;
    }

    /// <summary>Current game state.</summary>
    public BattleGameState State => Ctx.State;

    /// <summary>The game metadata.</summary>
    public Game Game => Ctx.Game;

    /// <summary>Player number of the effect owner.</summary>
    public long PlayerNum => Ctx.PlayerNum;

    /// <summary>Opponent's player number.</summary>
    public long OpponentNum => State.OpponentOf(PlayerNum);

    /// <summary>Card definition cache.</summary>
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

    /// <summary>The effect owner's field (cached).</summary>
    public Field MyField => GetField(PlayerNum);

    /// <summary>The opponent's field (cached).</summary>
    public Field OpponentField => GetField(OpponentNum);

    /// <summary>
    /// Source resource (the card that triggered the effect).
    /// </summary>
    public DeployedResource? Source => Ctx.Source;

    /// <summary>
    /// Target resource (may be null if no target specified).
    /// </summary>
    public DeployedResource? Target => Ctx.Target;

    /// <summary>
    /// サポートゾーン source (for platform/reactive cards).
    /// </summary>
    public DeployedSupport? SupSource => Ctx.SupSource;

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

    /// <summary>
    /// Tracks success/failure of named effect groups within this pipeline execution.
    /// Used by <see cref="Ops.DependentEffectOp"/> to check whether the parent group succeeded.
    /// </summary>
    public Dictionary<string, bool> GroupResults { get; } = [];
}

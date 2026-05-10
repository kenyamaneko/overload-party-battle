namespace OverloadParty.GameState;

/// <summary>Marker interface for all EventData record types.</summary>
public interface IEventData { }

public partial class PlayCardEventData : IEventData { }
public partial class AttachCardEventData : IEventData { }
public partial class AttackEventData : IEventData { }
public partial class ScaleUpEventData : IEventData { }
public partial class MonetizeEventData : IEventData { }
public partial class DiscardHandEventData : IEventData { }
public partial class UseEffectEventData : IEventData { }
public partial class PhaseChangeEventData : IEventData { }
public partial class PhaseEndEventData : IEventData { }
public partial class TurnEndEventData : IEventData { }
public partial class BattleStartEventData : IEventData { }
public partial class TurnStartEventData : IEventData { }
public partial class ReactiveRevealedEventData : IEventData { }
public partial class SelectSlotEventData : IEventData { }
public partial class GameOverEventData : IEventData { }

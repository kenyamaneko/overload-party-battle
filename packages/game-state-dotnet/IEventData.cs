namespace OverloadParty.GameState;

/// <summary>Marker interface for all EventData record types.</summary>
public interface IEventData { }

/// <summary>カードをプレイしたイベントのペイロード。</summary>
public partial class PlayCardEventData : IEventData { }

/// <summary>アタッチメントを装備したイベントのペイロード。</summary>
public partial class AttachCardEventData : IEventData { }

/// <summary>攻撃したイベントのペイロード。</summary>
public partial class AttackEventData : IEventData { }

/// <summary>スケールアップしたイベントのペイロード。</summary>
public partial class ScaleUpEventData : IEventData { }

/// <summary>収益化したイベントのペイロード。</summary>
public partial class MonetizeEventData : IEventData { }

/// <summary>手札を捨てたイベントのペイロード。</summary>
public partial class DiscardHandEventData : IEventData { }

/// <summary>起動効果を使用したイベントのペイロード。</summary>
public partial class UseIgnitionEventData : IEventData { }

/// <summary>施策を使用したイベントのペイロード。</summary>
public partial class UseInitiativeEventData : IEventData { }

/// <summary>フェーズが遷移したイベントのペイロード。</summary>
public partial class PhaseChangeEventData : IEventData { }

/// <summary>フェーズが終了したイベントのペイロード。</summary>
public partial class PhaseEndEventData : IEventData { }

/// <summary>ターンが終了したイベントのペイロード。</summary>
public partial class TurnEndEventData : IEventData { }

/// <summary>ゲーム開始時の情報を表すイベントのペイロード。</summary>
public partial class BattleStartEventData : IEventData { }

/// <summary>ターンが開始したイベントのペイロード。</summary>
public partial class TurnStartEventData : IEventData { }

/// <summary>リアクティブが公開されたイベントのペイロード。</summary>
public partial class ReactiveRevealedEventData : IEventData { }

/// <summary>スロットを選択したイベントのペイロード。</summary>
public partial class SelectSlotEventData : IEventData { }

/// <summary>ゲームが終了したイベントのペイロード。</summary>
public partial class GameOverEventData : IEventData { }

using YamlDotNet.Serialization;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Top-level NPC AI configuration loaded from YAML.
/// One file per faction × difficulty (e.g. SHE-easy.yaml).
/// </summary>
public class AiConfig
{
    [YamlMember(Alias = "model")]
    public string Model { get; set; } = "";

    [YamlMember(Alias = "faction")]
    public string Faction { get; set; } = "";

    [YamlMember(Alias = "display_name")]
    public string? DisplayName { get; set; }

    [YamlMember(Alias = "deck")]
    public List<DeckEntry> Deck { get; set; } = [];

    /// <summary>NPC デッキがセットしたルーチン施策の ID。</summary>
    [YamlMember(Alias = "routine_id")]
    public string RoutineId { get; set; } = "";

    /// <summary>NPC デッキがセットしたスペシャル施策の ID。</summary>
    [YamlMember(Alias = "special_id")]
    public string SpecialId { get; set; } = "";

    [YamlMember(Alias = "budget")]
    public BudgetConfig Budget { get; set; } = new();

    [YamlMember(Alias = "game_phases")]
    public GamePhaseConfig? GamePhases { get; set; }

    [YamlMember(Alias = "deploy")]
    public DeployConfig Deploy { get; set; } = new();

    /// <summary>カードごとの分岐効果の選択 (cardId → 分岐肢キー)。trigger 非依存。</summary>
    [YamlMember(Alias = "branch_choices")]
    public Dictionary<string, string>? BranchChoices { get; set; }

    [YamlMember(Alias = "immediate_cards")]
    public ImmediateConfig ImmediateCards { get; set; } = new();

    [YamlMember(Alias = "effect_priorities")]
    public Dictionary<string, EffectPriorityEntry> EffectPriorities { get; set; } = new();

    [YamlMember(Alias = "target_selection")]
    public TargetSelectionConfig TargetSelection { get; set; } = new();

    [YamlMember(Alias = "scale_up")]
    public ScaleUpConfig ScaleUp { get; set; } = new();

    [YamlMember(Alias = "monetize")]
    public MonetizeConfig Monetize { get; set; } = new();

    /// <summary>施策 ID から使用設定を引く。デッキがセットした各施策を ID で指定する。</summary>
    [YamlMember(Alias = "initiative")]
    public Dictionary<string, InitiativePolicyConfig>? Initiative { get; set; }

    // discard: 既存の deploy/effect/attachment/reactive 優先度で判断するため専用設定不要

    [YamlMember(Alias = "attachments")]
    public Dictionary<string, AttachmentEntry>? Attachments { get; set; }

    [YamlMember(Alias = "reactive")]
    public ReactiveConfig? Reactive { get; set; }

    [YamlMember(Alias = "slot_select")]
    public SlotSelectConfig? SlotSelect { get; set; }
}

// ── Budget ──────────────────────────────────────────────────

public class BudgetConfig
{
    [YamlMember(Alias = "low_threshold")]
    public long LowThreshold { get; set; }

    [YamlMember(Alias = "maintenance_limit_ratio")]
    public double MaintenanceLimitRatio { get; set; }
}

// ── Game Phases ─────────────────────────────────────────────

public class GamePhaseConfig
{
    [YamlMember(Alias = "late")]
    public LatePhaseDef? Late { get; set; }
}

public class LatePhaseDef
{
    [YamlMember(Alias = "condition")]
    public PhaseCondition Condition { get; set; } = new();

    [YamlMember(Alias = "target_selection")]
    public TargetSelectionConfig? TargetSelection { get; set; }

    [YamlMember(Alias = "effect_priorities")]
    public Dictionary<string, EffectPriorityEntry>? EffectPriorities { get; set; }
}

public class PhaseCondition
{
    [YamlMember(Alias = "turn_min")]
    public int? TurnMin { get; set; }

    [YamlMember(Alias = "count")]
    public ConditionDef? Count { get; set; }
}

// ── Deploy ──────────────────────────────────────────────────

public class DeployConfig
{
    [YamlMember(Alias = "priorities")]
    public List<DeployPriorityEntry> Priorities { get; set; } = [];

    [YamlMember(Alias = "conditional_priorities")]
    public List<ConditionalPriorityEntry>? ConditionalPriorities { get; set; }

    [YamlMember(Alias = "zone_preferences")]
    public Dictionary<string, List<string>>? ZonePreferences { get; set; }
}

public class DeployPriorityEntry
{
    [YamlMember(Alias = "card_id")]
    public string? CardId { get; set; }

    [YamlMember(Alias = "card_type")]
    public string? CardType { get; set; }

    [YamlMember(Alias = "priority")]
    public int Priority { get; set; }
}

public class ConditionalPriorityEntry
{
    [YamlMember(Alias = "card_id")]
    public string CardId { get; set; } = "";

    [YamlMember(Alias = "priority")]
    public int Priority { get; set; }

    [YamlMember(Alias = "condition")]
    public ConditionDef Condition { get; set; } = new();

    [YamlMember(Alias = "fallback_priority")]
    public int FallbackPriority { get; set; }
}

// ── Immediate Cards ─────────────────────────────────────────

public class ImmediateConfig
{
    [YamlMember(Alias = "always_use")]
    public List<string> AlwaysUse { get; set; } = [];

    [YamlMember(Alias = "use_conditions")]
    public Dictionary<string, List<ConditionDef>>? UseConditions { get; set; }

    [YamlMember(Alias = "hold_until")]
    public List<HoldUntilEntry>? HoldUntil { get; set; }
}

public class HoldUntilEntry
{
    [YamlMember(Alias = "card_type")]
    public string CardType { get; set; } = "";

    [YamlMember(Alias = "condition")]
    public ConditionDef Condition { get; set; } = new();
}

// ── Effect Priorities ───────────────────────────────────────

/// <summary>
/// Represents a priority entry that can be either a simple int or an object
/// with threshold/condition logic.
/// In YAML: `deploy_free: 75` or `budget_gain: { priority: 90, ... }`
/// </summary>
public class EffectPriorityEntry
{
    [YamlMember(Alias = "priority")]
    public int Priority { get; set; }

    [YamlMember(Alias = "low_priority")]
    public int? LowPriority { get; set; }

    [YamlMember(Alias = "threshold")]
    public long? Threshold { get; set; }

    [YamlMember(Alias = "hand_threshold")]
    public int? HandThreshold { get; set; }

    [YamlMember(Alias = "min_targets")]
    public int? MinTargets { get; set; }

    [YamlMember(Alias = "condition")]
    public ConditionDef? Condition { get; set; }
}

// ── Target Selection ────────────────────────────────────────

public class TargetSpec
{
    [YamlMember(Alias = "selector")]
    public SelectorDef Selector { get; set; } = new();

    [YamlMember(Alias = "order_by")]
    public string? OrderBy { get; set; }
}

public class TargetSelectionConfig
{
    [YamlMember(Alias = "attack")]
    public TargetSpec? Attack { get; set; }

    [YamlMember(Alias = "single_damage")]
    public TargetSpec? SingleDamage { get; set; }

    [YamlMember(Alias = "debuff")]
    public TargetSpec? Debuff { get; set; }

    [YamlMember(Alias = "buff")]
    public TargetSpec? Buff { get; set; }

    [YamlMember(Alias = "heal")]
    public TargetSpec? Heal { get; set; }
}

// ── Scale Up ────────────────────────────────────────────────

public class ScaleUpConfig
{
    [YamlMember(Alias = "instance_family")]
    public string InstanceFamily { get; set; } = "";

    [YamlMember(Alias = "conditional_family")]
    public List<ConditionalFamilyEntry>? ConditionalFamily { get; set; }

    [YamlMember(Alias = "max_maintenance_ratio")]
    public double MaxMaintenanceRatio { get; set; }

    [YamlMember(Alias = "order_by")]
    public string OrderBy { get; set; } = "";
}

public class ConditionalFamilyEntry
{
    [YamlMember(Alias = "family")]
    public string Family { get; set; } = "";

    [YamlMember(Alias = "condition")]
    public ConditionDef Condition { get; set; } = new();
}

// ── Monetize ────────────────────────────────────────────────

public class MonetizeConfig
{
    [YamlMember(Alias = "order_by")]
    public string OrderBy { get; set; } = "";

    [YamlMember(Alias = "reserve_ratio")]
    public double ReserveRatio { get; set; }
}

// ── Initiative ──────────────────────────────────────────────

/// <summary>
/// 施策 1 件の使用条件と優先度。
/// </summary>
public class InitiativePolicyConfig
{
    [YamlMember(Alias = "priority")]
    public int Priority { get; set; }

    /// <summary>使用に必要な Insight プールの下限。スペシャルの温存などに用いる。</summary>
    [YamlMember(Alias = "min_insight")]
    public long? MinInsight { get; set; }

    [YamlMember(Alias = "condition")]
    public ConditionDef? Condition { get; set; }
}

// ── Attachments ─────────────────────────────────────────────

public class AttachmentEntry
{
    [YamlMember(Alias = "priority")]
    public int Priority { get; set; }

    [YamlMember(Alias = "target")]
    public TargetSpec? Target { get; set; }
}

// ── Reactive ────────────────────────────────────────────────

public class ReactiveConfig
{
    [YamlMember(Alias = "max_slots")]
    public int MaxSlots { get; set; }

    [YamlMember(Alias = "priorities")]
    public Dictionary<string, int> Priorities { get; set; } = new();
}

// ── Slot Select ─────────────────────────────────────────────

public class SlotSelectConfig
{
    [YamlMember(Alias = "strategy")]
    public string Strategy { get; set; } = "";
}

// ── Deck ────────────────────────────────────────────────────

public class DeckEntry
{
    [YamlMember(Alias = "card_id")]
    public string CardId { get; set; } = "";

    [YamlMember(Alias = "copies")]
    public int Copies { get; set; } = 1;
}

// ── Shared Condition Definition ─────────────────────────────

/// <summary>
/// Reuses Guard vocabulary from Effect YAML: stat, count, match.
/// </summary>
public class ConditionDef
{
    [YamlMember(Alias = "selector")]
    public SelectorDef? Selector { get; set; }

    [YamlMember(Alias = "stat")]
    public string? Stat { get; set; }

    [YamlMember(Alias = "min")]
    public long? Min { get; set; }

    [YamlMember(Alias = "max")]
    public long? Max { get; set; }

    [YamlMember(Alias = "card_id")]
    public List<string>? CardId { get; set; }

    [YamlMember(Alias = "card_type")]
    public string? CardType { get; set; }

    [YamlMember(Alias = "face_down")]
    public bool? FaceDown { get; set; }
}

public class SelectorDef
{
    [YamlMember(Alias = "owner")]
    public string? Owner { get; set; }

    [YamlMember(Alias = "zone")]
    public string? Zone { get; set; }

    [YamlMember(Alias = "faction")]
    public string? Faction { get; set; }

    [YamlMember(Alias = "card_type")]
    public string? CardType { get; set; }

    [YamlMember(Alias = "card_id")]
    public List<string>? CardId { get; set; }

    [YamlMember(Alias = "face_down")]
    public bool? FaceDown { get; set; }
}

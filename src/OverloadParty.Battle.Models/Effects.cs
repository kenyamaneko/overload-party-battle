namespace OverloadParty.Battle.Models;

// Passive effect type constants
public static class PassiveEffectTypes
{
    public const string TPPerBackendDB = "tp_per_backend_db";
    public const string TPPerBackendData = "tp_per_backend_data";
    public const string TPIfCardTypeOnField = "tp_if_card_type_on_field";
    public const string YieldPerOtherDB = "dv_per_other_db";
    public const string YieldIfCardOnField = "dv_if_card_on_field";
    public const string AVBonus = "av_bonus";
}

public static class PlatformEffectTypes
{
    public const string TPBonus = "tp_bonus";
    public const string YieldBonus = "yield_bonus";
    public const string AVBonus = "av_bonus";
}

public static class AttachmentEffectTypes
{
    public const string StatBonus = "stat_bonus";
}

public class PassiveEffect
{
    public string Type { get; set; } = "";
    public PassiveEffectConfig Params { get; set; } = new();
}

public class PlatformEffect
{
    public string Type { get; set; } = "";
    public PlatformEffectConfig Params { get; set; } = new();
}

public class AttachmentEffect
{
    public string Type { get; set; } = "";
    public AttachmentEffectConfig Params { get; set; } = new();
}

public class PassiveEffectConfig
{
    public string? Faction { get; set; }
    public List<string>? CardTypes { get; set; }
    public long BonusPerCard { get; set; }
    public long FlatBonus { get; set; }
    public List<string>? MultiModelCardIDs { get; set; }
    public List<string>? SpecificCardIDs { get; set; }
    public string? Zone { get; set; }
    public bool ExcludeSelf { get; set; }
}

public class PlatformEffectConfig
{
    public string? TargetFaction { get; set; }
    public List<string>? TargetCardTypes { get; set; }
    public long Bonus { get; set; }
    public long Reduction { get; set; }
    public bool ApplyToSelf { get; set; }
}

public class AttachmentEffectConfig
{
    public string StatType { get; set; } = "";
    public long Bonus { get; set; }
}

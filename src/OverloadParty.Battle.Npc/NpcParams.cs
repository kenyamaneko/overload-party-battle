using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

public class FactionParams
{
    public string InstanceFamily { get; init; } = "M";
}

public static class NpcParams
{
    public static readonly Dictionary<string, FactionParams> FactionParamsTable = new()
    {
        [GameConstants.FactionSHE] = new FactionParams { InstanceFamily = "M" },
        [GameConstants.FactionTenki] = new FactionParams { InstanceFamily = "R" },
        [GameConstants.FactionSugar] = new FactionParams { InstanceFamily = "C" },
        [GameConstants.FactionTuners] = new FactionParams { InstanceFamily = "M" },
    };

    // Budget threshold below which BudgetGain priority is boosted.
    public const long LowBudgetThreshold = 1500;

    // Effect evaluation priorities
    public const int PriBudgetGainHigh = 90;
    public const int PriBudgetGainLow = 40;
    public const int PriBudgetPenalty = 60;
    public const int PriInsightGain = 50;
    public const int PriInsightAbsorb = 65;
    public const int PriDrawHigh = 80;
    public const int PriDrawLow = 30;
    public const int PriSearchHigh = 70;
    public const int PriSearchLow = 25;
    public const int PriAoEDamage = 75;
    public const int PriSingleDamage = 60;
    public const int PriDebuff = 55;
    public const int PriBuff = 45;
    public const int PriHeal = 50;
    public const int PriDeployFree = 75;
    public const int PriRecoverCard = 35;
    public const int PriRevealReactive = 35;
    public const int PriDestroyPlatform = 70;
}

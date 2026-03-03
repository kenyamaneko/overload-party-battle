namespace OverloadParty.Battle.Models;

public class Player
{
    public string PlayerID { get; set; } = "";
    public string FirebaseUID { get; set; } = "";
    public string Username { get; set; } = "";
    public long Level { get; set; }
    public long Exp { get; set; }
    public long Wins { get; set; }
    public long Losses { get; set; }
    public bool IsPremium { get; set; }
    public long? EquippedIconNo { get; set; }
    public string? SelectedFaction { get; set; }
    public DateTime? PremiumExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PlayerDailyBattle
{
    public string PlayerID { get; set; } = "";
    public long DailyBattleCount { get; set; }
    public DateOnly LastResetDate { get; set; }
}

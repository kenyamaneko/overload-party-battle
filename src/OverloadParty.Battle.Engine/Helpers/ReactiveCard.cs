using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// リアクティブの発動後のライフサイクル操作。
/// </summary>
public static class ReactiveCard
{
    /// <summary>
    /// 発動したリアクティブを表向きにして所有者のトラッシュへ送ります。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="reactive">発動したリアクティブ。</param>
    /// <param name="ownerNum">リアクティブを所有するプレイヤー番号。</param>
    public static void Consume(BattleGameState state, DeployedSupport reactive, long ownerNum)
    {
        reactive.FaceUp = true;
        FieldHelpers.RemoveSupportFromField(state.GetField(ownerNum), reactive.InstanceID);
        CardMoveHelpers.AddToTrash(state, ownerNum, reactive.CardID, reactive.InstanceID, reactive.ArtNo);
    }
}

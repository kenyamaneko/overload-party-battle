using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// リアクティブカード（使い切りトリガー）のライフサイクル操作。
/// </summary>
public static class ReactiveCard
{
    /// <summary>
    /// 発火した使い切りトリガーを表向きにして所有者のトラッシュへ送ります。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="candidate">発火した使い切りトリガーの候補。</param>
    public static void Consume(BattleGameState state, EventTriggerCandidate candidate)
    {
        if (candidate.Support is { } support)
        {
            support.FaceUp = true;
            FieldHelpers.RemoveSupportFromField(state.GetField(candidate.OwnerNum), support.InstanceID);
            CardMoveHelpers.AddToTrash(state, candidate.OwnerNum, support.CardID, support.InstanceID, support.ArtNo);
            return;
        }

        if (candidate.Resource is { } resource)
        {
            resource.FaceUp = true;
            FieldHelpers.RemoveResourceFromField(state.GetField(candidate.OwnerNum), resource.InstanceID);
            CardMoveHelpers.AddToTrash(state, candidate.OwnerNum, resource.CardID, resource.InstanceID, resource.ArtNo);
        }
    }
}

using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// リアクティブの発動後のライフサイクル操作。
/// </summary>
public static class ReactiveCard
{
    /// <summary>
    /// 発動したリアクティブを表向きにして所有者のトラッシュへ送り、盤面変化に伴う
    /// パッシブ効果を再計算します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="reactive">発動したリアクティブ。</param>
    /// <param name="ownerNum">リアクティブを所有するプレイヤー番号。</param>
    public static void Consume(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects,
        DeployedSupport reactive, long ownerNum)
    {
        reactive.FaceUp = true;
        FieldHelpers.RemoveSupportFromField(state.GetField(ownerNum), reactive.InstanceID);
        CardMoveHelpers.AddToTrash(state, ownerNum, reactive.CardID, reactive.InstanceID, reactive.ArtNo);
        PassiveRecalculator.Recalculate(state, game, cc, effects);
    }
}

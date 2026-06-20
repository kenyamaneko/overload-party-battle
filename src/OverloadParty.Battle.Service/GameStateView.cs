using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Service;

/// <summary>
/// Builds info-hidden game state for a specific player.
/// Maps engine-internal types (Models) to API-contract types (BattleGameState).
/// </summary>
public static class GameStateView
{
    /// <summary>指定プレイヤー視点の情報秘匿済み ClientGameState を組み立てる。</summary>
    /// <param name="state">エンジン内部のゲーム状態。</param>
    /// <param name="game">対象 Game。</param>
    /// <param name="playerNum">視点となるプレイヤー番号。</param>
    /// <param name="cc">カード定義の参照元キャッシュ。</param>
    /// <param name="effects">効果レジストリ。null の場合は操作可能アクションを計算しない。</param>
    /// <param name="initiatives">施策カタログ。null の場合 use_initiative アクションは列挙しない。</param>
    /// <returns>視点プレイヤー向けの ClientGameState。</returns>
    public static GD.ClientGameState Build(
        BattleGameState state, Game game, long playerNum,
        ICardCache cc, IEffectRegistry effects, IInitiativeCatalog? initiatives = null)
    {
        var oppNum = state.OpponentOf(playerNum);

        // 自分のビュー（全情報）
        var myField = state.GetField(playerNum);
        var myHand = state.GetHand(playerNum);
        var myRepo = state.GetRepository(playerNum);
        var myTrash = state.GetTrash(playerNum);
        var budget = state.GetBudget(playerNum);
        var insightPool = state.GetInsightPool(playerNum);

        // 相手のデータ
        var oppField = state.GetField(oppNum);
        var oppHand = state.GetHand(oppNum);
        var oppRepo = state.GetRepository(oppNum);
        var oppTrash = state.GetTrash(oppNum);

        // PlayerView 構築前に実行可能アクションを算出（init-only のため先に計算）
        // pending reactive choice 中は ChooserPlayerNum に解決アクションを提示する。
        // 通常は ActivePlayer のみがアクション可。
        System.Collections.Generic.List<GD.AvailableAction>? availableActions = null;
        bool isChooser = state.PendingEffectChoice is { } pc && pc.ChooserPlayerNum == playerNum;
        bool isActivePlayer = state.ActivePlayer == playerNum && state.PendingEffectChoice is null;
        if (game.Status == GameStatus.Playing && (isActivePlayer || isChooser))
        {
            availableActions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, myHand, budget, insightPool, cc, effects, initiatives)
                .Select(MapAvailableAction)
                .ToList();
        }

        var myView = new GD.PlayerView
        {
            PlayerNum = playerNum,
            Budget = budget,
            InsightPool = insightPool,
            TimeBank = state.GetTimeBank(playerNum),
            Field = MapField(myField),
            Hand = myHand.Select(MapUndeployedCard).ToList(),
            RepoCount = myRepo.Count,
            TrashCount = myTrash.Count,
            Trash = myTrash.Select(MapUndeployedCard).ToList(),
            AvailableActions = availableActions,
            PendingSlotSelect = MapPendingSlotSelect(state, playerNum),
        };

        var oppView = new GD.OpponentView
        {
            PlayerNum = oppNum,
            Budget = state.GetBudget(oppNum),
            InsightPool = state.GetInsightPool(oppNum),
            TimeBank = state.GetTimeBank(oppNum),
            Field = BuildOpponentField(oppField, playerNum),
            HandCount = oppHand.Count,
            RepoCount = oppRepo.Count,
            TrashCount = oppTrash.Count,
            Trash = oppTrash.Select(MapUndeployedCard).ToList(),
        };

        return new GD.ClientGameState
        {
            GameID = game.GameID,
            CurrentTurn = state.CurrentTurn,
            CurrentPhase = state.CurrentPhase.ToWireString(),
            ActivePlayer = state.ActivePlayer,
            IsMyTurn = state.ActivePlayer == playerNum,
            TurnStartedAt = state.TurnStartedAt,
            MyView = myView,
            OppView = oppView,
            PendingEffectChoice = MapPendingEffectChoice(state.PendingEffectChoice),
        };
    }

    private static GD.PendingSlotSelectView? MapPendingSlotSelect(BattleGameState state, long playerNum)
    {
        var pending = state.PendingSlotSelects.FirstOrDefault(p => p.PlayerNum == playerNum);
        if (pending is null) { return null; }
        return new GD.PendingSlotSelectView
        {
            Resource = MapResource(pending.Resource),
            ValidZones = pending.ValidZones.ToList(),
        };
    }

    private static GD.PendingEffectChoiceView? MapPendingEffectChoice(PendingEffectChoice? pending)
    {
        if (pending is null) { return null; }
        return new GD.PendingEffectChoiceView
        {
            ChooserPlayerNum = pending.ChooserPlayerNum,
            EffectCardId = pending.EffectCardId,
            EffectInstanceId = pending.EffectInstanceId,
            ChoiceKind = pending.ChoiceKind,
        };
    }

    // ─── Mapping helpers (Models → GameData) ────────────────

    private static GD.Field MapField(Field field)
    {
        // Zone<T>.IEnumerable は null スロットを除外するため、ToArray() で
        // 元の固定容量配列を取り出してから Select する。
        return new GD.Field
        {
            Frontend = field.Frontend.ToArray().Select(r => r is null ? null : MapResource(r)).ToList(),
            Backend = field.Backend.ToArray().Select(r => r is null ? null : MapResource(r)).ToList(),
            Support = field.Support.ToArray().Select(s => s is null ? null : MapSupport(s)).ToList(),
        };
    }

    private static GD.DeployedResource MapResource(DeployedResource r)
    {
        return new GD.DeployedResource
        {
            InstanceID = r.InstanceID,
            CardID = r.CardID,
            ArtNo = r.ArtNo,
            Rank = r.Rank?.ToWireString(),
            InstanceFamily = r.InstanceFamily?.ToWireString(),
            FaceUp = r.FaceUp,
            DeployingTurnsLeft = r.DeployingTurnsLeft,
            CurrentAV = r.CurrentAV,
            MaxAV = r.MaxAV,
            CurrentTP = r.CurrentTP,
            MaxTP = r.MaxTP,
            CurrentYield = r.CurrentYield,
            MaxYield = r.MaxYield,
            Damage = r.Damage,
            TemporaryEffects = r.TemporaryEffects.Select(MapTemporaryEffect).ToList(),
            MonetizedAmount = r.MonetizedAmount,
            HasAttacked = r.HasAttacked,
            EffectUsedThisTurn = r.EffectUsedThisTurn,
            EffectUsedThisGame = r.EffectUsedThisGame,
            DeployedOnTurn = r.DeployedOnTurn,
            DeployOrder = r.DeployOrder,
            ElasticBonus = r.ElasticBonus,
            LastAttackTurn = r.LastAttackTurn,
        };
    }

    private static GD.DeployedSupport MapSupport(DeployedSupport s)
    {
        return new GD.DeployedSupport
        {
            InstanceID = s.InstanceID,
            CardID = s.CardID,
            ArtNo = s.ArtNo,
            FaceUp = s.FaceUp,
            DeployingTurnsLeft = s.DeployingTurnsLeft,
            DeployOrder = s.DeployOrder,
            EffectUsedThisTurn = s.EffectUsedThisTurn,
            EffectUsedThisGame = s.EffectUsedThisGame,
            TargetInstanceID = s.TargetInstanceID,
        };
    }

    private static GD.UndeployedCard MapUndeployedCard(UndeployedCard c)
    {
        return new GD.UndeployedCard
        {
            InstanceID = c.InstanceID,
            CardID = c.CardID,
            ArtNo = c.ArtNo,
        };
    }

    private static GD.TemporaryEffect MapTemporaryEffect(TemporaryEffect e)
    {
        return new GD.TemporaryEffect
        {
            EffectType = e.EffectType,
            Value = e.Value,
            Duration = e.Duration,
            SourceID = e.SourceID,
            Mode = e.Mode,
        };
    }

    private static GD.AvailableAction MapAvailableAction(AvailableAction a)
    {
        return new GD.AvailableAction
        {
            Type = a.Type,
            HandInstanceID = a.HandInstanceID,
            CardID = a.CardID,
            ValidZones = a.ValidZones,
            ValidTargets = a.ValidTargets,
            ChoiceOptions = a.ChoiceOptions,
            SourceInstanceID = a.SourceInstanceID,
            TargetRank = a.TargetRank,
            InstanceFamily = a.InstanceFamily,
            NeedsFamily = a.IsFamilyRequired,
            RemainingCapacity = a.RemainingCapacity,
            EffectTargetType = a.EffectTargetType,
            RequiredCount = a.RequiredCount,
            Kind = a.Kind,
            Cost = a.Cost,
        };
    }

    // ─── Opponent field with info hiding ────────────────────

    private static GD.OpponentField BuildOpponentField(Field field, long viewerPlayerNum)
    {
        // Zone<T>.IEnumerable は null スロットを除外するため、ToArray() で
        // 固定容量配列を取り出してから Select する。
        return new GD.OpponentField
        {
            Frontend = field.Frontend.ToArray().Select(HideResourceIfFaceDown).ToList(),
            Backend = field.Backend.ToArray().Select(HideResourceIfFaceDown).ToList(),
            Support = field.Support.ToArray().Select(sup =>
            {
                if (sup is null) { return null; }

                bool peeked = !sup.FaceUp && sup.PeekedBy.Contains(viewerPlayerNum);
                return new GD.HiddenDeployedSupport
                {
                    InstanceID = sup.InstanceID,
                    FaceDown = !sup.FaceUp,
                    CardID = sup.FaceUp || peeked ? sup.CardID : null,
                    ArtNo = sup.FaceUp || peeked ? sup.ArtNo : 0,
                    Peeked = peeked,
                };
            }).ToList(),
        };
    }

    private static GD.DeployedResource? HideResourceIfFaceDown(DeployedResource? res)
    {
        if (res is null) { return null; }
        if (res.FaceUp) { return MapResource(res); }

        // Hide all stats for face-down (still deploying) resources.
        // CardID は required スキーマだが face-down では公開しないため空文字を入れる。
        return new GD.DeployedResource
        {
            InstanceID = res.InstanceID,
            CardID = "",
            FaceUp = false,
            DeployingTurnsLeft = res.DeployingTurnsLeft,
        };
    }
}

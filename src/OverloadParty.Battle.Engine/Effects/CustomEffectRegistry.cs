using System.Text.Json;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Named custom effect implementations that cannot be expressed with standard ops.
/// Builds Action&lt;OpContext&gt; closures from a custom name and optional YAML meta parameters.
/// </summary>
public class CustomEffectRegistry
{
    /// <summary>cancel_nth_deploy が無効化する、そのターンの何体目のデプロイか。</summary>
    private const int CancelNthDeployTargetCount = 3;

    private readonly Dictionary<string, Func<string, Dictionary<string, JsonElement>?, Action<OpContext>>> _factories = new()
    {
        // Phase 2-2: 既存カスタム（EffectInit から移行）
        [CustomEffects.ChainAttackBonus] = RequireMeta("damage", element => element.GetInt64(), ChainAttackBonus),
        [CustomEffects.DeploySameTypeFromHand] = (_, _) => DeploySameTypeFromHand,
        [CustomEffects.DisableHighTpDeploy] = (_, _) => DisableHighTpDeploy,
        [CustomEffects.CancelNthDeploy] = (_, _) => CancelNthDeploy,
        [CustomEffects.RedirectAttack] = (_, _) => RedirectAttack,

        // Phase 2-3: 新規カスタム
        [CustomEffects.CloudShift] = BuildCloudShift,
        [CustomEffects.SpotExpiry] = (_, meta) => BuildSpotExpiry(meta),
        [CustomEffects.Reattach] = (_, _) => Reattach,
        [CustomEffects.ScaleToZero] = (_, _) => ScaleToZero,
        [CustomEffects.KeepOneFromDeckTop] = RequireMeta("peek", element => element.GetInt32(), KeepOneFromDeckTop),

        // target_shield はカード定義を passive に検査する marker（FieldHelpers.IsTargetShielded）。
        // deploy trigger では副作用なしだが、登録しておかないと loader が unknown custom として throw する。
        [CustomEffects.TargetShield] = (_, _) => NoOp,
    };

    private static void NoOp(OpContext _) { }

    /// <summary>リソースを置くスロットを要求するカスタム効果の名前。</summary>
    private static readonly HashSet<string> PlacementSlotRequiringEffects =
    [
        CustomEffects.DeploySameTypeFromHand,
        CustomEffects.CloudShift,
    ];

    /// <summary>
    /// カスタム効果がリソースを置くスロットを要求するかを返します。
    /// </summary>
    /// <param name="customName">カスタム効果名。</param>
    /// <returns>配置スロットを要求するなら true。</returns>
    public static bool RequiresPlacementSlot(string customName) =>
        PlacementSlotRequiringEffects.Contains(customName);

    /// <summary>
    /// meta から必須パラメータ 1 件を読み出し、それを束縛した効果関数を返すファクトリを組む。
    /// </summary>
    /// <typeparam name="T">効果が受け取るパラメータの型。</typeparam>
    /// <param name="key">meta から読むキー。欠けている場合は読み込みを失敗させる。</param>
    /// <param name="read">JsonElement を効果が要する型へ変換する関数。</param>
    /// <param name="effect">読み出した値を適用する効果。</param>
    /// <returns>カスタム効果名と meta を受け取り効果関数を返すファクトリ。</returns>
    private static Func<string, Dictionary<string, JsonElement>?, Action<OpContext>> RequireMeta<T>(
        string key, Func<JsonElement, T> read, Action<OpContext, T> effect) =>
        (customName, meta) => meta is not null && meta.TryGetValue(key, out var element)
            ? octx => effect(octx, read(element))
            : throw new InvalidOperationException(
                $"Custom effect {customName} requires meta key: {key}");

    /// <summary>
    /// Builds a custom effect function for the given name and meta parameters.
    /// Returns null if the custom name is not registered.
    /// </summary>
    /// <param name="customName">登録済みのカスタム効果名。</param>
    /// <param name="meta">カスタム効果の追加パラメータ。</param>
    /// <returns>構築したカスタム効果関数。登録名が未登録の場合は null。</returns>
    public Action<OpContext>? Build(string customName, Dictionary<string, JsonElement>? meta)
    {
        if (!_factories.TryGetValue(customName, out var factory))
        {
            return null;
        }
        return factory(customName, meta);
    }

    // ================================================================
    // Phase 2-2: 既存カスタム
    // ================================================================

    /// <summary>
    /// 他の自分の しゅがーらぼ Compute系リソースが自分のフロントエンドに居る場合、対象に追加ダメージを与える。
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    /// <param name="damage">カード定義由来の追加ダメージ量。</param>
    private static void ChainAttackBonus(OpContext octx, long damage)
    {
        if (octx.Target is null)
        {
            return;
        }

        var ally = octx.MyField.Frontend
            .Where(r => r.InstanceID != octx.Source?.InstanceID)
            .FirstOrDefault(r =>
            {
                var card = octx.CardCache.MustGet(r.CardID);
                return card.Faction == Factions.Sugar && card.IsComputeType;
            });
        if (ally is not null)
        {
            DamageApplication.Apply(octx, octx.Target, damage);
        }
    }

    /// <summary>SuspendForChoice / ChoiceData で deck_top 選択値を受け渡す key。</summary>
    private const string DeckTopChoiceKey = "deckTop";

    /// <summary>
    /// デッキ上端 peek 枚を見て、選択された 1 枚を手札・残りをトラッシュへ移す。選択値が未指定なら選択待ちに遷移する。
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    /// <param name="peek">見るデッキ上端の枚数。</param>
    private static void KeepOneFromDeckTop(OpContext octx, int peek)
    {
        var deck = octx.State.GetRepository(octx.PlayerNum);
        int targetCount = Math.Min(peek, deck.Count);
        // デッキが空なら覗くカードが無い。サーチ効果の失敗 (対象なし) と同じく、コストは既に払われ効果は何もしない。
        if (targetCount == 0)
        {
            return;
        }

        var topCards = deck.Take(targetCount).ToList();
        string? chosen = octx.ChoiceData?.GetValueOrDefault(DeckTopChoiceKey)?.ToString();
        if (chosen is null)
        {
            // 候補が 1 枚なら選択の余地がないため、そのまま手札へ移す。
            if (targetCount == 1)
            {
                var only = topCards[0];
                deck.Remove(only);
                octx.State.GetHand(octx.PlayerNum).Add(only);
                return;
            }
            var candidates = topCards.Select(card => card.InstanceID).ToList();
            octx.SuspendForChoice(DeckTopChoiceKey, ChoiceKinds.DeckTop, candidates, octx.PlayerNum);
            return;
        }

        var kept = topCards.First(card => card.InstanceID == chosen);
        foreach (var card in topCards)
        {
            deck.Remove(card);
            if (ReferenceEquals(card, kept))
            {
                octx.State.GetHand(octx.PlayerNum).Add(card);
            }
            else
            {
                octx.State.GetTrash(octx.PlayerNum).Add(card);
            }
        }
    }

    /// <summary>
    /// Validate choice card type matches destroyed target's type, request slot selection for deploy.
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    public static void DeploySameTypeFromHand(OpContext octx)
    {
        if (octx.Target is null)
        {
            throw new GameRuleException("No target");
        }

        var targetCard = octx.CardCache.Get(octx.Target.CardID);
        if (targetCard is null)
        {
            throw new GameRuleException("Target card not found");
        }

        string? choiceCardId = octx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();
        if (choiceCardId is null)
        {
            // reactive 経路のみ選択待ちに遷移する。
            if (octx.SupSource is null)
            {
                throw new GameRuleException("No card chosen");
            }
            var candidates = octx.State.GetHand(octx.PlayerNum)
                .Where(c =>
                {
                    var card = octx.CardCache.Get(c.CardID);
                    return card is not null
                        && card.CardType == targetCard.CardType
                        && card.Subtype == targetCard.Subtype;
                })
                .Select(c => c.CardID)
                .Distinct()
                .ToList();
            if (candidates.Count == 0)
            {
                throw new GameRuleException("No matching card in hand");
            }
            octx.SuspendForChoice("cardId", ChoiceKinds.HandCard, candidates, octx.PlayerNum);
            return;
        }

        var choiceCard = octx.CardCache.Get(choiceCardId);
        if (choiceCard is null)
        {
            throw new GameRuleException("Card not found");
        }
        // 旧 CardType レベル (個別 subtype) での同一性チェックを新スキーマで保つため、
        // CardType (category) と Subtype 両方の一致を要求する。
        // 例: Container → Container のみ可 (Orchestrator 等への置換は不可)。
        if (choiceCard.CardType != targetCard.CardType || choiceCard.Subtype != targetCard.Subtype)
        {
            throw new GameRuleException("Must deploy same type as destroyed card");
        }

        SlotRequestHelpers.DeployFromHand(octx, choiceCardId);
    }

    /// <summary>
    /// When opponent deploys a Compute/AI_ML card with TP >= 900, apply dormant.
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    public static void DisableHighTpDeploy(OpContext octx)
    {
        var target = octx.Target;
        if (target is null)
        {
            return;
        }

        var targetCard = octx.CardCache.MustGet(target.CardID);
        if (!targetCard.IsComputeType)
        {
            return;
        }
        if (target.MaxTP is null || target.MaxTP < 900)
        {
            return;
        }

        target.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = BuffTypes.Dormant,
            Value = 1,
            Duration = EffectDurations.ThisTurn,
            SourceID = "rate_limiter",
        });

        long deployerNum = octx.State.OpponentOf(octx.PlayerNum);
        var field = octx.GetField(deployerNum);
        var found = FieldHelpers.FindResourceByID(field, target.InstanceID);
        if (found is not null)
        {
            found.TemporaryEffects = target.TemporaryEffects;
        }
    }

    /// <summary>
    /// When opponent deploys their 3rd resource in a turn, cancel (destroy) it.
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    public static void CancelNthDeploy(OpContext octx)
    {
        long deployerNum = octx.State.OpponentOf(octx.PlayerNum);
        var field = octx.GetField(deployerNum);
        int count = FieldHelpers.AllResources(field).Count(r => r.DeployedOnTurn == octx.State.CurrentTurn);
        if (count != CancelNthDeployTargetCount)
        {
            octx.AbortAsConditionUnmet();
            return;
        }

        octx.CancelAction();
    }

    /// <summary>
    /// 攻撃側のフロントエンドから選ばせたリソースへ、攻撃したリソースのスループット分のダメージを移す。
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    public static void RedirectAttack(OpContext octx)
    {
        var instanceId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (instanceId is null)
        {
            // 攻撃側 (= EventOwnerNum) が再ダメージ先を選ぶ。リアクティブの所有者ではない。
            if (octx.SupSource is null || octx.EventOwnerNum is not { } chooserNum)
            {
                throw new GameRuleException("No redirect target chosen");
            }
            var candidates = octx.OpponentField.Frontend
                .ToArray()
                .Where(r => r is not null)
                .Select(r => r!.InstanceID)
                .ToList();
            if (candidates.Count == 0)
            {
                throw new GameRuleException("No frontend resource to redirect to");
            }
            octx.SuspendForChoice("instanceId", ChoiceKinds.FieldTarget, candidates, chooserNum);
            return;
        }

        var oppField = octx.OpponentField;
        if (FieldHelpers.FindResourceZone(oppField, instanceId) != Zone.Frontend)
        {
            throw new GameRuleException("Redirect target must be opponent's frontend");
        }

        var target = FieldHelpers.FindResourceByID(oppField, instanceId)
            ?? throw new GameRuleException($"Redirect target {instanceId} not found");
        long damage = octx.EventDamage
            ?? throw new GameRuleException("Redirect requires the declared attack damage");

        DamageApplication.Apply(octx, target, damage);

        foreach (var evt in DestructionSweep.Run(octx.State, octx.Game, octx.CardCache, octx.Effects))
        {
            octx.AddEvent(evt);
        }
    }

    // ================================================================
    // Phase 2-3: 新規カスタム
    // ================================================================

    /// <summary>
    /// Deploy a resource from hand matching faction/card_type filter, then self-destruct the source.
    /// meta: { faction, card_type, deploy_discount }
    /// </summary>
    private static Action<OpContext> BuildCloudShift(string customName, Dictionary<string, JsonElement>? meta)
    {
        if (meta is null)
        {
            throw new InvalidOperationException($"Custom effect {customName} requires meta");
        }

        string? faction = meta.GetStringOrNull("faction");
        var cardTypes = meta.GetStringListOrNull("card_type");
        long discount = meta.GetInt64Or("deploy_discount", 0);

        return octx =>
        {
            string? choiceCardId = octx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();
            if (choiceCardId is null)
            {
                throw new GameRuleException("No card chosen for cloud_shift");
            }

            var card = octx.CardCache.Get(choiceCardId);
            if (card is null)
            {
                throw new GameRuleException($"Card {choiceCardId} not found");
            }
            if (faction is not null && card.Faction != faction)
            {
                throw new GameRuleException($"Card must be {faction} faction");
            }
            if (cardTypes is { Count: > 0 } && !EffectHelpers.MatchesAnyCardType(card, cardTypes))
            {
                throw new GameRuleException($"Card type {card.CardType} not allowed");
            }

            SlotRequestHelpers.DeployFromHand(octx, choiceCardId);

            // 移設先のスロットがなく移設が成立しなかったので、割引も発動元の除去も行わず不発で終える。
            if (octx.Result.HasGuardFailed) { return; }

            if (discount > 0)
            {
                long budget = octx.State.GetBudget(octx.PlayerNum);
                octx.State.SetBudget(octx.PlayerNum, budget + discount);
            }

            // 効果のコストとして発動元をトラッシュへ送る。破壊ではないため SLA ペナルティは伴わない。
            if (octx.SupSource is not null)
            {
                FieldHelpers.DestroySupport(
                    octx.State, octx.Game, octx.PlayerNum, octx.MyField, octx.SupSource.InstanceID,
                    octx.CardCache, octx.Effects);
            }
            else if (octx.Source is not null)
            {
                ResourceHelpers.MoveResourceToTrash(
                    octx.State, octx.PlayerNum, octx.MyField, octx.Source);
                PassiveRecalculator.Recalculate(octx.State, octx.Game, octx.CardCache, octx.Effects);
            }
        };
    }

    /// <summary>
    /// Self-destruct after N turns since deploy.
    /// meta: { turns }
    /// </summary>
    private static Action<OpContext> BuildSpotExpiry(Dictionary<string, JsonElement>? meta)
    {
        int expiryTurns = meta?.GetInt32Or("turns", 2) ?? 2;

        return octx =>
        {
            if (octx.Source is null)
            {
                return;
            }

            long deployedOn = octx.Source.DeployedOnTurn;
            long currentTurn = octx.State.CurrentTurn;

            if (deployedOn > 0 && currentTurn - deployedOn >= expiryTurns)
            {
                foreach (var evt in DestructionSweep.DestroyNow(
                    octx.State, octx.Game, octx.PlayerNum, octx.Source, octx.CardCache, octx.Effects))
                {
                    octx.AddEvent(evt);
                }
            }
        };
    }

    /// <summary>
    /// Move this attachment to a different valid target resource.
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    public static void Reattach(OpContext octx)
    {
        var newTargetId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (newTargetId is null)
        {
            throw new GameRuleException("No target chosen for reattach");
        }

        if (octx.SupSource is null)
        {
            throw new GameRuleException("Source is not a support card");
        }

        var field = octx.MyField;
        var newTarget = FieldHelpers.FindResourceByID(field, newTargetId);
        if (newTarget is null)
        {
            throw new GameRuleException("Target resource not found");
        }

        string attachmentId = octx.SupSource.InstanceID;

        var attachment = field.Support.FirstOrDefault(a => a.InstanceID == attachmentId);
        if (attachment is null)
        {
            throw new GameRuleException("Attachment not found in attachment zone");
        }

        attachment.TargetInstanceID = newTarget.InstanceID;
    }

    /// <summary>
    /// Scale to Zero: if the source did not attack last turn and was not deployed this turn,
    /// set its maintenance cost to 0 for this turn.
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    public static void ScaleToZero(OpContext octx)
    {
        if (octx.Source is null)
        {
            return;
        }

        if (octx.Source.DeployedOnTurn == octx.State.CurrentTurn)
        {
            throw new GameRuleException("Cannot use on deploy turn");
        }

        if (octx.Source.LastAttackTurn >= octx.State.CurrentTurn - 1)
        {
            throw new GameRuleException("Source attacked last turn");
        }

        // 徴収される維持コストと同額の軽減を付与して、そのターンの徴収を 0 にする。
        var card = octx.CardCache.MustGet(octx.Source.CardID);
        long maintenanceCost = StatCalculator.CalculateBaseMaintenanceCost(octx.Source, card);

        if (maintenanceCost <= 0) { return; }

        octx.Source.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = BuffTypes.MaintenanceReduction,
            Value = maintenanceCost,
            Duration = EffectDurations.ThisTurn,
            SourceID = "scale_to_zero",
        });
    }
}

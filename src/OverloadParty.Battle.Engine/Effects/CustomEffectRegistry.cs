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
    private readonly Dictionary<string, Func<Dictionary<string, JsonElement>?, Action<OpContext>?>> _factories = new()
    {
        // Phase 2-2: 既存カスタム（EffectInit から移行）
        [CustomEffects.ChainAttackBonus] = BuildChainAttackBonus,
        [CustomEffects.DeploySameTypeFromHand] = _ => DeploySameTypeFromHand,
        [CustomEffects.DisableHighTpDeploy] = _ => DisableHighTpDeploy,
        [CustomEffects.CancelNthDeploy] = _ => CancelNthDeploy,
        [CustomEffects.RedirectAttack] = _ => RedirectAttack,

        // Phase 2-3: 新規カスタム
        [CustomEffects.CloudShift] = BuildCloudShift,
        [CustomEffects.SpotExpiry] = BuildSpotExpiry,
        [CustomEffects.Reattach] = _ => Reattach,
        [CustomEffects.ScaleToZero] = _ => ScaleToZero,
        [CustomEffects.DeckTopKeepOne] = BuildDeckTopKeepOne,

        // target_shield はカード定義を passive に検査する marker（FieldHelpers.IsTargetShielded）。
        // deploy trigger では副作用なしだが、登録しておかないと loader が unknown custom として throw する。
        [CustomEffects.TargetShield] = _ => NoOp,
    };

    private static void NoOp(OpContext _) { }

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
        return factory(meta);
    }

    // ================================================================
    // Phase 2-2: 既存カスタム
    // ================================================================

    /// <summary>
    /// 他の自分の しゅがーらぼ Compute系リソースが自分のフロントエンドに居る場合、対象に追加ダメージを与える。
    /// meta: { damage }
    /// </summary>
    /// <param name="meta">カード定義由来の追加ダメージ量。</param>
    /// <returns>構築した効果関数。meta に damage が欠ける場合は null。</returns>
    private static Action<OpContext>? BuildChainAttackBonus(Dictionary<string, JsonElement>? meta)
    {
        if (meta is null || !meta.TryGetValue("damage", out var damageEl))
        {
            return null;
        }
        long damage = damageEl.GetInt64();

        return octx =>
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
        };
    }

    /// <summary>
    /// デッキの上から count 枚のうち、プレイヤーが選んだ 1 枚を手札に残し、残りをトラッシュに送る。
    /// meta: { count }
    /// </summary>
    /// <param name="meta">対象とするデッキ上端の枚数 count を含むカード定義由来のパラメータ。</param>
    /// <returns>構築した効果関数。meta に count が欠ける場合は null。</returns>
    private static Action<OpContext>? BuildDeckTopKeepOne(Dictionary<string, JsonElement>? meta)
    {
        if (meta is null || !meta.TryGetValue("count", out var countEl))
        {
            return null;
        }
        int count = countEl.GetInt32();

        return octx => DeckTopKeepOne(octx, count);
    }

    /// <summary>
    /// デッキ上端 count 枚から、選択された 1 枚を手札・残りをトラッシュへ移す。選択値が未指定なら選択待ちに遷移する。
    /// </summary>
    /// <param name="octx">効果実行コンテキスト。</param>
    /// <param name="count">手札・トラッシュへ振り分ける対象とするデッキ上端の枚数。</param>
    private static void DeckTopKeepOne(OpContext octx, int count)
    {
        var deck = octx.State.GetRepository(octx.PlayerNum);
        int targetCount = Math.Min(count, deck.Count);
        if (targetCount == 0)
        {
            return;
        }

        string? chosen = octx.ChoiceData?.GetValueOrDefault("deckTop")?.ToString();
        if (chosen is null)
        {
            // 候補が 1 枚なら選択の余地がないため、そのまま手札へ移す。
            if (targetCount == 1)
            {
                var only = deck[0];
                deck.RemoveAt(0);
                octx.State.GetHand(octx.PlayerNum).Add(only);
                return;
            }
            // 候補はデッキ上端の 1 始まりの位置。実体カードは非公開なので位置で参照する。
            var positions = Enumerable.Range(1, targetCount).Select(i => i.ToString()).ToList();
            octx.SuspendForChoice("deckTop", ChoiceKinds.DeckTop, positions, octx.PlayerNum);
            return;
        }

        int keepPosition = int.Parse(chosen);
        var topCards = deck.Take(targetCount).ToList();
        var kept = topCards[keepPosition - 1];
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
        if (octx.SupSource is { EffectUsedThisTurn: true })
        {
            throw new GameRuleException("Already used this turn");
        }

        long deployerNum = octx.State.OpponentOf(octx.PlayerNum);
        var field = octx.GetField(deployerNum);
        int count = FieldHelpers.AllResources(field).Count(r => r.DeployedOnTurn == octx.State.CurrentTurn);
        if (count != 3)
        {
            throw new GameRuleException($"Not the 3rd deploy (count={count})");
        }

        if (octx.SupSource is not null)
        {
            octx.SupSource.EffectUsedThisTurn = true;
        }

        octx.CancelAction();
    }

    /// <summary>
    /// Validate redirect target is opponent's frontend.
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
    }

    // ================================================================
    // Phase 2-3: 新規カスタム
    // ================================================================

    /// <summary>
    /// Deploy a resource from hand matching faction/card_type filter, then self-destruct the source.
    /// meta: { faction, card_type, deploy_discount }
    /// </summary>
    private static Action<OpContext>? BuildCloudShift(Dictionary<string, JsonElement>? meta)
    {
        if (meta is null)
        {
            return null;
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

            if (discount > 0)
            {
                long budget = octx.State.GetBudget(octx.PlayerNum);
                octx.State.SetBudget(octx.PlayerNum, budget + discount);
            }

            SlotRequestHelpers.DeployFromHand(octx, choiceCardId);

            if (octx.SupSource is not null)
            {
                FieldHelpers.DestroySupport(octx.State, octx.PlayerNum, octx.MyField, octx.SupSource.InstanceID);
            }
        };
    }

    /// <summary>
    /// Self-destruct after N turns since deploy.
    /// meta: { turns }
    /// </summary>
    private static Action<OpContext>? BuildSpotExpiry(Dictionary<string, JsonElement>? meta)
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
                var field = octx.MyField;
                ResourceHelpers.DestroyResource(octx.State, octx.PlayerNum, field, octx.Source, octx.CardCache);
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

        // Elastic カードの維持コストを算出して同額の reduction を付与
        var card = octx.CardCache.MustGet(octx.Source.CardID);
        long intrinsic = card.IsComputeType ? card.BaseThroughput : card.BaseYield;
        long scaledStat = intrinsic * BattleConstants.GetRankMultiplier(octx.Source.Rank) + octx.Source.ElasticBonus;
        long maintenanceCost = Math.Max(0, scaledStat - card.FreeTier) * card.CostPerRequest / 100;

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

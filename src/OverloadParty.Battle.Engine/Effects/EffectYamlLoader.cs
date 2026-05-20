using System.Text.Json;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Deserializes card effect definitions from JSON (YAML schema) and registers
/// them into the EffectRegistry as composed IEffectOp pipelines.
/// </summary>
public static class EffectYamlLoader
{
    // 旧 YAML 互換: lowercase "data"/"compute" を新 category 名にマッピング。
    // 新 YAML は CardTypes.Data / CardTypes.Compute を直接使う。
    private static readonly Dictionary<string, string> LowercaseCategoryAliases = new()
    {
        ["data"] = CardTypes.Data,
        ["compute"] = CardTypes.Compute,
    };

    /// <summary>
    /// Loads effects from card definitions and registers them into the registry.
    /// Cards with a <c>custom</c> effect require a matching entry in the custom registry.
    /// </summary>
    /// <param name="cards">読み込み対象のカード定義群。</param>
    /// <param name="registry">登録先の効果レジストリ。</param>
    /// <param name="customRegistry">カスタム効果のレジストリ。</param>
    public static void LoadFromCards(
        IEnumerable<CardDefinition> cards,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        foreach (var card in cards)
        {
            if (card.Effects is not { Count: > 0 })
            {
                continue;
            }

            LoadCardEffects(card.CardId, card.Effects, registry, customRegistry);
        }
    }

    private static void LoadCardEffects(
        string cardId,
        List<EffectDef> effects,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        var byTrigger = effects.GroupBy(e => ParseTrigger(e.Trigger));

        foreach (var group in byTrigger)
        {
            var defs = group.ToList();
            var ops = BuildTriggerOps(defs, customRegistry);
            if (ops.Count > 0)
            {
                registry.RegisterComposed(cardId, group.Key, ops.ToArray());
            }
        }
    }

    // ================================================================
    // Block composition
    // ================================================================

    private static List<IEffectOp> BuildTriggerOps(
        List<EffectDef> defs,
        CustomEffectRegistry customRegistry)
    {
        var independent = defs.Where(d => d.After is null).ToList();
        var dependent = defs.Where(d => d.After is not null).ToList();

        if (independent.Count == 1 && dependent.Count == 0)
        {
            return BuildSingleBlock(independent[0], customRegistry);
        }

        if (independent.Count == 1 && dependent.Count > 0)
        {
            // Single root: guards propagate normally (no exception isolation).
            // Record success in GroupResults so DependentEffectOp can check.
            string rootId = independent[0].Id
                ?? throw new InvalidOperationException("Root block with dependents must have an id");
            var allOps = BuildSingleBlock(independent[0], customRegistry);
            allOps.Add(new MarkGroupSucceededOp(rootId));

            foreach (var dep in dependent)
            {
                var depOps = BuildSingleBlock(dep, customRegistry);
                if (depOps.Count == 0) { continue; }

                if (dep.After is null || dep.After != rootId)
                {
                    throw new InvalidOperationException(
                        $"Effect block references unknown after target '{dep.After}'");
                }
                allOps.Add(new DependentEffectOp(dep.After, depOps.ToArray()));
            }
            return allOps;
        }

        // Multiple independent blocks: wrap each for exception isolation.
        var effectGroupIds = new HashSet<string>();
        var isolatedOps = new List<IEffectOp>();
        int autoId = 0;

        foreach (var def in independent)
        {
            var ops = BuildSingleBlock(def, customRegistry);
            if (ops.Count == 0) { continue; }

            string groupId = def.Id ?? $"_auto_{autoId++}";
            isolatedOps.Add(new EffectGroupOp(groupId, ops.ToArray()));
            effectGroupIds.Add(groupId);
        }

        foreach (var def in dependent)
        {
            var ops = BuildSingleBlock(def, customRegistry);
            if (ops.Count == 0) { continue; }

            if (def.After is null)
            {
                continue;
            }
            if (!effectGroupIds.Contains(def.After))
            {
                throw new InvalidOperationException(
                    $"Effect block references unknown after target '{def.After}'");
            }
            isolatedOps.Add(new DependentEffectOp(def.After, ops.ToArray()));
        }

        return isolatedOps;
    }

    private static List<IEffectOp> BuildSingleBlock(
        EffectDef def,
        CustomEffectRegistry customRegistry)
    {
        if (def.Custom is { } customName)
        {
            return BuildCustomBlock(customName, def.Meta, customRegistry);
        }

        var ops = new List<IEffectOp>();

        if (def.Guard is { Count: > 0 })
        {
            foreach (var guardEl in def.Guard)
            {
                ops.Add(BuildGuard(guardEl));
            }
        }

        if (def.Choice is not null)
        {
            var branches = def.Choice.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(BuildOp).ToList());
            ops.Add(new BranchOnChoiceOp(branches));
        }
        else if (def.Ops is { Count: > 0 })
        {
            foreach (var opEl in def.Ops)
            {
                ops.Add(BuildOp(opEl));
            }
        }

        // UseEffectProcessor already enforces once-per-turn for ignition triggers
        if (def.UseLimit is not null && ParseTrigger(def.Trigger) != TriggerType.Ignition)
        {
            bool perGame = def.UseLimit switch
            {
                UseLimits.OncePerGame => true,
                UseLimits.OncePerTurn => false,
                _ => throw new InvalidOperationException($"Unknown use_limit: {def.UseLimit}"),
            };
            ops.Insert(0, new CheckUseLimitOp(perGame));
            ops.Add(new MarkUseLimitOp(perGame));
        }

        return ops;
    }

    private static List<IEffectOp> BuildCustomBlock(
        string customName,
        Dictionary<string, JsonElement>? meta,
        CustomEffectRegistry customRegistry)
    {
        var fn = customRegistry.Build(customName, meta)
            ?? throw new InvalidOperationException($"Unknown custom effect: {customName}");

        List<EffectCategory>? categories = null;
        EffectTargetType targetType = EffectTargetType.None;
        string? zoneHint = null;

        if (meta is not null)
        {
            if (meta.TryGetValue("categories", out var catEl))
            {
                categories = catEl.EnumerateArray()
                    .Select(c => ParseEffectCategory(c.GetString()!))
                    .ToList();
            }
            if (meta.TryGetValue("target", out var tgtEl))
            {
                targetType = ParseEffectTargetType(tgtEl.GetString()!);
            }
            if (meta.TryGetValue("zone", out var zoneEl))
            {
                zoneHint = zoneEl.GetString();
            }
        }

        return [new CustomFnTaggedOp(fn)
        {
            Categories = categories ?? [],
            Target = targetType,
            Zone = zoneHint,
        }];
    }

    // ================================================================
    // Op builder
    // ================================================================

    private static IEffectOp BuildOp(JsonElement element)
    {
        using var enumerator = element.EnumerateObject();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("Empty op object");
        }

        var prop = enumerator.Current;
        string opName = prop.Name;
        JsonElement p = prop.Value;

        return opName switch
        {
            EffectOps.GainBudget => new GainBudgetOp(
                ParsePlayerRef(p.GetProperty("target").GetString()!),
                BuildAmount(p.GetProperty("amount"))),

            EffectOps.LoseBudget => new LoseBudgetOp(
                ParsePlayerRef(p.GetProperty("target").GetString()!),
                BuildAmount(p.GetProperty("amount"))),

            EffectOps.DealDamage => BuildDealDamage(p),

            EffectOps.HealDamage => new HealDamageOp(
                BuildSelector(p.GetProperty("selector")),
                BuildAmount(p.GetProperty("amount"))),

            EffectOps.DestroyCheck => new DestroyCheckOp(
                ParsePlayerRef(p.GetProperty("target").GetString()!)),

            EffectOps.SurviveDestruction => new SurviveDestructionOp(
                p.GetProperty("av").GetInt64()),

            EffectOps.ApplyBuff => BuildApplyBuff(p),

            EffectOps.Draw => new DrawCardsOp(
                p.GetProperty("count").GetInt32()),

            EffectOps.SearchRepo => BuildSearchRepo(p),

            EffectOps.AddToHand => AddToHandOp.Instance,

            EffectOps.TrashToHand => BuildTrashToHand(p),

            EffectOps.DeployFromHand => BuildRequestSlotFromHand(p),

            EffectOps.DeployFromRepo => BuildRequestSlotFromRepo(p),

            EffectOps.DeployFromRepoSameCard => new RequestSlotFromRepoSameCardOp(
                p.TryGetProperty("override_av", out var oav) ? oav.GetInt64() : 0),

            EffectOps.DestroyPlatform => new DestroyPlatformOp(),

            EffectOps.ScaleToRank => new ScaleToRankOp(
                p.GetProperty("rank").GetString()!),

            EffectOps.CancelAction => SetCancelActionOp.Instance,

            EffectOps.RevealReactive => new RevealReactiveOp(),

            EffectOps.PeekReactive => new PeekReactiveOp(),

            EffectOps.ReduceDeployTurns => new ReduceDeployTurnsOp(
                BuildAmount(p.GetProperty("amount"))),

            EffectOps.AbsorbInsight => new AbsorbInsightOp(
                BuildAmount(p.GetProperty("amount"))),

            EffectOps.GainInsight => new GainInsightOp(
                BuildAmount(p.GetProperty("amount"))),

            _ => throw new InvalidOperationException($"Unknown op: {opName}"),
        };
    }

    private static IEffectOp BuildDealDamage(JsonElement p)
    {
        var selector = BuildSelector(p.GetProperty("selector"));
        var amount = BuildAmount(p.GetProperty("amount"));

        if (p.TryGetProperty("budget_damage", out var bdEl))
        {
            var budgetDamage = BuildAmount(bdEl);
            return new IncidentDamageOp(selector, amount, budgetDamage);
        }

        return new DealDamageOp(selector, amount);
    }

    private static IEffectOp BuildApplyBuff(JsonElement p)
    {
        var selector = BuildSelector(p.GetProperty("selector"));
        string buff = MapBuffName(p.GetProperty("buff").GetString()!);
        var amount = BuildAmount(p.GetProperty("amount"));
        string duration = p.TryGetProperty("duration", out var durEl) ? durEl.GetString()! : EffectDurations.Permanent;
        string? sourceId = p.TryGetProperty("source_id", out var sid) ? sid.GetString() : null;
        string mode = p.TryGetProperty("mode", out var modeEl) ? modeEl.GetString()! : "";

        return new ApplyBuffOp(selector, buff, amount, duration, sourceId, mode);
    }

    private static IEffectOp BuildSearchRepo(JsonElement p)
    {
        string? faction = p.TryGetProperty("faction", out var fEl) ? fEl.GetString() : null;
        return new SearchRepoOp { Faction = faction };
    }

    private static IEffectOp BuildRequestSlotFromHand(JsonElement p)
    {
        Func<CardDefinition, bool>? filter = p.TryGetProperty("filter", out var filterEl)
            ? BuildCardFilter(filterEl) : null;
        return new RequestSlotFromHandOp { Filter = filter };
    }

    private static IEffectOp BuildRequestSlotFromRepo(JsonElement p)
    {
        Func<CardDefinition, bool>? filter = p.TryGetProperty("filter", out var filterEl)
            ? BuildCardFilter(filterEl) : null;
        long overrideAV = p.TryGetProperty("override_av", out var oav) ? oav.GetInt64() : 0;
        return new RequestSlotFromRepoOp { Filter = filter, OverrideAV = overrideAV };
    }

    private static IEffectOp BuildTrashToHand(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object)
        {
            return new TrashToHandOp();
        }

        Func<CardDefinition, bool>? filter = HasFilterProperty(p) ? BuildCardFilter(p) : null;
        return new TrashToHandOp { Filter = filter };
    }

    private static bool HasFilterProperty(JsonElement p) =>
        p.TryGetProperty("faction", out _)
        || p.TryGetProperty("card_type", out _)
        || p.TryGetProperty("card_id", out _);

    // ================================================================
    // Selector builder
    // ================================================================

    private static ISelector BuildSelector(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? pick = element.TryGetProperty("pick", out var pk) ? pk.GetString() : SelectorPickModes.All;
            if (pick == SelectorPickModes.Choice)
            {
                string? owner = element.TryGetProperty("owner", out var ow) ? ow.GetString() : PlayerRefs.Myself;
                string? zone = element.TryGetProperty("zone", out var zn) ? zn.GetString() : null;
                string? faction = element.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
                var cardTypes = ParseCardTypes(element);

                return new ByChoiceSelector
                {
                    Zone = zone,
                    Faction = faction,
                    CardType = cardTypes is { Count: 1 } ? cardTypes[0] : null,
                    Owner = owner ?? PlayerRefs.Myself,
                };
            }
        }

        return BuildSelectorCore(element);
    }

    private static ISelector BuildSelectorCore(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() switch
            {
                "source" => SourceSelector.Instance,
                "target" => TargetSelector.Instance,
                _ => throw new InvalidOperationException($"Unknown selector keyword: {element.GetString()}"),
            };
        }

        string? owner = element.TryGetProperty("owner", out var ow) ? ow.GetString() : PlayerRefs.Myself;
        string? zone = element.TryGetProperty("zone", out var zn) ? zn.GetString() : null;
        string? faction = element.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
        bool excludeSource = element.TryGetProperty("exclude", out var ex) && ex.GetString() == "source";
        var cardTypes = ParseCardTypes(element);

        ISelector selector = owner switch
        {
            PlayerRefs.Myself => new AllOwnSelector { Zone = zone, Faction = faction, CardTypes = cardTypes },
            PlayerRefs.Opponent => new AllOpponentSelector { Zone = zone, Faction = faction, CardTypes = cardTypes },
            PlayerRefs.Both => new UnionSelector(
                new AllOwnSelector { Zone = zone, Faction = faction, CardTypes = cardTypes },
                new AllOpponentSelector { Zone = zone, Faction = faction, CardTypes = cardTypes }),
            _ => throw new InvalidOperationException($"Unknown selector owner: {owner}"),
        };

        if (excludeSource)
        {
            selector = new ExcludeSourceSelector(selector);
        }

        return selector;
    }

    // ================================================================
    // Amount builder
    // ================================================================

    private static IAmountResolver BuildAmount(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return new StaticAmount(element.GetInt64());
        }

        // ref-based: { ref: "source.yield", multiply: 1.0 }
        if (element.TryGetProperty("ref", out var refEl))
        {
            string refStr = refEl.GetString()!;
            var parts = refStr.Split('.');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"Invalid ref format: {refStr}");
            }

            double multiply = element.TryGetProperty("multiply", out var mulEl)
                ? mulEl.GetDouble()
                : 1.0;

            return new RefAmount(parts[0], parts[1], multiply);
        }

        // per-count: { base, per: { count: selector, value }, max? }
        if (element.TryGetProperty("per", out var perEl))
        {
            long baseVal = element.TryGetProperty("base", out var baseEl) ? baseEl.GetInt64() : 0;
            long? max = element.TryGetProperty("max", out var maxEl) ? maxEl.GetInt64() : null;

            var countSelectorEl = perEl.GetProperty("count");
            long perValue = perEl.GetProperty("value").GetInt64();

            var countSelector = BuildCountSelector(countSelectorEl);
            return new PerCountAmount(baseVal, countSelector, perValue, max);
        }

        throw new InvalidOperationException($"Unknown amount format: {element}");
    }

    /// <summary>
    /// Builds a selector for counting purposes (used in per-count amounts).
    /// Count selectors don't support pick:choice, so delegates directly to the core builder.
    /// </summary>
    private static ISelector BuildCountSelector(JsonElement element) => BuildSelectorCore(element);

    // ================================================================
    // Guard builder
    // ================================================================

    private static IEffectOp BuildGuard(JsonElement element)
    {
        // ガードの negate: true フラグ。指定時はガードの判定結果を反転する。
        // 反転は内側 op の構築と分離し、外側で NegateGuardOp を 1 回だけ被せる。
        bool negate = element.TryGetProperty("negate", out var negEl)
            && negEl.ValueKind == JsonValueKind.True;

        IEffectOp op = BuildGuardBody(element);
        return negate ? new NegateGuardOp(op) : op;
    }

    private static IEffectOp BuildGuardBody(JsonElement element)
    {
        if (element.TryGetProperty("stat", out var statEl))
        {
            return BuildStatGuard(statEl);
        }

        if (element.TryGetProperty("count", out var countEl))
        {
            return BuildCountGuard(countEl);
        }

        if (element.TryGetProperty("match", out var matchEl))
        {
            return BuildMatchGuard(matchEl);
        }

        if (element.TryGetProperty("not_same", out var notSameEl))
        {
            var (a, b) = ParseResourceRefPair(notSameEl);
            return new GuardNotSameOp(a, b);
        }

        if (element.TryGetProperty("same", out var sameEl))
        {
            var (a, b) = ParseResourceRefPair(sameEl);
            return new GuardSameOp(a, b);
        }

        if (element.TryGetProperty("event_owner", out var ownerEl))
        {
            bool isSelf = ownerEl.GetString() switch
            {
                PlayerRefs.Myself => true,
                PlayerRefs.Opponent => false,
                _ => throw new InvalidOperationException($"Unknown event_owner: {ownerEl.GetString()}"),
            };
            return new GuardEventOwnerOp(isSelf);
        }

        if (element.TryGetProperty("lethal", out var lethalEl)
            && lethalEl.ValueKind == JsonValueKind.True)
        {
            return GuardLethalOp.Instance;
        }

        throw new InvalidOperationException($"Unknown guard type: {element}");
    }

    private static (ResourceRef A, ResourceRef B) ParseResourceRefPair(JsonElement element)
    {
        var a = ParseResourceRef(element.GetProperty("a").GetString()!);
        var b = ParseResourceRef(element.GetProperty("b").GetString()!);
        return (a, b);
    }

    private static ResourceRef ParseResourceRef(string s) => s switch
    {
        "source" => ResourceRef.Source,
        "target" => ResourceRef.Target,
        "equip_host" => ResourceRef.EquipHost,
        _ => throw new InvalidOperationException($"Unknown resource reference: {s}"),
    };

    private static IEffectOp BuildStatGuard(JsonElement element)
    {
        string selectorStr = element.GetProperty("selector").GetString()!;
        string stat = element.GetProperty("stat").GetString()!;

        if (selectorStr == "myself" && stat == "budget")
        {
            if (element.TryGetProperty("min", out var minEl))
            {
                return new RequireBudgetOp(minEl.GetInt64());
            }
            if (element.TryGetProperty("max", out var maxEl))
            {
                return new RequireMaxBudgetOp(maxEl.GetInt64());
            }
        }

        if (selectorStr == "target" && stat == "av")
        {
            if (element.TryGetProperty("max", out var maxEl))
            {
                return new GuardTargetAVOp(maxEl.GetInt64());
            }
        }

        throw new InvalidOperationException($"Unsupported stat guard: selector={selectorStr}, stat={stat}");
    }

    private static IEffectOp BuildCountGuard(JsonElement element)
    {
        var selectorEl = element.GetProperty("selector");
        int min = element.TryGetProperty("min", out var minEl) ? minEl.GetInt32() : 1;
        int? max = element.TryGetProperty("max", out var maxEl) ? maxEl.GetInt32() : null;

        // NPC classifier 互換のため、faction 限定の count guard は RequireFactionCountOp に集約する。
        // 反転 (negate) は外側 BuildGuard が NegateGuardOp で被せるため、ここでは関与しない。
        if (max is null && selectorEl.ValueKind == JsonValueKind.Object)
        {
            string? owner = selectorEl.TryGetProperty("owner", out var ow) ? ow.GetString() : null;
            string? faction = selectorEl.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
            bool hasZone = selectorEl.TryGetProperty("zone", out _);
            bool hasCardType = selectorEl.TryGetProperty("card_type", out _);
            bool hasCardId = selectorEl.TryGetProperty("card_id", out _);

            if (owner == PlayerRefs.Myself && faction is not null && !hasZone && !hasCardType && !hasCardId)
            {
                return new RequireFactionCountOp(faction, min);
            }
        }

        return BuildResourceCountGuard(selectorEl, min, max);
    }

    private static IEffectOp BuildResourceCountGuard(JsonElement selectorEl, int min, int? max)
    {
        string? owner = null;
        string? zone = null;
        string? faction = null;
        List<string>? cardTypes = null;
        List<string>? cardIds = null;

        if (selectorEl.ValueKind == JsonValueKind.Object)
        {
            owner = selectorEl.TryGetProperty("owner", out var ow) ? ow.GetString() : PlayerRefs.Myself;
            zone = selectorEl.TryGetProperty("zone", out var zn) ? zn.GetString() : null;
            faction = selectorEl.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
            cardTypes = ParseCardTypes(selectorEl);
            cardIds = ParseCardIds(selectorEl);
        }

        return new ResourceCountGuardOp(
            owner ?? PlayerRefs.Myself, zone, faction, cardTypes, cardIds, min, max);
    }

    private static IEffectOp BuildMatchGuard(JsonElement element)
    {
        var selector = ParseMatchSelector(element.GetProperty("selector").GetString()!);
        string? faction = element.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
        var cardTypes = ParseCardTypes(element);
        var cardIds = ParseCardIds(element);
        bool? ownerIsOpponent = element.TryGetProperty("owner", out var ow)
            ? ow.GetString() switch
            {
                PlayerRefs.Myself => false,
                PlayerRefs.Opponent => true,
                _ => throw new InvalidOperationException($"Unknown match owner: {ow.GetString()}"),
            }
            : null;

        return new GuardMatchOp(selector, faction, cardTypes, cardIds, ownerIsOpponent);
    }

    private static MatchSelector ParseMatchSelector(string s) => s switch
    {
        "target" => MatchSelector.Target,
        "event_card" => MatchSelector.EventCard,
        "attacker" => MatchSelector.Attacker,
        _ => throw new InvalidOperationException($"Unknown match selector: {s}"),
    };

    // ================================================================
    // Card filter builder (for deploy_from_repo / deploy_from_hand)
    // ================================================================

    private static Func<CardDefinition, bool> BuildCardFilter(JsonElement filterEl)
    {
        string? faction = filterEl.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
        var cardTypes = ParseCardTypes(filterEl);
        var cardIds = ParseCardIds(filterEl);

        return card =>
        {
            if (faction is { Length: > 0 } && card.Faction != faction)
            {
                return false;
            }

            if (cardTypes is { Count: > 0 } && !EffectHelpers.MatchesAnyCardType(card, cardTypes))
            {
                return false;
            }

            if (cardIds is { Count: > 0 } && !cardIds.Contains(card.CardId))
            {
                return false;
            }

            return true;
        };
    }

    // ================================================================
    // Parsing helpers
    // ================================================================

    private static TriggerType ParseTrigger(string trigger) => trigger switch
    {
        TriggerTypes.OnDeploy => TriggerType.OnDeploy,
        TriggerTypes.Ignition => TriggerType.Ignition,
        TriggerTypes.Passive => TriggerType.OnEndPhase, // 後方互換: passive → OnEndPhase
        TriggerTypes.OnEndPhase => TriggerType.OnEndPhase,
        TriggerTypes.OnFieldChange => TriggerType.OnFieldChange,
        TriggerTypes.OnScaleUp => TriggerType.OnScaleUp,
        TriggerTypes.OnAttack => TriggerType.OnAttack,
        TriggerTypes.OnHit => TriggerType.OnHit,
        TriggerTypes.OnDestroy => TriggerType.OnDestroy,
        TriggerTypes.OnAttackDeclared => TriggerType.OnAttackDeclared,
        TriggerTypes.OnIncident => TriggerType.OnIncident,
        TriggerTypes.OnDamaged => TriggerType.OnDamaged,
        _ => throw new InvalidOperationException($"Unknown trigger: {trigger}"),
    };

    private static PlayerRef ParsePlayerRef(string s) => s switch
    {
        PlayerRefs.Myself => PlayerRef.Myself,
        PlayerRefs.Opponent => PlayerRef.Opponent,
        PlayerRefs.Both => PlayerRef.Both,
        _ => throw new InvalidOperationException($"Unknown player ref: {s}"),
    };

    private static readonly HashSet<string> KnownBuffTypes =
    [
        BuffTypes.Tp, BuffTypes.Yield, BuffTypes.Av,
        BuffTypes.CannotAttack, BuffTypes.IncidentImmune, BuffTypes.IncidentBlock,
        BuffTypes.IncidentReduction, BuffTypes.Ransomware, BuffTypes.ReservedInstance,
        BuffTypes.ScaleCostReduction, BuffTypes.DeployDiscount, BuffTypes.MaintenanceReduction,
        BuffTypes.PendingRevival, BuffTypes.AttackDamageReduction, BuffTypes.CountMultiplier,
        BuffTypes.SlaPenalty, BuffTypes.SlaPenaltyReduction, BuffTypes.TpSuppressed,
    ];

    private static string MapBuffName(string yamlBuff) => yamlBuff switch
    {
        BuffTypes.Tp => EffectTypes.BuffTP,
        BuffTypes.Yield => EffectTypes.BuffYield,
        _ when KnownBuffTypes.Contains(yamlBuff) => yamlBuff,
        _ => throw new InvalidOperationException($"Unknown buff type: {yamlBuff}"),
    };

    /// <summary>
    /// Parses card_type from a JSON element. 各値は category 名 (Compute/Data/Platform...)
    /// または subtype 名 (VM/Container/Database...) のいずれでもよく、後段の matcher が
    /// dual-match (CardType OR Subtype) で判定する。lowercase "data"/"compute" は旧 YAML
    /// 互換のため category 名にエイリアスする。
    /// </summary>
    private static List<string>? ParseCardTypes(JsonElement element)
    {
        if (!element.TryGetProperty("card_type", out var ctEl))
        {
            return null;
        }

        if (ctEl.ValueKind == JsonValueKind.String)
        {
            string val = ctEl.GetString()!;
            return [LowercaseCategoryAliases.GetValueOrDefault(val, val)];
        }

        if (ctEl.ValueKind == JsonValueKind.Array)
        {
            return ctEl.EnumerateArray()
                .Select(e => e.GetString()!)
                .Select(v => LowercaseCategoryAliases.GetValueOrDefault(v, v))
                .ToList();
        }

        return null;
    }

    private static List<string>? ParseCardIds(JsonElement element)
    {
        if (!element.TryGetProperty("card_id", out var idEl))
        {
            return null;
        }

        if (idEl.ValueKind == JsonValueKind.String)
        {
            return [idEl.GetString()!];
        }

        if (idEl.ValueKind == JsonValueKind.Array)
        {
            return idEl.EnumerateArray().Select(e => e.GetString()!).ToList();
        }

        return null;
    }

    private static EffectCategory ParseEffectCategory(string s) => s switch
    {
        EffectCategories.BudgetGain => EffectCategory.BudgetGain,
        EffectCategories.BudgetPenalty => EffectCategory.BudgetPenalty,
        EffectCategories.InsightAbsorb => EffectCategory.InsightAbsorb,
        EffectCategories.InsightGain => EffectCategory.InsightGain,
        EffectCategories.SingleDamage => EffectCategory.SingleDamage,
        EffectCategories.AoeDamage => EffectCategory.AoEDamage,
        EffectCategories.Buff => EffectCategory.Buff,
        EffectCategories.Debuff => EffectCategory.Debuff,
        EffectCategories.Heal => EffectCategory.Heal,
        EffectCategories.Draw => EffectCategory.Draw,
        EffectCategories.Search => EffectCategory.Search,
        EffectCategories.DeployFree => EffectCategory.DeployFree,
        EffectCategories.RecoverCard => EffectCategory.RecoverCard,
        EffectCategories.RevealReactive => EffectCategory.RevealReactive,
        EffectCategories.DestroyPlatform => EffectCategory.DestroyPlatform,
        EffectCategories.CancelAction => EffectCategory.CancelAction,
        EffectCategories.Survive => EffectCategory.Survive,
        "self_destruct" => EffectCategory.SelfDestruct,
        "cost_reduction" => EffectCategory.CostReduction,
        "defensive" => EffectCategory.Defensive,
        "utility" => EffectCategory.Utility,
        _ => throw new InvalidOperationException($"Unknown effect category: {s}"),
    };

    private static EffectTargetType ParseEffectTargetType(string s) => s switch
    {
        EffectTargetTypes.None => EffectTargetType.None,
        EffectTargetTypes.Choice => EffectTargetType.Choice,
        EffectTargetTypes.AllOpp => EffectTargetType.AllOpp,
        EffectTargetTypes.Myself => EffectTargetType.Myself,
        _ => throw new InvalidOperationException($"Unknown effect target type: {s}"),
    };
}


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
            foreach (var guardElement in def.Guard)
            {
                ops.Add(BuildGuard(guardElement));
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
            foreach (var opElement in def.Ops)
            {
                ops.Add(BuildOp(opElement));
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
            if (meta.TryGetValue("categories", out var catElement))
            {
                categories = catElement.EnumerateArray()
                    .Select(c => ParseEffectCategory(c.GetString()!))
                    .ToList();
            }
            if (meta.TryGetValue("target", out var tgtElement))
            {
                targetType = ParseEffectTargetType(tgtElement.GetString()!);
            }
            if (meta.TryGetValue("zone", out var zoneElement))
            {
                zoneHint = zoneElement.GetString();
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
                p.GetInt64Or("override_av", 0)),

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

        if (p.TryGetProperty("budget_damage", out var bdElement))
        {
            var budgetDamage = BuildAmount(bdElement);
            return new IncidentDamageOp(selector, amount, budgetDamage);
        }

        return new DealDamageOp(selector, amount);
    }

    private static IEffectOp BuildApplyBuff(JsonElement p)
    {
        var selector = BuildSelector(p.GetProperty("selector"));
        string buff = MapBuffName(p.GetProperty("buff").GetString()!);
        var amount = BuildAmount(p.GetProperty("amount"));
        string duration = p.GetStringOr("duration", EffectDurations.Permanent);
        string? sourceId = p.GetStringOrNull("source_id");
        string mode = p.GetStringOr("mode", "");

        return new ApplyBuffOp(selector, buff, amount, duration, sourceId, mode);
    }

    private static IEffectOp BuildSearchRepo(JsonElement p)
    {
        string? faction = p.GetStringOrNull("faction");
        return new SearchRepoOp { Faction = faction };
    }

    private static IEffectOp BuildRequestSlotFromHand(JsonElement p)
    {
        Func<CardDefinition, bool>? filter = p.TryGetProperty("filter", out var filterElement)
            ? BuildCardFilter(filterElement) : null;
        return new RequestSlotFromHandOp { Filter = filter };
    }

    private static IEffectOp BuildRequestSlotFromRepo(JsonElement p)
    {
        Func<CardDefinition, bool>? filter = p.TryGetProperty("filter", out var filterElement)
            ? BuildCardFilter(filterElement) : null;
        long overrideAV = p.GetInt64Or("override_av", 0);
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
        p.HasProperty("faction")
        || p.HasProperty("card_type")
        || p.HasProperty("card_id");

    // ================================================================
    // Selector builder
    // ================================================================

    private static ISelector BuildSelector(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? pick = element.GetStringOr("pick", SelectorPickModes.All);
            if (pick == SelectorPickModes.Choice)
            {
                string? owner = element.GetStringOr("owner", PlayerRefs.Myself);
                string? zone = element.GetStringOrNull("zone");
                string? faction = element.GetStringOrNull("faction");
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

        string? owner = element.GetStringOr("owner", PlayerRefs.Myself);
        string? zone = element.GetStringOrNull("zone");
        string? faction = element.GetStringOrNull("faction");
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
        if (element.TryGetProperty("ref", out var refElement))
        {
            string refStr = refElement.GetString()!;
            var parts = refStr.Split('.');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"Invalid ref format: {refStr}");
            }

            double multiply = element.TryGetProperty("multiply", out var mulElement)
                ? mulElement.GetDouble()
                : 1.0;

            return new RefAmount(parts[0], parts[1], multiply);
        }

        // per-count: { base, per: { count: selector, value }, max? }
        if (element.TryGetProperty("per", out var perElement))
        {
            long baseVal = element.GetInt64Or("base", 0);
            long? max = element.GetInt64OrNull("max");

            var countSelectorElement = perElement.GetProperty("count");
            long perValue = perElement.GetProperty("value").GetInt64();

            var countSelector = BuildCountSelector(countSelectorElement);
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
        // negate は反転条件であって判定種別ではないため、sub-builder に持たせるとガード種別ごとに
        // 反転ラップが重複する。外側で 1 度だけ NegateGuardOp を被せる責務分離。
        bool negate = element.GetBoolOrFalse("negate");

        IEffectOp op = BuildGuardBody(element);
        return negate ? new NegateGuardOp(op) : op;
    }

    private static IEffectOp BuildGuardBody(JsonElement element)
    {
        if (element.TryGetProperty("stat", out var statElement))
        {
            return BuildStatGuard(statElement);
        }

        if (element.TryGetProperty("count", out var countElement))
        {
            return BuildCountGuard(countElement);
        }

        if (element.TryGetProperty("match", out var matchElement))
        {
            return BuildMatchGuard(matchElement);
        }

        if (element.TryGetProperty("not_same", out var notSameElement))
        {
            var (a, b) = ParseResourceRefPair(notSameElement);
            return new GuardNotSameOp(a, b);
        }

        if (element.TryGetProperty("same", out var sameElement))
        {
            var (a, b) = ParseResourceRefPair(sameElement);
            return new GuardSameOp(a, b);
        }

        if (element.TryGetProperty("event_owner", out var ownerElement))
        {
            bool isSelf = ownerElement.GetString() switch
            {
                PlayerRefs.Myself => true,
                PlayerRefs.Opponent => false,
                _ => throw new InvalidOperationException($"Unknown event_owner: {ownerElement.GetString()}"),
            };
            return new GuardEventOwnerOp(isSelf);
        }

        if (element.GetBoolOrFalse("lethal"))
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
            if (element.GetInt64OrNull("min") is long min)
            {
                return new RequireBudgetOp(min);
            }
            if (element.GetInt64OrNull("max") is long max)
            {
                return new RequireMaxBudgetOp(max);
            }
        }

        if (selectorStr == "target" && stat == "av")
        {
            if (element.GetInt64OrNull("max") is long max)
            {
                return new GuardTargetAVOp(max);
            }
        }

        throw new InvalidOperationException($"Unsupported stat guard: selector={selectorStr}, stat={stat}");
    }

    private static IEffectOp BuildCountGuard(JsonElement element)
    {
        var selectorElement = element.GetProperty("selector");
        int? max = element.GetInt32OrNull("max");
        // max のみ指定 = 「n 体以下」を意図しており 0 体も成立させたい。両方省略 = 「n 体以上」
        // の慣用で「とにかく 1 体は居ること」の暗黙仕様を保つため min=1 を残す。
        int min = element.GetInt32OrNull("min") ?? (max is not null ? 0 : 1);

        // NPC classifier 互換のため、faction 限定の count guard は RequireFactionCountOp に集約する。
        // 反転 (negate) は外側 BuildGuard が NegateGuardOp で被せるため、ここでは関与しない。
        if (max is null && selectorElement.ValueKind == JsonValueKind.Object)
        {
            string? owner = selectorElement.GetStringOrNull("owner");
            string? faction = selectorElement.GetStringOrNull("faction");
            bool hasZone = selectorElement.HasProperty("zone");
            bool hasCardType = selectorElement.HasProperty("card_type");
            bool hasCardId = selectorElement.HasProperty("card_id");

            if (owner == PlayerRefs.Myself && faction is not null && !hasZone && !hasCardType && !hasCardId)
            {
                return new RequireFactionCountOp(faction, min);
            }
        }

        return BuildResourceCountGuard(selectorElement, min, max);
    }

    private static IEffectOp BuildResourceCountGuard(JsonElement selectorElement, int min, int? max)
    {
        string? owner = null;
        string? zone = null;
        string? faction = null;
        List<string>? cardTypes = null;
        List<string>? cardIds = null;

        if (selectorElement.ValueKind == JsonValueKind.Object)
        {
            owner = selectorElement.GetStringOr("owner", PlayerRefs.Myself);
            zone = selectorElement.GetStringOrNull("zone");
            faction = selectorElement.GetStringOrNull("faction");
            cardTypes = ParseCardTypes(selectorElement);
            cardIds = ParseCardIds(selectorElement);
        }

        return new ResourceCountGuardOp(
            owner ?? PlayerRefs.Myself, zone, faction, cardTypes, cardIds, min, max);
    }

    private static IEffectOp BuildMatchGuard(JsonElement element)
    {
        var selector = ParseMatchSelector(element.GetProperty("selector").GetString()!);
        string? faction = element.GetStringOrNull("faction");
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

    private static Func<CardDefinition, bool> BuildCardFilter(JsonElement filterElement)
    {
        string? faction = filterElement.GetStringOrNull("faction");
        var cardTypes = ParseCardTypes(filterElement);
        var cardIds = ParseCardIds(filterElement);

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
        if (!element.TryGetProperty("card_type", out var ctElement))
        {
            return null;
        }

        if (ctElement.ValueKind == JsonValueKind.String)
        {
            string val = ctElement.GetString()!;
            return [LowercaseCategoryAliases.GetValueOrDefault(val, val)];
        }

        if (ctElement.ValueKind == JsonValueKind.Array)
        {
            return ctElement.EnumerateArray()
                .Select(e => e.GetString()!)
                .Select(v => LowercaseCategoryAliases.GetValueOrDefault(v, v))
                .ToList();
        }

        return null;
    }

    private static List<string>? ParseCardIds(JsonElement element)
    {
        if (!element.TryGetProperty("card_id", out var idElement))
        {
            return null;
        }

        if (idElement.ValueKind == JsonValueKind.String)
        {
            return [idElement.GetString()!];
        }

        if (idElement.ValueKind == JsonValueKind.Array)
        {
            return idElement.EnumerateArray().Select(e => e.GetString()!).ToList();
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


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
    // "data" category expands to these card types
    private static readonly List<string> DataCardTypes = [CardTypes.Database, CardTypes.CacheDB, CardTypes.ObjectStorage];

    // "compute" category expands to these card types
    private static readonly List<string> ComputeCardTypes = [CardTypes.Compute, CardTypes.Container, CardTypes.Orchestrator, CardTypes.Serverless, CardTypes.AiMl];

    /// <summary>
    /// Loads effects from card definitions and registers them into the registry.
    /// Cards with a <c>custom</c> effect require a matching entry in the custom registry.
    /// </summary>
    public static void LoadFromCards(
        IEnumerable<CardDefinition> cards,
        EffectRegistry registry,
        ICustomEffectRegistry? customRegistry = null)
    {
        foreach (var card in cards)
        {
            if (card.Effects is not { Count: > 0 }) continue;
            LoadCardEffects(card.CardId, card.Effects, registry, customRegistry);
        }
    }

    private static void LoadCardEffects(
        string cardId,
        List<EffectDef> effects,
        EffectRegistry registry,
        ICustomEffectRegistry? customRegistry)
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
        ICustomEffectRegistry? customRegistry)
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
        ICustomEffectRegistry? customRegistry)
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

        // UseEffectProcessor already enforces once-per-turn for activate triggers
        if (def.UseLimit is not null && ParseTrigger(def.Trigger) != TriggerType.Activate)
        {
            bool perGame = def.UseLimit == "once_per_game";
            ops.Insert(0, new CheckUseLimitOp(perGame));
            ops.Add(new MarkUseLimitOp(perGame));
        }

        return ops;
    }

    private static List<IEffectOp> BuildCustomBlock(
        string customName,
        Dictionary<string, JsonElement>? meta,
        ICustomEffectRegistry? customRegistry)
    {
        if (customRegistry is null)
        {
            return [];
        }

        var fn = customRegistry.Build(customName, meta);
        if (fn is null)
        {
            return [];
        }

        List<EffectCategory>? categories = null;
        EffectTargetType targetType = EffectTargetType.None;
        string? zoneHint = null;

        if (meta is not null)
        {
            if (meta.TryGetValue("categories", out var catEl))
            {
                categories = catEl.EnumerateArray()
                    .Select(c => TryParseEffectCategory(c.GetString()!))
                    .Where(c => c is not null)
                    .Select(c => c!.Value)
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

    private static IEffectOp BuildOp(JsonElement el)
    {
        using var enumerator = el.EnumerateObject();
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("Empty op object");
        }

        var prop = enumerator.Current;
        string opName = prop.Name;
        JsonElement p = prop.Value;

        return opName switch
        {
            "gain_budget" => new GainBudgetOp(
                ParsePlayerRef(p.GetProperty("target").GetString()!),
                BuildAmount(p.GetProperty("amount"))),

            "lose_budget" => new LoseBudgetOp(
                ParsePlayerRef(p.GetProperty("target").GetString()!),
                BuildAmount(p.GetProperty("amount"))),

            "deal_damage" => BuildDealDamage(p),

            "heal_damage" => new HealDamageOp(
                BuildSelector(p.GetProperty("selector")),
                BuildAmount(p.GetProperty("amount"))),

            "destroy_check" => new DestroyCheckOp(
                ParsePlayerRef(p.GetProperty("target").GetString()!)),

            "survive_destruction" => new SurviveDestructionOp(
                p.GetProperty("av").GetInt64()),

            "apply_buff" => BuildApplyBuff(p),

            "draw" => new DrawCardsOp(
                p.GetProperty("count").GetInt32()),

            "search_repo" => BuildSearchRepo(p),

            "add_to_hand" => AddToHandOp.Instance,

            "trash_to_hand" => new TrashToHandOp(),

            "deploy_from_hand" => BuildDeployFromHand(p),

            "deploy_from_repo" => BuildDeployFromRepo(p),

            "deploy_from_repo_same_card" => new DeployFromRepoSameCardOp(
                p.TryGetProperty("override_av", out var oav) ? oav.GetInt64() : 0),

            "destroy_platform" => new DestroyPlatformOp(),

            "scale_to_rank" => new ScaleToRankOp(
                p.GetProperty("rank").GetString()!),

            "cancel_action" => SetCancelActionOp.Instance,

            "reveal_reactive" => new RevealReactiveOp(),

            "peek_reactive" => new PeekReactiveOp(),

            "absorb_insight" => new AbsorbInsightOp(
                BuildAmount(p.GetProperty("amount"))),

            "gain_insight" => new GainInsightOp(
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
        string duration = p.TryGetProperty("duration", out var durEl) ? durEl.GetString()! : "permanent";
        string? sourceId = p.TryGetProperty("source_id", out var sid) ? sid.GetString() : null;

        return new ApplyBuffOp(selector, buff, amount, duration, sourceId);
    }

    private static IEffectOp BuildSearchRepo(JsonElement p)
    {
        string? faction = p.TryGetProperty("faction", out var fEl) ? fEl.GetString() : null;
        return new SearchRepoOp { Faction = faction };
    }

    private static IEffectOp BuildDeployFromHand(JsonElement p)
    {
        Func<CardDefinition, bool>? filter = p.TryGetProperty("filter", out var filterEl)
            ? BuildCardFilter(filterEl) : null;
        return new DeployFromHandOp { Filter = filter };
    }

    private static IEffectOp BuildDeployFromRepo(JsonElement p)
    {
        Func<CardDefinition, bool>? filter = p.TryGetProperty("filter", out var filterEl)
            ? BuildCardFilter(filterEl) : null;
        long overrideAV = p.TryGetProperty("override_av", out var oav) ? oav.GetInt64() : 0;
        return new DeployFromRepoOp { Filter = filter, OverrideAV = overrideAV };
    }

    // ================================================================
    // Selector builder
    // ================================================================

    private static ISelector BuildSelector(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            string? pick = el.TryGetProperty("pick", out var pk) ? pk.GetString() : "all";
            if (pick == "choice")
            {
                string? owner = el.TryGetProperty("owner", out var ow) ? ow.GetString() : "self";
                string? zone = el.TryGetProperty("zone", out var zn) ? zn.GetString() : null;
                string? faction = el.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
                var cardTypes = ParseCardTypes(el);

                return new ByChoiceSelector
                {
                    Zone = zone,
                    Faction = faction,
                    CardType = cardTypes is { Count: 1 } ? cardTypes[0] : null,
                    Owner = owner ?? "self",
                };
            }
        }

        return BuildSelectorCore(el);
    }

    private static ISelector BuildSelectorCore(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            return el.GetString() switch
            {
                "source" => SourceSelector.Instance,
                "target" => TargetSelector.Instance,
                _ => throw new InvalidOperationException($"Unknown selector keyword: {el.GetString()}"),
            };
        }

        string? owner = el.TryGetProperty("owner", out var ow) ? ow.GetString() : "self";
        string? zone = el.TryGetProperty("zone", out var zn) ? zn.GetString() : null;
        string? faction = el.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
        bool excludeSource = el.TryGetProperty("exclude", out var ex) && ex.GetString() == "source";
        var cardTypes = ParseCardTypes(el);

        ISelector selector = owner switch
        {
            "self" => new AllOwnSelector { Zone = zone, Faction = faction, CardTypes = cardTypes },
            "opponent" => new AllOpponentSelector { Zone = zone, Faction = faction, CardTypes = cardTypes },
            "both" => new UnionSelector(
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

    private static IAmountResolver BuildAmount(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number)
        {
            return new StaticAmount(el.GetInt64());
        }

        // ref-based: { ref: "source.yield", multiply: 1.0 }
        if (el.TryGetProperty("ref", out var refEl))
        {
            string refStr = refEl.GetString()!;
            var parts = refStr.Split('.');
            if (parts.Length != 2)
            {
                throw new InvalidOperationException($"Invalid ref format: {refStr}");
            }

            double multiply = el.TryGetProperty("multiply", out var mulEl)
                ? mulEl.GetDouble()
                : 1.0;

            return new RefAmount(parts[0], parts[1], multiply);
        }

        // per-count: { base, per: { count: selector, value }, max? }
        if (el.TryGetProperty("per", out var perEl))
        {
            long baseVal = el.TryGetProperty("base", out var baseEl) ? baseEl.GetInt64() : 0;
            long? max = el.TryGetProperty("max", out var maxEl) ? maxEl.GetInt64() : null;

            var countSelectorEl = perEl.GetProperty("count");
            long perValue = perEl.GetProperty("value").GetInt64();

            var countSelector = BuildCountSelector(countSelectorEl);
            return new PerCountAmount(baseVal, countSelector, perValue, max);
        }

        throw new InvalidOperationException($"Unknown amount format: {el}");
    }

    /// <summary>
    /// Builds a selector for counting purposes (used in per-count amounts).
    /// Count selectors don't support pick:choice, so delegates directly to the core builder.
    /// </summary>
    private static ISelector BuildCountSelector(JsonElement el) => BuildSelectorCore(el);

    // ================================================================
    // Guard builder
    // ================================================================

    private static IEffectOp BuildGuard(JsonElement el)
    {
        bool negate = el.TryGetProperty("negate", out var negEl)
            && negEl.ValueKind == JsonValueKind.True;

        if (el.TryGetProperty("stat", out var statEl))
        {
            return BuildStatGuard(statEl, negate);
        }

        if (el.TryGetProperty("count", out var countEl))
        {
            return BuildCountGuard(countEl, negate);
        }

        if (el.TryGetProperty("match", out var matchEl))
        {
            return BuildMatchGuard(matchEl, negate);
        }

        if (el.TryGetProperty("not_same", out var notSameEl))
        {
            if (negate)
            {
                throw new InvalidOperationException("negate on not_same is not supported");
            }
            return GuardNotSelfOp.Instance;
        }

        throw new InvalidOperationException($"Unknown guard type: {el}");
    }

    private static IEffectOp BuildStatGuard(JsonElement el, bool negate)
    {
        string selectorStr = el.GetProperty("selector").GetString()!;
        string stat = el.GetProperty("stat").GetString()!;

        if (selectorStr == "self" && stat == "budget")
        {
            if (el.TryGetProperty("min", out var minEl))
            {
                IEffectOp op = new RequireBudgetOp(minEl.GetInt64());
                return negate ? new NegateGuardOp(op) : op;
            }
            if (el.TryGetProperty("max", out var maxEl))
            {
                IEffectOp op = new RequireMaxBudgetOp(maxEl.GetInt64());
                return negate ? new NegateGuardOp(op) : op;
            }
        }

        if (selectorStr == "target" && stat == "av")
        {
            if (el.TryGetProperty("max", out var maxEl))
            {
                IEffectOp op = new GuardTargetAVOp(maxEl.GetInt64());
                return negate ? new NegateGuardOp(op) : op;
            }
        }

        throw new InvalidOperationException($"Unsupported stat guard: selector={selectorStr}, stat={stat}");
    }

    private static IEffectOp BuildCountGuard(JsonElement el, bool negate)
    {
        var selectorEl = el.GetProperty("selector");
        int min = el.TryGetProperty("min", out var minEl) ? minEl.GetInt32() : 1;

        // Try to map to existing RequireFactionCountOp for NPC classifier compatibility
        if (!negate && selectorEl.ValueKind == JsonValueKind.Object)
        {
            string? owner = selectorEl.TryGetProperty("owner", out var ow) ? ow.GetString() : null;
            string? faction = selectorEl.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
            bool hasZone = selectorEl.TryGetProperty("zone", out _);
            bool hasCardType = selectorEl.TryGetProperty("card_type", out _);
            bool hasCardId = selectorEl.TryGetProperty("card_id", out _);

            if (owner == "self" && faction is not null && !hasZone && !hasCardType && !hasCardId)
            {
                return new RequireFactionCountOp(faction, min);
            }
        }

        // General-purpose count guard
        return BuildResourceCountGuard(selectorEl, min, negate);
    }

    private static IEffectOp BuildResourceCountGuard(JsonElement selectorEl, int min, bool negate)
    {
        string? owner = null;
        string? zone = null;
        string? faction = null;
        List<string>? cardTypes = null;
        List<string>? cardIds = null;

        if (selectorEl.ValueKind == JsonValueKind.Object)
        {
            owner = selectorEl.TryGetProperty("owner", out var ow) ? ow.GetString() : "self";
            zone = selectorEl.TryGetProperty("zone", out var zn) ? zn.GetString() : null;
            faction = selectorEl.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
            cardTypes = ParseCardTypes(selectorEl);
            cardIds = ParseCardIds(selectorEl);
        }

        return new ResourceCountGuardOp(
            owner ?? "self", zone, faction, cardTypes, cardIds, min, negate);
    }

    private static IEffectOp BuildMatchGuard(JsonElement el, bool negate)
    {
        string? faction = el.TryGetProperty("faction", out var fc) ? fc.GetString() : null;
        string? cardType = el.TryGetProperty("card_type", out var ct) ? ct.GetString() : null;

        IEffectOp op = new GuardFactionOp(faction ?? "", cardType);
        return negate ? new NegateGuardOp(op) : op;
    }

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
            if (faction is { Length: > 0 } && card.Faction != faction) return false;
            if (cardTypes is { Count: > 0 } && !cardTypes.Contains(card.CardType)) return false;
            if (cardIds is { Count: > 0 } && !cardIds.Contains(card.CardId)) return false;
            return true;
        };
    }

    // ================================================================
    // Parsing helpers
    // ================================================================

    private static TriggerType ParseTrigger(string trigger) => trigger switch
    {
        "deploy" => TriggerType.Deploy,
        "activate" => TriggerType.Activate,
        "passive" => TriggerType.OnEndPhase, // 後方互換: passive → OnEndPhase
        "on_end_phase" => TriggerType.OnEndPhase,
        "on_field_change" => TriggerType.OnFieldChange,
        "on_scale_up" => TriggerType.OnScaleUp,
        "on_attack" => TriggerType.OnAttack,
        "on_hit" => TriggerType.OnHit,
        "on_destroy" => TriggerType.OnDestroy,
        "reactive" => TriggerType.Reactive,
        "on_enemy_deploy" => TriggerType.OnEnemyDeploy,
        _ => throw new InvalidOperationException($"Unknown trigger: {trigger}"),
    };

    private static PlayerRef ParsePlayerRef(string s) => s switch
    {
        "self" => PlayerRef.Self,
        "opponent" => PlayerRef.Opponent,
        "both" => PlayerRef.Both,
        _ => throw new InvalidOperationException($"Unknown player ref: {s}"),
    };

    private static string MapBuffName(string yamlBuff) => yamlBuff switch
    {
        "tp" => EffectTypes.BuffTP,
        "yield" => EffectTypes.BuffYield,
        _ => yamlBuff,
    };

    /// <summary>
    /// Parses card_type from a JSON element, handling:
    /// - single string: "Database" → ["Database"]
    /// - list: ["Compute", "AI/ML"] → ["Compute", "AI/ML"]
    /// - category: "data" → ["Database", "CacheDB", "ObjectStorage"]
    /// </summary>
    private static List<string>? ParseCardTypes(JsonElement el)
    {
        if (!el.TryGetProperty("card_type", out var ctEl))
        {
            return null;
        }

        if (ctEl.ValueKind == JsonValueKind.String)
        {
            string val = ctEl.GetString()!;
            return val switch
            {
                "data" => new List<string>(DataCardTypes),
                "compute" => new List<string>(ComputeCardTypes),
                _ => [val],
            };
        }

        if (ctEl.ValueKind == JsonValueKind.Array)
        {
            return ctEl.EnumerateArray().Select(e => e.GetString()!).ToList();
        }

        return null;
    }

    private static List<string>? ParseCardIds(JsonElement el)
    {
        if (!el.TryGetProperty("card_id", out var idEl))
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

    private static EffectCategory? TryParseEffectCategory(string s) => s switch
    {
        "budget_gain" => EffectCategory.BudgetGain,
        "budget_penalty" => EffectCategory.BudgetPenalty,
        "insight_absorb" => EffectCategory.InsightAbsorb,
        "insight_gain" => EffectCategory.InsightGain,
        "single_damage" => EffectCategory.SingleDamage,
        "aoe_damage" => EffectCategory.AoEDamage,
        "buff" => EffectCategory.Buff,
        "debuff" => EffectCategory.Debuff,
        "heal" => EffectCategory.Heal,
        "draw" => EffectCategory.Draw,
        "search" => EffectCategory.Search,
        "deploy_free" => EffectCategory.DeployFree,
        "recover_card" => EffectCategory.RecoverCard,
        "reveal_reactive" => EffectCategory.RevealReactive,
        "destroy_platform" => EffectCategory.DestroyPlatform,
        "cancel_action" => EffectCategory.CancelAction,
        "survive" => EffectCategory.Survive,
        _ => null,
    };

    private static EffectTargetType ParseEffectTargetType(string s) => s switch
    {
        "none" => EffectTargetType.None,
        "choice" => EffectTargetType.Choice,
        "all_opp" => EffectTargetType.AllOpp,
        "self" => EffectTargetType.Self,
        _ => throw new InvalidOperationException($"Unknown effect target type: {s}"),
    };
}


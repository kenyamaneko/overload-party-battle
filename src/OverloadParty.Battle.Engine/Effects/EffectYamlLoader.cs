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
    // 新 YAML は CardTypes.DataResource / CardTypes.Compute を直接使う。
    private static readonly Dictionary<string, string> LowercaseCategoryAliases = new()
    {
        ["data"] = CardTypes.DataResource,
        ["compute"] = CardTypes.Compute,
    };

    /// <summary>
    /// Loads effects from effect sources and registers them into the registry.
    /// Sources with a <c>custom</c> effect require a matching entry in the custom registry.
    /// </summary>
    /// <param name="sources">読み込み対象の効果供給元群。</param>
    /// <param name="registry">登録先の効果レジストリ。</param>
    /// <param name="customRegistry">カスタム効果のレジストリ。</param>
    public static void LoadEffectSources(
        IEnumerable<IEffectSource> sources,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        foreach (var source in sources)
        {
            if (source.EffectDefs is not { Count: > 0 } effectDefs)
            {
                continue;
            }

            LoadSourceEffects(source.EffectSourceId, effectDefs, registry, customRegistry);
        }
    }

    private static void LoadSourceEffects(
        string sourceId,
        IReadOnlyList<EffectDef> effects,
        EffectRegistry registry,
        CustomEffectRegistry customRegistry)
    {
        var eventDefs = new List<EffectDef>();

        foreach (var def in effects)
        {
            var trigger = ParseTrigger(def.Trigger);

            if (trigger == TriggerType.OnFieldChange)
            {
                registry.RegisterPassive(sourceId, BuildPassiveDef(def));
                continue;
            }

            if (trigger == TriggerType.OnDeploy && IsOnDeployPassiveDef(def))
            {
                registry.RegisterPassive(sourceId, BuildPassiveDef(def));
                continue;
            }

            eventDefs.Add(def);
        }

        var byTrigger = eventDefs.GroupBy(e => ParseTrigger(e.Trigger));

        foreach (var group in byTrigger)
        {
            var defs = group.ToList();
            var block = BuildTriggerBlock(defs, customRegistry);
            if (block.Guards.Length > 0 || block.Ops.Length > 0)
            {
                registry.RegisterComposed(sourceId, group.Key, block);
            }
        }
    }

    // ================================================================
    // パッシブ効果 (常時再計算されるバフ) の分類・構築
    // ================================================================

    /// <summary>duration が this_turn / while_on_field の apply_buff だけで構成される on_deploy def か判定する。</summary>
    private static bool IsOnDeployPassiveDef(EffectDef def)
    {
        if (def.Custom is not null || def.Choice is not null) { return false; }
        if (def.Ops is not { Count: > 0 } ops) { return false; }

        foreach (var opElement in ops)
        {
            if (!TryGetSoleOp(opElement, out string opName, out var body)) { return false; }
            if (opName != EffectOps.ApplyBuff) { return false; }
            if (body.GetStringOr("duration", EffectDurations.Permanent) != EffectDurations.WhileOnField)
            {
                return false;
            }
        }
        return true;
    }

    private static readonly HashSet<string> PassiveApplyBuffDurations =
    [
        EffectDurations.ThisTurn,
        EffectDurations.WhileOnField,
    ];

    /// <summary>on_field_change / on_deploy+while_on_field と分類された def をパッシブ効果として構築する。</summary>
    private static PassiveEffectDef BuildPassiveDef(EffectDef def)
    {
        if (def.Custom is not null)
        {
            throw new InvalidOperationException($"passive effect def cannot use custom: {def.Custom}");
        }
        if (def.Choice is not null)
        {
            throw new InvalidOperationException("passive effect def cannot use choice");
        }
        if (def.Ops is not { Count: > 0 } ops)
        {
            throw new InvalidOperationException("passive effect def requires at least one apply_buff op");
        }

        var guards = (def.Guard ?? []).Select(BuildGuard).ToArray();
        var applications = ops.Select(BuildPassiveApplication).ToArray();

        bool hasCountMultiplier = applications.Any(a => a.EffectType == BuffTypes.CountMultiplier);
        if (hasCountMultiplier && guards.Length > 0)
        {
            throw new InvalidOperationException(
                "a passive def with a count_multiplier application cannot also have guards");
        }

        return new PassiveEffectDef { Guards = guards, Applications = applications };
    }

    private static PassiveBuffApplication BuildPassiveApplication(JsonElement opElement)
    {
        if (!TryGetSoleOp(opElement, out string opName, out var body) || opName != EffectOps.ApplyBuff)
        {
            throw new InvalidOperationException($"passive effect def ops must all be apply_buff, got: {opName}");
        }

        string duration = body.GetStringOr("duration", EffectDurations.Permanent);
        if (!PassiveApplyBuffDurations.Contains(duration))
        {
            throw new InvalidOperationException($"unsupported duration for a passive apply_buff: {duration}");
        }

        var selector = BuildSelector(body.GetProperty("selector"));
        if (selector is ByChoiceSelector)
        {
            throw new InvalidOperationException("passive apply_buff selector cannot use pick: choice");
        }

        string buff = MapBuffName(body.GetProperty("buff").GetString()!);

        var amountElement = body.GetProperty("amount");
        if (amountElement.ValueKind == JsonValueKind.Object && amountElement.TryGetProperty("ref", out _))
        {
            throw new InvalidOperationException("passive apply_buff amount cannot use ref");
        }
        var amount = BuildAmount(amountElement);

        if (buff == BuffTypes.CountMultiplier && amount is not StaticAmount)
        {
            throw new InvalidOperationException("count_multiplier passive application amount must be static");
        }

        string mode = body.GetStringOr("mode", "");

        return new PassiveBuffApplication
        {
            Selector = selector,
            EffectType = buff,
            Amount = amount,
            Mode = mode,
        };
    }

    /// <summary>op オブジェクト (単一キーの JSON object) からオペレーション名と中身を取り出す。</summary>
    private static bool TryGetSoleOp(JsonElement element, out string opName, out JsonElement body)
    {
        using var enumerator = element.EnumerateObject();
        if (!enumerator.MoveNext())
        {
            opName = "";
            body = default;
            return false;
        }

        var prop = enumerator.Current;
        opName = prop.Name;
        body = prop.Value;
        return true;
    }

    // ================================================================
    // Block composition
    // ================================================================

    private static BuiltBlock BuildTriggerBlock(
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
            // Single root: 親 guard が top-level に上がる。dependent は wrap して
            // 親成功時のみ起動する DependentEffectOp に格納する。
            string rootId = independent[0].Id
                ?? throw new InvalidOperationException("Root block with dependents must have an id");
            var rootBlock = BuildSingleBlock(independent[0], customRegistry);
            var ops = new List<IEffectOp>(rootBlock.Ops) { new MarkGroupSucceededOp(rootId) };

            foreach (var dep in dependent)
            {
                var depBlock = BuildSingleBlock(dep, customRegistry);
                if (depBlock.Guards.Length == 0 && depBlock.Ops.Length == 0) { continue; }

                if (dep.After is null || dep.After != rootId)
                {
                    throw new InvalidOperationException(
                        $"Effect block references unknown after target '{dep.After}'");
                }
                ops.Add(new DependentEffectOp(dep.After, depBlock));
            }
            return new BuiltBlock { Guards = rootBlock.Guards, Ops = ops.ToArray() };
        }

        // Multiple independent blocks: 各ブロックの guard / ops を EffectGroupOp に包む。
        var effectGroupIds = new HashSet<string>();
        var isolatedOps = new List<IEffectOp>();
        int autoId = 0;

        foreach (var def in independent)
        {
            var block = BuildSingleBlock(def, customRegistry);
            if (block.Guards.Length == 0 && block.Ops.Length == 0) { continue; }

            string groupId = def.Id ?? $"_auto_{autoId++}";
            isolatedOps.Add(new EffectGroupOp(groupId, block));
            effectGroupIds.Add(groupId);
        }

        foreach (var def in dependent)
        {
            var block = BuildSingleBlock(def, customRegistry);
            if (block.Guards.Length == 0 && block.Ops.Length == 0) { continue; }

            if (def.After is null) { continue; }
            if (!effectGroupIds.Contains(def.After))
            {
                throw new InvalidOperationException(
                    $"Effect block references unknown after target '{def.After}'");
            }
            isolatedOps.Add(new DependentEffectOp(def.After, block));
        }

        return new BuiltBlock { Ops = isolatedOps.ToArray() };
    }

    private static BuiltBlock BuildSingleBlock(
        EffectDef def,
        CustomEffectRegistry customRegistry)
    {
        var guards = new List<IEffectGuard>();
        var ops = new List<IEffectOp>();

        if (def.Guard is { Count: > 0 })
        {
            foreach (var guardElement in def.Guard)
            {
                guards.Add(BuildGuard(guardElement));
            }
        }

        // custom は ops 列の先頭要素として扱う。同じ効果定義に併記された guard / ops / use_limit も
        // 通常の効果定義と同じように合成する。
        if (def.Custom is { } customName)
        {
            ops.AddRange(BuildCustomBlock(customName, def.Meta, customRegistry));
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

        // UseIgnitionProcessor already enforces once-per-turn for ignition triggers
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

        return new BuiltBlock { Guards = guards.ToArray(), Ops = ops.ToArray() };
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
            RequiresPlacementSlot = CustomEffectRegistry.RequiresPlacementSlot(customName),
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

            EffectOps.DestroyCheck => BuildDestroyCheck(p),

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

            EffectOps.ConvertAllInsight => new ConvertAllInsightOp(
                p.GetProperty("rate_percent").GetInt64()),

            _ => throw new InvalidOperationException($"Unknown op: {opName}"),
        };
    }

    private static IEffectOp BuildDestroyCheck(JsonElement p)
    {
        // target は破壊の一元化で挙動に使わなくなったが、typo 検知のため値の妥当性検証だけは続ける。
        ParsePlayerRef(p.GetProperty("target").GetString()!);
        return new DestroyCheckOp();
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

        if (duration == EffectDurations.Continuous)
        {
            throw new InvalidOperationException(
                "duration 'continuous' is engine-managed and cannot appear in card data");
        }
        if (duration == EffectDurations.WhileOnField && selector is not SourceSelector)
        {
            throw new InvalidOperationException(
                "apply_buff outside a passive effect definition with duration 'while_on_field' must use selector: source");
        }

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
        var subtypes = ParseSubtypes(element);

        ISelector selector = owner switch
        {
            PlayerRefs.Myself => new AllOwnSelector { Zone = zone, Faction = faction, CardTypes = cardTypes, Subtypes = subtypes },
            PlayerRefs.Opponent => new AllOpponentSelector { Zone = zone, Faction = faction, CardTypes = cardTypes, Subtypes = subtypes },
            PlayerRefs.Both => new UnionSelector(
                new AllOwnSelector { Zone = zone, Faction = faction, CardTypes = cardTypes, Subtypes = subtypes },
                new AllOpponentSelector { Zone = zone, Faction = faction, CardTypes = cardTypes, Subtypes = subtypes }),
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

    private static IEffectGuard BuildGuard(JsonElement element)
    {
        // negate は反転条件であって判定種別ではないため、sub-builder に持たせると種別ごとに
        // 反転ラップが重複する。外側で 1 度だけ NegateGuard を被せる責務分離。
        bool negate = element.GetBoolOrFalse("negate");

        IEffectGuard guard = BuildGuardBody(element);
        return negate ? new NegateGuard(guard) : guard;
    }

    private static IEffectGuard BuildGuardBody(JsonElement element)
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
            return new NotSameGuard(a, b);
        }

        if (element.TryGetProperty("same", out var sameElement))
        {
            var (a, b) = ParseResourceRefPair(sameElement);
            return new SameGuard(a, b);
        }

        if (element.TryGetProperty("event_owner", out var ownerElement))
        {
            bool isSelf = ownerElement.GetString() switch
            {
                PlayerRefs.Myself => true,
                PlayerRefs.Opponent => false,
                _ => throw new InvalidOperationException($"Unknown event_owner: {ownerElement.GetString()}"),
            };
            return new EventOwnerGuard(isSelf);
        }

        if (element.GetBoolOrFalse("lethal"))
        {
            return LethalGuard.Instance;
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

    private static IEffectGuard BuildStatGuard(JsonElement element)
    {
        string selectorStr = element.GetProperty("selector").GetString()!;
        string stat = element.GetProperty("stat").GetString()!;

        if (selectorStr == "myself" && stat == "budget")
        {
            if (element.GetInt64OrNull("min") is long min)
            {
                return new MinBudgetGuard(min);
            }
            if (element.GetInt64OrNull("max") is long max)
            {
                return new MaxBudgetGuard(max);
            }
        }

        if (selectorStr == "target" && stat == "av")
        {
            if (element.GetInt64OrNull("max") is long max)
            {
                return new TargetAVGuard(max);
            }
        }

        throw new InvalidOperationException($"Unsupported stat guard: selector={selectorStr}, stat={stat}");
    }

    private static IEffectGuard BuildCountGuard(JsonElement element)
    {
        var selectorElement = element.GetProperty("selector");
        int? min = element.GetInt32OrNull("min");
        int? max = element.GetInt32OrNull("max");
        return BuildResourceCountGuard(selectorElement, min, max);
    }

    private static IEffectGuard BuildResourceCountGuard(JsonElement selectorElement, int? min, int? max)
    {
        string? owner = null;
        string? zone = null;
        string? faction = null;
        List<string>? cardTypes = null;
        List<string>? subtypes = null;
        List<string>? cardIds = null;

        if (selectorElement.ValueKind == JsonValueKind.Object)
        {
            owner = selectorElement.GetStringOr("owner", PlayerRefs.Myself);
            zone = selectorElement.GetStringOrNull("zone");
            faction = selectorElement.GetStringOrNull("faction");
            cardTypes = ParseCardTypes(selectorElement);
            subtypes = ParseSubtypes(selectorElement);
            cardIds = ParseCardIds(selectorElement);
        }

        return new ResourceCountGuard(
            owner ?? PlayerRefs.Myself, zone, faction, cardTypes, subtypes, cardIds, min, max);
    }

    private static IEffectGuard BuildMatchGuard(JsonElement element)
    {
        var selector = ParseMatchSelector(element.GetProperty("selector").GetString()!);
        string? faction = element.GetStringOrNull("faction");
        var cardTypes = ParseCardTypes(element);
        var subtypes = ParseSubtypes(element);
        var cardIds = ParseCardIds(element);
        bool? ownerIsOpponent = element.TryGetProperty("owner", out var ow)
            ? ow.GetString() switch
            {
                PlayerRefs.Myself => false,
                PlayerRefs.Opponent => true,
                _ => throw new InvalidOperationException($"Unknown match owner: {ow.GetString()}"),
            }
            : null;

        return new MatchGuard(selector, faction, cardTypes, subtypes, cardIds, ownerIsOpponent);
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
        var subtypes = ParseSubtypes(filterElement);
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

            if (subtypes is { Count: > 0 } && !EffectHelpers.MatchesAnySubtype(card, subtypes))
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
        BuffTypes.CannotAttack, BuffTypes.Dormant, BuffTypes.IncidentImmune, BuffTypes.IncidentBlock,
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
    /// Parses card_type from a JSON element. category 名 (Compute / DataResource / Platform ...) を期待。
    /// lowercase "data" / "compute" は旧 YAML 互換のため category 名にエイリアスする。
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

    /// <summary>Parses subtype from a JSON element. 値は subtype 名 (VM / Container / Database ...) を期待。</summary>
    private static List<string>? ParseSubtypes(JsonElement element)
    {
        if (!element.TryGetProperty("subtype", out var stElement))
        {
            return null;
        }

        if (stElement.ValueKind == JsonValueKind.String)
        {
            return [stElement.GetString()!];
        }

        if (stElement.ValueKind == JsonValueKind.Array)
        {
            return stElement.EnumerateArray().Select(e => e.GetString()!).ToList();
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


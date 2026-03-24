using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Registers all card effect handlers into the EffectRegistry.
/// Organised by faction, matching the Go init.go structure.
/// </summary>
public static class EffectInit
{
    /// <summary>
    /// Registers all card effect handlers into the given registry.
    /// </summary>
    /// <param name="registry">The registry to populate.</param>
    public static void RegisterAllEffects(EffectRegistry registry)
    {
        RegisterSHE(registry);
        RegisterTenki(registry);
        RegisterSugar(registry);
        RegisterTuners(registry);
        RegisterNeutral(registry);
        RegisterIncidents(registry);
        RegisterReactives(registry);
    }

    // ========================
    // SHE (Smile Horizon Express)
    // ========================
    private static void RegisterSHE(EffectRegistry r)
    {
        // SH-0006 SHE RDB - アデリース: Reserved Instance (optional deploy cost -200)
        r.RegisterComposed("SH-0006", TriggerType.Deploy,
            new BranchOnChoiceOp(new Dictionary<string, List<IEffectOp>>
            {
                ["use"] = [
                    new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)),
                    new ApplyBuffOp(SourceSelector.Instance, "reserved_instance", new StaticAmount(1), "permanent", "reserved_instance"),
                ],
                ["skip"] = [],
            })
        );

        // SH-0008 SHE Storage - えすす: Versioning (return destroyed SHE backend to hand)
        r.RegisterComposed("SH-0008", TriggerType.OnDestroy,
            new GuardFactionOp(GameConstants.FactionSHE),
            AddToHandOp.Instance,
            SetCancelActionOp.Instance
        );

        // SH-0009 SHE DB - ダイノ: On-Demand (pay 400, double Yield this turn)
        r.RegisterComposed("SH-0009", TriggerType.Activate,
            new RequireBudgetOp(400),
            new LoseBudgetOp(PlayerRef.Self, new StaticAmount(400)),
            new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, SourceYieldAmount.Instance, "this_turn", "on_demand")
        );

        // SH-0010 SHE Cache - メリーモ: Cache Engine choice on deploy
        r.RegisterComposed("SH-0010", TriggerType.Deploy,
            new BranchOnChoiceOp(new Dictionary<string, List<IEffectOp>>
            {
                ["memcached"] = [new GainBudgetOp(PlayerRef.Self, new StaticAmount(400))],
                ["redis"] = [new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, new StaticAmount(200), "permanent", "cache_engine_redis")],
            })
        );

        // SH-0013 SHE Guard: Reveal 1 opponent reactive
        r.RegisterComposed("SH-0013", TriggerType.Activate, new RevealReactiveOp());

        // SH-0014 SHE Firewall: Block DDoS / Data Breach
        r.RegisterComposed("SH-0014", TriggerType.Reactive, SetCancelActionOp.Instance);

        // SH-0017 SHE Keys: Block Data Breach (attachment)
        r.RegisterComposed("SH-0017", TriggerType.Reactive, SetCancelActionOp.Instance);

        // SH-0018 SHE Formation: Search SHE Component
        r.RegisterComposed("SH-0018", TriggerType.Activate, new SearchRepoOp { Faction = GameConstants.FactionSHE });

        // SH-0019 SHE Marketplace: Budget +600 if 3+ SHE on field
        r.RegisterComposed("SH-0019", TriggerType.Activate,
            new RequireFactionCountOp(GameConstants.FactionSHE, 3),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(600))
        );

        // SH-0020 SHE Cost Explorer: All Deploy Cost -200 this turn
        r.RegisterComposed("SH-0020", TriggerType.Activate,
            new ApplyBuffOp(new AllOwnSelector(), "deploy_discount", new StaticAmount(200), "this_turn", "cost_explorer")
        );

        // SH-0021 Smile Recovery: AV +500 when SHE resource ≤ 400 AV
        r.RegisterComposed("SH-0021", TriggerType.Reactive,
            new GuardFactionOp(GameConstants.FactionSHE),
            new GuardTargetAVOp(400),
            new HealDamageOp(TargetSelector.Instance, new StaticAmount(500)),
            SetCancelActionOp.Instance
        );

        // SH-0022 SHE Ecosystem: SHE frontend TP +200 this turn
        r.RegisterComposed("SH-0022", TriggerType.Activate,
            new RequireFactionCountOp(GameConstants.FactionSHE, 3),
            new ApplyBuffOp(
                new AllOwnSelector { Zone = GameConstants.ZoneFrontend, Faction = GameConstants.FactionSHE },
                EffectTypes.BuffTP, new StaticAmount(200), "this_turn", "she_ecosystem")
        );

        // SH-0023 SHE Smile Horizon Express: Deploy from hand at cost 0
        r.RegisterComposed("SH-0023", TriggerType.Activate, new DeployFromHandOp());
    }

    // ========================
    // Tenki (Tenki Cloud)
    // ========================
    private static void RegisterTenki(EffectRegistry r)
    {
        // TK-0008 Tenki DB - ハヤテ: Failover Group (Yield +400 when ally Tenki DB destroyed)
        r.RegisterComposed("TK-0008", TriggerType.OnDestroy,
            new GuardFactionOp(GameConstants.FactionTenki, "data"),
            GuardNotSelfOp.Instance,
            new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, new StaticAmount(400), "until_next_own_turn_end", "failover_group")
        );

        // TK-0010 Tenki DB - 百花の天穹<コスモ>: deploy another from repo
        r.RegisterComposed("TK-0010", TriggerType.Deploy,
            new DeployFromRepoOp { Filter = EffectHelpers.CardIdFilter("TK-0010") }
        );

        // TK-0025 Tenki Sentinel: Reveal reactive + Incident damage -300
        r.RegisterComposed("TK-0025", TriggerType.Activate,
            new RevealReactiveOp(),
            new ApplyBuffOp(new AllOwnSelector(), "incident_reduction", new StaticAmount(300), "this_turn", "sentinel")
        );

        // TK-0014 Tenki Protection: Block DDoS / Data Breach
        r.RegisterComposed("TK-0014", TriggerType.Reactive, SetCancelActionOp.Instance);

        // TK-0015 Tenki Backup: Revival on destroy
        r.RegisterComposed("TK-0015", TriggerType.OnDestroy,
            new ApplyBuffOp(TargetSelector.Instance, "pending_revival", HalfMaxAVAmount.Instance, "next_turn", "tenki_backup")
        );

        // TK-0017 Tenki Key Vault: Block Data Breach
        r.RegisterComposed("TK-0017", TriggerType.Reactive, SetCancelActionOp.Instance);

        // TK-0018 Tenki Site Recovery: Deploy copy from repo on destroy
        r.RegisterComposed("TK-0018", TriggerType.OnDestroy, new DeployFromRepoSameCardOp(200));

        // TK-0020 Tenki Template: Search Tenki Component
        r.RegisterComposed("TK-0020", TriggerType.Activate, new SearchRepoOp { Faction = GameConstants.FactionTenki });

        // TK-0021 Tenki Migration: Return Component from trash to hand
        r.RegisterComposed("TK-0021", TriggerType.Activate, new TrashToHandOp());

        // TK-0022 Tenki Policy: Block opponent Incidents this turn
        r.RegisterComposed("TK-0022", TriggerType.Activate,
            new ApplyBuffOp(
                new AllOpponentSelector { Zone = GameConstants.ZoneFrontend },
                "incident_block", new StaticAmount(1), "this_turn", "tenki_policy")
        );

        // TK-0023 Tenki Defender: Nullify Incident
        r.RegisterComposed("TK-0023", TriggerType.Reactive, SetCancelActionOp.Instance);

        // TK-0024 Tenki Traffic: Deploy Tenki Compute from hand when frontend destroyed
        r.RegisterComposed("TK-0024", TriggerType.Reactive,
            new DeployFromHandOp
            {
                Filter = EffectHelpers.FactionAndTypeFilter(GameConstants.FactionTenki, ct => ct is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl),
            }
        );

        // NT-0027 Windy 障害: Tenki frontends cannot attack this turn
        r.RegisterComposed("NT-0027", TriggerType.Reactive,
            new ApplyBuffOp(
                new AllOwnSelector { Zone = GameConstants.ZoneFrontend, Faction = GameConstants.FactionTenki },
                "cannot_attack", new StaticAmount(1), "this_turn", "madonosoft_failer")
        );

        // NT-0028 Windy Update: Deal 400 damage to all Tenki cards
        r.RegisterComposed("NT-0028", TriggerType.Reactive,
            new DealDamageOp(new AllOwnSelector { Faction = GameConstants.FactionTenki }, new StaticAmount(400)),
            new DestroyCheckOp(PlayerRef.Self)
        );
    }

    // ========================
    // Sugar
    // ========================
    private static void RegisterSugar(EffectRegistry r)
    {
        // SL-0004 Sugar Orchestrator: Autopilot (auto scale to medium on deploy)
        r.RegisterComposed("SL-0004", TriggerType.Deploy, new ScaleToRankOp("medium"));

        // SL-0006 Sugar AI - バター X: Training Pipeline (absorb 400 insight on attack)
        r.RegisterComposed("SL-0006", TriggerType.OnAttack, new AbsorbInsightOp(new StaticAmount(400)));

        // SL-0007 Sugar AI - Dr. テンソルベ: absorb 600 insight on attack + cascade failure on destroy
        r.RegisterComposed("SL-0007", TriggerType.OnAttack, new AbsorbInsightOp(new StaticAmount(600)));
        r.RegisterComposed("SL-0007", TriggerType.OnDestroy,
            new DealDamageOp(new AllOwnSelector { Zone = GameConstants.ZoneBackend }, new StaticAmount(400)),
            new DestroyCheckOp(PlayerRef.Self)
        );

        // SL-0010 Sugar DB - ファイアトーストア: Realtime Sync (+200 insight on Sugar deploy)
        r.RegisterComposed("SL-0010", TriggerType.Deploy, new GainInsightOp(new StaticAmount(200)));

        // SL-0011 Sugar Datawarehouse: Streaming Insert (+200 insight on Sugar frontend attack)
        r.RegisterComposed("SL-0011", TriggerType.OnAttack, new GainInsightOp(new StaticAmount(200)));

        // SL-0016 Sugar ビッグ・アイスクエリム Analytics: Absorb 300 insight per Yield phase
        r.RegisterComposed("SL-0016", TriggerType.Passive, new AbsorbInsightOp(new StaticAmount(300)));

        // SL-0018 Sugar ぱくぱくサブレ: Message Fanout (chain attack bonus 200 damage)
        r.RegisterComposed("SL-0018", TriggerType.OnAttack,
            new CustomFnTaggedOp(PubSubChainDamage)
            {
                Categories = [EffectCategory.SingleDamage],
                Target = EffectTargetType.Self,
            }
        );

        // SL-0021 Sugar Deployment: Search Sugar Component
        r.RegisterComposed("SL-0021", TriggerType.Activate, new SearchRepoOp { Faction = GameConstants.FactionSugar });

        // SL-0022 Sugar バター X Batch: Double 1 frontend Compute's TP this turn
        r.RegisterComposed("SL-0022", TriggerType.Activate,
            new CustomFnTaggedOp(VeloceBatchBuff)
            {
                Categories = [EffectCategory.Buff],
                Target = EffectTargetType.Choice,
                Zone = GameConstants.ZoneFrontend,
            }
        );

        // SL-0023 Sugar Knowledge: Absorb insight with backend bonus
        r.RegisterComposed("SL-0023", TriggerType.Activate,
            new AbsorbInsightOp(new BackendScaledAmount(400, 200, 600))
        );

        // SL-0024 Sugar Error Budget: Survive destruction with AV 200
        r.RegisterComposed("SL-0024", TriggerType.Reactive,
            new GuardFactionOp(GameConstants.FactionSugar),
            new SurviveDestructionOp(200)
        );

        // SL-0012 Sugar Cache: Cache Engine choice
        r.RegisterComposed("SL-0012", TriggerType.Deploy,
            new BranchOnChoiceOp(new Dictionary<string, List<IEffectOp>>
            {
                ["memcached"] = [new GainBudgetOp(PlayerRef.Self, new StaticAmount(400))],
                ["redis"] = [new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, new StaticAmount(200), "permanent", "cache_engine_redis")],
            })
        );
    }

    // ========================
    // Tuners
    // ========================
    private static void RegisterTuners(EffectRegistry r)
    {
        // TN-0002 Tuners Bare Metal: Self-damage on attack
        r.RegisterComposed("TN-0002", TriggerType.OnAttack,
            new DealDamageOp(SourceSelector.Instance, new StaticAmount(300))
        );

        // TN-0012 Tuners Guard: Reveal reactive + conditional Incident reduction
        r.RegisterComposed("TN-0012", TriggerType.Activate,
            new RevealReactiveOp(),
            new IfConditionOp(
                octx =>
                {
                    int count = EffectHelpers.CountFactionCards(octx.MyField, GameConstants.FactionTuners, octx.CardCache);
                    return count >= 3;
                },
                [new ApplyBuffOp(new AllOwnSelector(), "incident_reduction", new StaticAmount(200), "this_turn", "tuners_guard")]
            )
        );

        // TN-0013 Tuners WAF: Block DDoS / Data Breach
        r.RegisterComposed("TN-0013", TriggerType.Reactive, SetCancelActionOp.Instance);

        // TN-0014 Tuners ノーツガード: Deploy Tuners DB from repo on destroy
        r.RegisterComposed("TN-0014", TriggerType.OnDestroy,
            new DeployFromRepoOp
            {
                Filter = EffectHelpers.FactionAndTypeFilter(GameConstants.FactionTuners, EffectHelpers.IsDBType),
                OverrideAV = 200,
            }
        );

        // TN-0017 Tuners License: Full AV restore on Tuners DB
        r.RegisterComposed("TN-0017", TriggerType.Activate,
            new FullHealOp(
                new ByChoiceSelector
                {
                    Zone = GameConstants.ZoneBackend,
                    Faction = GameConstants.FactionTuners,
                    CardType = "data",
                    Owner = "self",
                })
        );

        // TN-0018 Tuners Failback: Deploy Tuners DB from hand when Tuners DB destroyed
        r.RegisterComposed("TN-0018", TriggerType.Reactive,
            new DeployFromHandOp
            {
                Filter = EffectHelpers.FactionAndTypeFilter(GameConstants.FactionTuners, EffectHelpers.IsDBType),
            }
        );
    }

    // ========================
    // Neutral
    // ========================
    private static void RegisterNeutral(EffectRegistry r)
    {
        // NT-0007 Cloud Engineer: Draw 1
        r.RegisterComposed("NT-0007", TriggerType.Activate, new DrawCardsOp(1));

        // NT-0008 Cloud Architect: Draw 2
        r.RegisterComposed("NT-0008", TriggerType.Activate, new DrawCardsOp(2));

        // NT-0009 寺リフォーム: Search any Component
        r.RegisterComposed("NT-0009", TriggerType.Activate, new SearchRepoOp());

        // NT-0010 プロジェクトマネージャー: Budget +400
        r.RegisterComposed("NT-0010", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(400))
        );

        // NT-0011 クラウドファンディング: Budget +1000
        r.RegisterComposed("NT-0011", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(1000))
        );

        // NT-0012 Open Source Migration: Destroy opponent Platform
        r.RegisterComposed("NT-0012", TriggerType.Activate, new DestroyPlatformOp());

        // NT-0026 Venture Capital: Budget +900 if ≤ 1000
        r.RegisterComposed("NT-0026", TriggerType.Activate,
            new RequireMaxBudgetOp(1000),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(900))
        );
    }

    // ========================
    // Incidents
    // ========================
    private static void RegisterIncidents(EffectRegistry r)
    {
        // NT-0013 DDoS Attack: 500 damage to 1 frontend
        r.RegisterComposed("NT-0013", TriggerType.Activate,
            new IncidentDamageOp(
                new ByChoiceSelector { Zone = GameConstants.ZoneFrontend, Owner = "opponent" },
                new StaticAmount(500)),
            new DestroyCheckOp(PlayerRef.Opponent)
        );

        // NT-0014 Data Breach: 600 damage to 1 backend, Budget -300
        r.RegisterComposed("NT-0014", TriggerType.Activate,
            new IncidentDamageOp(
                new ByChoiceSelector { Zone = GameConstants.ZoneBackend, Owner = "opponent" },
                new StaticAmount(600), new StaticAmount(300)),
            new DestroyCheckOp(PlayerRef.Opponent)
        );

        // NT-0015 Config Error: TP → 0 until next turn end
        r.RegisterComposed("NT-0015", TriggerType.Activate,
            new CustomFnTaggedOp(ConfigErrorDebuff)
            {
                Categories = [EffectCategory.Debuff],
                Target = EffectTargetType.Choice,
                Zone = GameConstants.ZoneFrontend,
            }
        );

        // NT-0016 Data Scraping: Absorb 600 insight (fails if no backend)
        r.RegisterComposed("NT-0016", TriggerType.Activate,
            RequireOpponentBackendOp.Instance,
            new AbsorbInsightOp(new StaticAmount(600))
        );

        // NT-0017 Crawler Bot: Absorb 300 insight
        r.RegisterComposed("NT-0017", TriggerType.Activate, new AbsorbInsightOp(new StaticAmount(300)));

        // NT-0018 Region Outage: 500 damage to all resources
        r.RegisterComposed("NT-0018", TriggerType.Activate,
            new IncidentDamageOp(new AllOpponentSelector(), new StaticAmount(500)),
            new DestroyCheckOp(PlayerRef.Opponent)
        );

        // NT-0019 Crypto Mining: 400 damage to 1 frontend, self Budget +500
        r.RegisterComposed("NT-0019", TriggerType.Activate,
            new IncidentDamageOp(
                new ByChoiceSelector { Zone = GameConstants.ZoneFrontend, Owner = "opponent" },
                new StaticAmount(400)),
            new DestroyCheckOp(PlayerRef.Opponent),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500))
        );

        // NT-0020 Ransomware: Disable 1 Component until next turn end
        r.RegisterComposed("NT-0020", TriggerType.Activate,
            new ApplyBuffOp(
                new ByChoiceSelector { Owner = "opponent" },
                "ransomware", new StaticAmount(1), "until_next_turn_end", "ransomware")
        );

        // NT-0021 Compliance Audit: Budget -400 to opponent; if opponent has no ISMS(NT-0005) or SOC2(NT-0006) platform, additional -400
        r.RegisterComposed("NT-0021", TriggerType.Activate,
            new RequireBudgetOp(200),
            new LoseBudgetOp(PlayerRef.Self, new StaticAmount(200)),
            new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(400)),
            new IfConditionOp(
                octx => !HasCompliancePlatform(octx.OpponentField, octx.CardCache),
                [new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(400))]
            )
        );

        // NT-0022 レートリミット: cannot_operate on high-TP Compute/AI_ML
        r.RegisterComposed("NT-0022", TriggerType.OnEnemyDeploy,
            new CustomFnTaggedOp(RateLimiterFire)
            {
                Categories = [EffectCategory.Debuff],
                Target = EffectTargetType.AllOpp,
            }
        );

        // NT-0034 スロットリング: destroy opponent's 3rd resource deployed in a turn
        r.RegisterComposed("NT-0034", TriggerType.OnEnemyDeploy,
            new CustomFnTaggedOp(ThrottlingFire)
            {
                Categories = [EffectCategory.CancelAction],
                Target = EffectTargetType.AllOpp,
            }
        );
    }

    // ========================
    // Reactives
    // ========================
    private static void RegisterReactives(EffectRegistry r)
    {
        // NT-0023 フェイルオーバー: Deploy same type from hand when frontend destroyed
        r.RegisterComposed("NT-0023", TriggerType.Reactive,
            new CustomFnTaggedOp(FailoverDeploy)
            {
                Categories = [EffectCategory.DeployFree],
            }
        );

        // NT-0024 Chaos Engineering: Redirect attack to opponent's frontend
        r.RegisterComposed("NT-0024", TriggerType.Reactive,
            new CustomFnTaggedOp(ChaosRedirect)
            {
                Categories = [EffectCategory.CancelAction],
            },
            SetCancelActionOp.Instance
        );
    }

    // ========================
    // Custom function implementations
    // ========================

    /// <summary>
    /// Find another Sugar Compute on own frontend, deal 200 bonus damage to target.
    /// </summary>
    private static void PubSubChainDamage(OpContext octx)
    {
        if (octx.Target is null)
        {
            return;
        }

        var field = octx.MyField;
        var ally = field.Frontend
            .Where(r => r.InstanceID != octx.Source?.InstanceID)
            .FirstOrDefault(r =>
            {
                var card = octx.CardCache.Get(r.CardID);
                return card is not null && card.Faction == GameConstants.FactionSugar && card.IsComputeType;
            });
        if (ally is not null)
        {
            octx.Target.Damage += 200;
        }
    }

    /// <summary>
    /// Double selected frontend Compute's TP this turn.
    /// </summary>
    private static void VeloceBatchBuff(OpContext octx)
    {
        var instanceId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (instanceId is null)
        {
            throw new GameRuleException("No instance chosen");
        }

        var field = octx.MyField;
        var target = FieldHelpers.FindResourceByID(field, instanceId);
        if (target is null || FieldHelpers.FindResourceZone(field, instanceId) != Zone.Frontend)
        {
            throw new GameRuleException("Target must be a frontend resource");
        }

        var card = octx.CardCache.Get(target.CardID);
        if (card is null || !card.IsComputeType)
        {
            throw new GameRuleException("Target must be a Compute type");
        }

        if (target.CurrentTP is { } tp)
        {
            target.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = EffectTypes.BuffTP,
                Value = tp,
                Duration = "this_turn",
                SourceID = "veloce_batch",
            });
        }
    }

    /// <summary>
    /// Set opponent frontend TP to 0 until next turn end.
    /// </summary>
    private static void ConfigErrorDebuff(OpContext octx)
    {
        var instanceId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (instanceId is null)
        {
            throw new GameRuleException("No instance chosen");
        }

        var oppField = octx.OpponentField;
        var target = FieldHelpers.FindResourceByID(oppField, instanceId);
        if (target is null || FieldHelpers.FindResourceZone(oppField, instanceId) != Zone.Frontend)
        {
            throw new GameRuleException("Target must be opponent's frontend");
        }

        target.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.TPSuppressed,
            Value = 1,
            Duration = "until_next_turn_end",
            SourceID = "config_error",
        });
    }

    /// <summary>
    /// Validate choice card type matches destroyed target's type, deploy from hand.
    /// </summary>
    private static void FailoverDeploy(OpContext octx)
    {
        if (octx.Target is null)
        {
            throw new GameRuleException("No target");
        }

        string? choiceCardId = octx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();

        if (choiceCardId is null)
        {
            throw new GameRuleException("No card chosen");
        }

        var targetCard = octx.CardCache.Get(octx.Target.CardID);
        var choiceCard = octx.CardCache.Get(choiceCardId);
        if (targetCard is null || choiceCard is null)
        {
            throw new GameRuleException("Card not found");
        }
        if (choiceCard.CardType != targetCard.CardType)
        {
            throw new GameRuleException("Must deploy same type as destroyed card");
        }

        var field = octx.GetField(octx.PlayerNum);
        ResourceHelpers.DeployFromHand(octx.State, octx.PlayerNum, field, choiceCardId, octx.CardCache);
    }

    /// <summary>
    /// When opponent deploys a Compute/AI_ML card with TP ≥ 900, apply cannot_operate.
    /// </summary>
    private static void RateLimiterFire(OpContext octx)
    {
        var target = octx.Target;
        if (target is null)
        {
            return;
        }

        var targetCard = octx.CardCache.Get(target.CardID);
        if (targetCard is null)
        {
            return;
        }
        if (!targetCard.IsComputeType && targetCard.CardType != CardTypes.AiMl)
        {
            return;
        }
        if (target.MaxTP is null || target.MaxTP < 900)
        {
            return;
        }

        target.TemporaryEffects.Add(new TemporaryEffect
        {
            EffectType = EffectTypes.CannotOperate,
            Value = 1,
            Duration = "this_turn",
            SourceID = "rate_limiter",
        });

        // Update the resource on the deployer's field
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
    private static void ThrottlingFire(OpContext octx)
    {
        if (octx.SupSource is { EffectUsedThisTurn: true })
        {
            throw new GameRuleException("Already used this turn");
        }

        long deployerNum = octx.State.OpponentOf(octx.PlayerNum);
        var field = octx.GetField(deployerNum);
        int count = CountDeployedThisTurn(field, octx.State.CurrentTurn);
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
    private static void ChaosRedirect(OpContext octx)
    {
        var instanceId = octx.ChoiceData?.GetValueOrDefault("instanceId")?.ToString();
        if (instanceId is null)
        {
            throw new GameRuleException("No redirect target chosen");
        }

        var oppField = octx.OpponentField;
        if (FieldHelpers.FindResourceZone(oppField, instanceId) != Zone.Frontend)
        {
            throw new GameRuleException("Redirect target must be opponent's frontend");
        }
    }

    /// <summary>
    /// Count resources deployed on a specific turn.
    /// </summary>
    private static int CountDeployedThisTurn(Field field, long currentTurn)
    {
        return FieldHelpers.AllResources(field).Count(r => r.DeployedOnTurn == currentTurn);
    }

    /// <summary>
    /// Returns true if the field contains an active (face-up, fully deployed) ISMS (NT-0005) or SOC2 (NT-0006) platform.
    /// </summary>
    private static bool HasCompliancePlatform(Field field, ICardCache _)
    {
        return field.Support.Any(s =>
            s.FaceUp
            && s.DeployingTurnsLeft <= 0
            && (s.CardID == "NT-0005" || s.CardID == "NT-0006"));
    }
}

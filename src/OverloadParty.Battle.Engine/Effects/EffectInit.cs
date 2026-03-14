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
        // #7 SHE RDB - アデリース: Reserved Instance (optional deploy cost -200)
        r.RegisterComposed(7, TriggerType.Deploy,
            new BranchOnChoiceOp(new Dictionary<string, List<IEffectOp>>
            {
                ["use"] = [
                    new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)),
                    new ApplyBuffOp(SourceSelector.Instance, "reserved_instance", new StaticAmount(1), "permanent", "reserved_instance"),
                ],
                ["skip"] = [],
            })
        );

        // #9 SHE Storage - えすす: Versioning (return destroyed SHE backend to hand)
        r.RegisterComposed(9, TriggerType.OnDestroy,
            new GuardFactionOp(GameConstants.FactionSHE),
            new AddToHandOp(TargetCardIDAmount.Instance),
            SetCancelActionOp.Instance
        );

        // #10 SHE DB - ダイノ: On-Demand (pay 400, double Yield this turn)
        r.RegisterComposed(10, TriggerType.Activate,
            new RequireBudgetOp(400),
            new LoseBudgetOp(PlayerRef.Self, new StaticAmount(400)),
            new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, SourceYieldAmount.Instance, "this_turn", "on_demand")
        );

        // #11 SHE Cache - メリーモ: Cache Engine choice on deploy
        r.RegisterComposed(11, TriggerType.Deploy,
            new BranchOnChoiceOp(new Dictionary<string, List<IEffectOp>>
            {
                ["memcached"] = [new GainBudgetOp(PlayerRef.Self, new StaticAmount(400))],
                ["redis"] = [new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, new StaticAmount(200), "permanent", "cache_engine_redis")],
            })
        );

        // #14 SHE Guard: Reveal 1 opponent reactive
        r.RegisterComposed(14, TriggerType.Activate, new RevealReactiveOp());

        // #15 SHE Firewall: Block DDoS / Data Breach
        r.RegisterComposed(15, TriggerType.Reactive, SetCancelActionOp.Instance);

        // #18 SHE Keys: Block Data Breach (attachment)
        r.RegisterComposed(18, TriggerType.Reactive, SetCancelActionOp.Instance);

        // #19 SHE Formation: Search SHE Component
        r.RegisterComposed(19, TriggerType.Activate, new SearchRepoOp { Faction = GameConstants.FactionSHE });

        // #20 SHE Marketplace: Budget +600 if 3+ SHE on field
        r.RegisterComposed(20, TriggerType.Activate,
            new RequireFactionCountOp(GameConstants.FactionSHE, 3),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(600))
        );

        // #21 SHE Cost Explorer: All Deploy Cost -200 this turn
        r.RegisterComposed(21, TriggerType.Activate,
            new ApplyBuffOp(new AllOwnSelector(), "deploy_discount", new StaticAmount(200), "this_turn", "cost_explorer")
        );

        // #22 Smile Recovery: AV +500 when SHE resource ≤ 400 AV
        r.RegisterComposed(22, TriggerType.Reactive,
            new GuardFactionOp(GameConstants.FactionSHE),
            new GuardTargetAVOp(400),
            new HealDamageOp(TargetSelector.Instance, new StaticAmount(500)),
            SetCancelActionOp.Instance
        );

        // #118 SHE Ecosystem: SHE frontend TP +200 this turn
        r.RegisterComposed(118, TriggerType.Activate,
            new RequireFactionCountOp(GameConstants.FactionSHE, 3),
            new ApplyBuffOp(
                new AllOwnSelector { Zone = GameConstants.ZoneFrontend, Faction = GameConstants.FactionSHE },
                EffectTypes.BuffTP, new StaticAmount(200), "this_turn", "she_ecosystem")
        );

        // #121 SHE Smile Horizon Express: Deploy from hand at cost 0
        r.RegisterComposed(121, TriggerType.Activate, new DeployFromHandOp());
    }

    // ========================
    // Tenki (Tenki Cloud)
    // ========================
    private static void RegisterTenki(EffectRegistry r)
    {
        // #30 Tenki DB - ハヤテ: Failover Group (Yield +400 when ally Tenki DB destroyed)
        r.RegisterComposed(30, TriggerType.OnDestroy,
            new GuardFactionOp(GameConstants.FactionTenki, "data"),
            GuardNotSelfOp.Instance,
            new ApplyBuffOp(SourceSelector.Instance, EffectTypes.BuffYield, new StaticAmount(400), "until_next_own_turn_end", "failover_group")
        );

        // #32 Tenki DB - 百花の天穹<コスモ>: deploy another from repo
        r.RegisterComposed(32, TriggerType.Deploy,
            new DeployFromRepoOp { Filter = EffectHelpers.CardNoFilter(32) }
        );

        // #36 Tenki Sentinel: Reveal reactive + Incident damage -300
        r.RegisterComposed(36, TriggerType.Activate,
            new RevealReactiveOp(),
            new ApplyBuffOp(new AllOwnSelector(), "incident_reduction", new StaticAmount(300), "this_turn", "sentinel")
        );

        // #37 Tenki Protection: Block DDoS / Data Breach
        r.RegisterComposed(37, TriggerType.Reactive, SetCancelActionOp.Instance);

        // #38 Tenki Backup: Revival on destroy
        r.RegisterComposed(38, TriggerType.OnDestroy,
            new ApplyBuffOp(TargetSelector.Instance, "pending_revival", HalfMaxAVAmount.Instance, "next_turn", "tenki_backup")
        );

        // #40 Tenki Key Vault: Block Data Breach
        r.RegisterComposed(40, TriggerType.Reactive, SetCancelActionOp.Instance);

        // #41 Tenki Site Recovery: Deploy copy from repo on destroy
        r.RegisterComposed(41, TriggerType.OnDestroy, new DeployFromRepoSameCardOp(200));

        // #42 Tenki Template: Search Tenki Component
        r.RegisterComposed(42, TriggerType.Activate, new SearchRepoOp { Faction = GameConstants.FactionTenki });

        // #43 Tenki Migration: Return Component from trash to hand
        r.RegisterComposed(43, TriggerType.Activate, new TrashToHandOp());

        // #44 Tenki Policy: Block opponent Incidents this turn
        r.RegisterComposed(44, TriggerType.Activate,
            new ApplyBuffOp(
                new AllOpponentSelector { Zone = GameConstants.ZoneFrontend },
                "incident_block", new StaticAmount(1), "this_turn", "tenki_policy")
        );

        // #45 Tenki Defender: Nullify Incident
        r.RegisterComposed(45, TriggerType.Reactive, SetCancelActionOp.Instance);

        // #46 Tenki Traffic: Deploy Tenki Compute from hand when frontend destroyed
        r.RegisterComposed(46, TriggerType.Reactive,
            new DeployFromHandOp
            {
                Filter = EffectHelpers.FactionAndTypeFilter(GameConstants.FactionTenki, ct => ct is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl),
            }
        );

        // #122 Windy 障害: Tenki frontends cannot attack this turn
        r.RegisterComposed(122, TriggerType.Reactive,
            new ApplyBuffOp(
                new AllOwnSelector { Zone = GameConstants.ZoneFrontend, Faction = GameConstants.FactionTenki },
                "cannot_attack", new StaticAmount(1), "this_turn", "madonosoft_failer")
        );

        // #123 Windy Update: Deal 400 damage to all Tenki cards
        r.RegisterComposed(123, TriggerType.Reactive,
            new DealDamageOp(new AllOwnSelector { Faction = GameConstants.FactionTenki }, new StaticAmount(400)),
            new DestroyCheckOp(PlayerRef.Self)
        );
    }

    // ========================
    // Sugar
    // ========================
    private static void RegisterSugar(EffectRegistry r)
    {
        // #50 Sugar Orchestrator: Autopilot (auto scale to medium on deploy)
        r.RegisterComposed(50, TriggerType.Deploy, new ScaleToRankOp("medium"));

        // #51 Sugar AI - バター X: Training Pipeline (absorb 400 insight on attack)
        r.RegisterComposed(51, TriggerType.OnAttack, new AbsorbInsightOp(new StaticAmount(400)));

        // #52 Sugar AI - Dr. テンソルベ: absorb 600 insight on attack + cascade failure on destroy
        r.RegisterComposed(52, TriggerType.OnAttack, new AbsorbInsightOp(new StaticAmount(600)));
        r.RegisterComposed(52, TriggerType.OnDestroy,
            new DealDamageOp(new AllOwnSelector { Zone = GameConstants.ZoneBackend }, new StaticAmount(400)),
            new DestroyCheckOp(PlayerRef.Self)
        );

        // #57 Sugar DB - ファイアトーストア: Realtime Sync (+200 insight on Sugar deploy)
        r.RegisterComposed(57, TriggerType.Deploy, new GainInsightOp(new StaticAmount(200)));

        // #58 Sugar Datawarehouse: Streaming Insert (+200 insight on Sugar frontend attack)
        r.RegisterComposed(58, TriggerType.OnAttack, new GainInsightOp(new StaticAmount(200)));

        // #61 Sugar ビッグ・アイスクエリム Analytics: Absorb 300 insight per Yield phase
        r.RegisterComposed(61, TriggerType.Passive, new AbsorbInsightOp(new StaticAmount(300)));

        // #63 Sugar ぱくぱくサブレ: Message Fanout (chain attack bonus 200 damage)
        r.RegisterComposed(63, TriggerType.OnAttack,
            new CustomFnTaggedOp(PubSubChainDamage)
            {
                Categories = [EffectCategory.SingleDamage],
                Target = EffectTargetType.Self,
            }
        );

        // #65 Sugar Deployment: Search Sugar Component
        r.RegisterComposed(65, TriggerType.Activate, new SearchRepoOp { Faction = GameConstants.FactionSugar });

        // #66 Sugar バター X Batch: Double 1 frontend Compute's TP this turn
        r.RegisterComposed(66, TriggerType.Activate,
            new CustomFnTaggedOp(VeloceBatchBuff)
            {
                Categories = [EffectCategory.Buff],
                Target = EffectTargetType.Choice,
                Zone = GameConstants.ZoneFrontend,
            }
        );

        // #67 Sugar Knowledge: Absorb insight with backend bonus
        r.RegisterComposed(67, TriggerType.Activate,
            new AbsorbInsightOp(new BackendScaledAmount(400, 200, 600))
        );

        // #68 Sugar Error Budget: Survive destruction with AV 200
        r.RegisterComposed(68, TriggerType.Reactive,
            new GuardFactionOp(GameConstants.FactionSugar),
            new SurviveDestructionOp(200)
        );

        // #125 Sugar Cache: Cache Engine choice
        r.RegisterComposed(125, TriggerType.Deploy,
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
        // #72 Tuners Bare Metal: Self-damage on attack
        r.RegisterComposed(72, TriggerType.OnAttack,
            new DealDamageOp(SourceSelector.Instance, new StaticAmount(300))
        );

        // #83 Tuners Guard: Reveal reactive + conditional Incident reduction
        r.RegisterComposed(83, TriggerType.Activate,
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

        // #84 Tuners WAF: Block DDoS / Data Breach
        r.RegisterComposed(84, TriggerType.Reactive, SetCancelActionOp.Instance);

        // #85 Tuners ノーツガード: Deploy Tuners DB from repo on destroy
        r.RegisterComposed(85, TriggerType.OnDestroy,
            new DeployFromRepoOp
            {
                Filter = EffectHelpers.FactionAndTypeFilter(GameConstants.FactionTuners, EffectHelpers.IsDBType),
                OverrideAV = 200,
            }
        );

        // #89 Tuners License: Full AV restore on Tuners DB
        r.RegisterComposed(89, TriggerType.Activate,
            new FullHealOp(
                new ByChoiceSelector
                {
                    Zone = GameConstants.ZoneBackend,
                    Faction = GameConstants.FactionTuners,
                    CardType = "data",
                    Owner = "self",
                })
        );

        // #90 Tuners Failback: Deploy Tuners DB from hand when Tuners DB destroyed
        r.RegisterComposed(90, TriggerType.Reactive,
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
        // #98 Cloud Engineer: Draw 1
        r.RegisterComposed(98, TriggerType.Activate, new DrawCardsOp(1));

        // #99 Cloud Architect: Draw 2
        r.RegisterComposed(99, TriggerType.Activate, new DrawCardsOp(2));

        // #100 寺リフォーム: Search any Component
        r.RegisterComposed(100, TriggerType.Activate, new SearchRepoOp());

        // #101 プロジェクトマネージャー: Budget +400
        r.RegisterComposed(101, TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(400))
        );

        // #102 クラウドファンディング: Budget +1000
        r.RegisterComposed(102, TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(1000))
        );

        // #103 Open Source Migration: Destroy opponent Platform
        r.RegisterComposed(103, TriggerType.Activate, new DestroyPlatformOp());

        // #120 Venture Capital: Budget +900 if ≤ 1000
        r.RegisterComposed(120, TriggerType.Activate,
            new RequireMaxBudgetOp(1000),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(900))
        );
    }

    // ========================
    // Incidents
    // ========================
    private static void RegisterIncidents(EffectRegistry r)
    {
        // #104 DDoS Attack: 500 damage to 1 frontend
        r.RegisterComposed(104, TriggerType.Activate,
            new IncidentDamageOp(
                new ByChoiceSelector { Zone = GameConstants.ZoneFrontend, Owner = "opponent" },
                new StaticAmount(500)),
            new DestroyCheckOp(PlayerRef.Opponent)
        );

        // #105 Data Breach: 600 damage to 1 backend, Budget -300
        r.RegisterComposed(105, TriggerType.Activate,
            new IncidentDamageOp(
                new ByChoiceSelector { Zone = GameConstants.ZoneBackend, Owner = "opponent" },
                new StaticAmount(600), new StaticAmount(300)),
            new DestroyCheckOp(PlayerRef.Opponent)
        );

        // #106 Config Error: TP → 0 until next turn end
        r.RegisterComposed(106, TriggerType.Activate,
            new CustomFnTaggedOp(ConfigErrorDebuff)
            {
                Categories = [EffectCategory.Debuff],
                Target = EffectTargetType.Choice,
                Zone = GameConstants.ZoneFrontend,
            }
        );

        // #107 Data Scraping: Absorb 600 insight (fails if no backend)
        r.RegisterComposed(107, TriggerType.Activate,
            RequireOpponentBackendOp.Instance,
            new AbsorbInsightOp(new StaticAmount(600))
        );

        // #108 Crawler Bot: Absorb 300 insight
        r.RegisterComposed(108, TriggerType.Activate, new AbsorbInsightOp(new StaticAmount(300)));

        // #109 Region Outage: 500 damage to all resources
        r.RegisterComposed(109, TriggerType.Activate,
            new IncidentDamageOp(new AllOpponentSelector(), new StaticAmount(500)),
            new DestroyCheckOp(PlayerRef.Opponent)
        );

        // #110 Crypto Mining: 400 damage to 1 frontend, self Budget +500
        r.RegisterComposed(110, TriggerType.Activate,
            new IncidentDamageOp(
                new ByChoiceSelector { Zone = GameConstants.ZoneFrontend, Owner = "opponent" },
                new StaticAmount(400)),
            new DestroyCheckOp(PlayerRef.Opponent),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(500))
        );

        // #111 Ransomware: Disable 1 Component until next turn end
        r.RegisterComposed(111, TriggerType.Activate,
            new ApplyBuffOp(
                new ByChoiceSelector { Owner = "opponent" },
                "ransomware", new StaticAmount(1), "until_next_turn_end", "ransomware")
        );

        // #112 Compliance Audit: Budget -400 to opponent; if opponent has no ISMS(#96) or SOC2(#97) platform, additional -400
        r.RegisterComposed(112, TriggerType.Activate,
            new RequireBudgetOp(200),
            new LoseBudgetOp(PlayerRef.Self, new StaticAmount(200)),
            new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(400)),
            new IfConditionOp(
                octx => !HasCompliancePlatform(octx.OpponentField, octx.CardCache),
                [new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(400))]
            )
        );

        // #113 レートリミット: cannot_operate on high-TP Compute/AI_ML
        r.RegisterComposed(113, TriggerType.OnEnemyDeploy,
            new CustomFnTaggedOp(RateLimiterFire)
            {
                Categories = [EffectCategory.Debuff],
                Target = EffectTargetType.AllOpp,
            }
        );

        // #135 スロットリング: destroy opponent's 3rd resource deployed in a turn
        r.RegisterComposed(135, TriggerType.OnEnemyDeploy,
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
        // #115 フェイルオーバー: Deploy same type from hand when frontend destroyed
        r.RegisterComposed(115, TriggerType.Reactive,
            new CustomFnTaggedOp(FailoverDeploy)
            {
                Categories = [EffectCategory.DeployFree],
            }
        );

        // #117 Chaos Engineering: Redirect attack to opponent's frontend
        r.RegisterComposed(117, TriggerType.Reactive,
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

        long? choiceCardNo = null;
        if (octx.ChoiceData?.TryGetValue("cardNo", out var val) == true)
        {
            if (val is long l)
            {
                choiceCardNo = l;
            }
            else if (val is int i)
            {
                choiceCardNo = i;
            }
            else if (long.TryParse(val?.ToString(), out var parsed))
            {
                choiceCardNo = parsed;
            }
        }

        if (choiceCardNo is null)
        {
            throw new GameRuleException("No card chosen");
        }

        var targetCard = octx.CardCache.Get(octx.Target.CardID);
        var choiceCard = octx.CardCache.Get(choiceCardNo.Value);
        if (targetCard is null || choiceCard is null)
        {
            throw new GameRuleException("Card not found");
        }
        if (choiceCard.CardType != targetCard.CardType)
        {
            throw new GameRuleException("Must deploy same type as destroyed card");
        }

        var field = octx.GetField(octx.PlayerNum);
        ResourceHelpers.DeployFromHand(octx.State, octx.PlayerNum, field, choiceCardNo.Value, octx.CardCache);
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
    /// Returns true if the field contains an active (face-up, fully deployed) ISMS (#96) or SOC2 (#97) platform.
    /// </summary>
    private static bool HasCompliancePlatform(Field field, ICardCache _)
    {
        return field.Support.Any(s =>
            s.FaceUp
            && s.DeployingTurnsLeft <= 0
            && (s.CardID == 96 || s.CardID == 97));
    }
}

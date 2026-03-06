using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Configurable AI that adapts behavior based on faction-specific parameters.
/// Extends StandardAi with faction-tuned deploy ordering and scale-up preferences.
/// </summary>
public class FactionAi : StandardAi
{
    private readonly string _faction;
    private readonly string _instanceFamily;

    private FactionAi(string faction, string instanceFamily, ICardCache cc, EffectRegistry reg)
        : base(cc, reg)
    {
        _faction = faction;
        _instanceFamily = instanceFamily;
    }

    public string Faction => _faction;

    protected override string GetInstanceFamily() => _instanceFamily;

    // ─── Factory methods ────────────────────────────────────────

    public static FactionAi Create(string faction, ICardCache cc, EffectRegistry reg)
    {
        var familyParam = NpcParams.FactionParamsTable.GetValueOrDefault(faction);
        var family = familyParam?.InstanceFamily ?? "M";
        return new FactionAi(faction, family, cc, reg);
    }

    public static INpcStrategy GetFactionAi(string faction, ICardCache cc, EffectRegistry reg)
    {
        return faction switch
        {
            GameConstants.FactionSD or
            GameConstants.FactionTenki or
            GameConstants.FactionSugar or
            GameConstants.FactionTuners => Create(faction, cc, reg),
            _ => new StandardAi(cc, reg),
        };
    }

    // ─── Overrides ──────────────────────────────────────────────

    public override List<NpcAction> DecideMainPhaseActions(
        GameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var field = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var hand = state.GetHand(npcPlayerNum);
        var budget = state.GetBudget(npcPlayerNum);

        var ctx = new DecisionContext(field, oppField, hand, budget, this);
        var usedZones = new HashSet<string>();
        var actions = new List<NpcAction>();

        // 1. Use Strategy/Incident cards from hand
        actions.AddRange(DoImmediateActions(ctx, available, usedZones));

        // 2. Deploy resource cards (with faction ordering)
        if (_faction == GameConstants.FactionTenki)
            actions.AddRange(DoTenkiDeployActions(ctx, available, usedZones));
        else
            actions.AddRange(DoDeployActions(ctx, available, usedZones));

        // 3. Activate field resource/support effects
        actions.AddRange(DecideActivateActions(ctx, available));

        // 4. Scale up with faction-preferred instance family
        actions.AddRange(DoScaleUpActions(available, _instanceFamily));

        // 5. Distribute Yield
        var insightPool = state.GetInsightPool(npcPlayerNum);
        if (insightPool > 0)
            actions.AddRange(DoDistributeYieldActions(available, insightPool));

        // 6. End phase
        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ─── Tenki-specific deploy logic ────────────────────────────

    /// <summary>
    /// Deploys cards with Tenki-specific priorities.
    /// Prioritizes #32 (百花の天穹) first, then #27 (智の解放者) if #32 is on field,
    /// then other Tenki data cards to boost #27's throughput.
    /// </summary>
    private List<NpcAction> DoTenkiDeployActions(
        DecisionContext ctx, List<AvailableAction> available, HashSet<string> usedZones)
    {
        var playActions = ActionFilter.FilterByType(available, WireActionTypes.PlayCard);

        var candidates = new List<(AvailableAction Action, CardDefinition Card, int BasePri, int AozoraPri)>();
        foreach (var a in playActions)
        {
            var card = CardCache.Get(a.CardID);
            if (card is null) continue;
            if (FieldHelpers.IsImmediateType(card.CardType) || card.CardType == CardTypes.Attachment) continue;

            int basePri = card.IsComputeType ? 0 : card.IsDataType ? 1 : 2;

            // Tenki-specific priority
            int aozoraPri = a.CardID switch
            {
                32 => 100, // 百花の天穹<コスモ> - HIGHEST (enables #27 combo)
                27 => HasCardOnField(ctx.Field, 32) ? 90 : 70, // 智の解放者<オープナー>
                30 => CountTenkiDBOnField(ctx.Field) >= 2 ? 85 : 60, // 天気使い DB - ハヤテ
                29 or 31 or 33 => 65, // Other Tenki data cards
                _ => 50,
            };

            candidates.Add((a, card, basePri, aozoraPri));
        }

        // Sort by: aozora priority DESC, then base priority ASC
        candidates.Sort((a, b) =>
        {
            if (a.AozoraPri != b.AozoraPri)
                return b.AozoraPri.CompareTo(a.AozoraPri);
            return a.BasePri.CompareTo(b.BasePri);
        });

        var actions = new List<NpcAction>();
        var deployed = new HashSet<string>();
        foreach (var c in candidates)
        {
            if (deployed.Contains(c.Action.HandInstanceID!)) continue;

            var zone = ActionFilter.PickBestZone(c.Action.ValidZones, c.Card, usedZones);
            if (zone is null) continue;

            var payload = new Dictionary<string, object>
            {
                ["cardInstanceId"] = c.Action.HandInstanceID!,
                ["position"] = ActionFilter.ParseZoneStr(zone)!,
            };
            if (c.Action.ChoiceOptions?.Any() == true)
            {
                var choice = DeployChoiceFor(c.Card.CardNo);
                if (choice == "")
                    choice = c.Action.ChoiceOptions.First();
                payload["choiceData"] = new Dictionary<string, string> { ["option"] = choice };
            }

            actions.Add(new NpcAction { ActionType = WireActionTypes.PlayCard, Data = payload });
            deployed.Add(c.Action.HandInstanceID!);
            usedZones.Add(zone);
        }

        return actions;
    }

    // ─── Tenki helpers ──────────────────────────────────────────

    private static bool HasCardOnField(Field field, long cardNo)
    {
        return FieldHelpers.AllResources(field).Any(r => r.CardID == cardNo);
    }

    private static readonly HashSet<long> TenkiDBCardNos = [29, 30, 31, 32, 33];

    private static int CountTenkiDBOnField(Field field)
    {
        return field.Backend.Count(res => TenkiDBCardNos.Contains(res.CardID));
    }
}

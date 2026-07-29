using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class GuardCheckerTests
{
    /// <summary>GuardChecker のテストに使う、コンピュート系リソースと Database を登録したカードキャッシュを作る。</summary>
    /// <returns>TST-0001 / TST-0002 を登録したキャッシュ。</returns>
    private static TestCardCache Cc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));
        cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800));
        return cc;
    }

    /// <summary>GuardChecker のテストに使う DecisionContext を作る。</summary>
    /// <param name="cc">紐づけるカードキャッシュ。</param>
    /// <param name="field">自分のフィールド。省略時は空。</param>
    /// <param name="oppField">相手のフィールド。省略時は空。</param>
    /// <param name="hand">手札。省略時は空。</param>
    /// <param name="budget">バジェット。</param>
    /// <param name="turn">現在ターン。</param>
    /// <returns>判定対象の DecisionContext。</returns>
    private static DecisionContext MakeCtx(
        TestCardCache cc,
        GD.Field? field = null, GD.OpponentField? oppField = null,
        List<GD.UndeployedCard>? hand = null, long budget = 5000, long turn = 1) =>
        new(
            field ?? TestFactory.MakeWireField(),
            oppField ?? TestFactory.MakeWireOpponentField(),
            hand ?? [],
            budget,
            cc)
        { CurrentTurn = turn };

    [Trait("対象", "条件未指定の判定")]
    public class NullCondition
    {
        [Fact(DisplayName = "条件が null のとき、満たしているとみなす")]
        public void Check_NullCondition_ReturnsTrue()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            GuardChecker.Check(null, ctx, cc).Should().BeTrue();
        }
    }

    [Trait("対象", "ステータス条件の判定")]
    public class StatConditions
    {
        [Fact(DisplayName = "バジェットが 2000 で min 1500 のとき、満たす")]
        public void Check_BudgetMin_Met()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 2000);
            var cond = new ConditionDef { Stat = "budget", Min = 1500 };

            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "バジェットが 1000 で min 1500 のとき、満たさない")]
        public void Check_BudgetMin_NotMet()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 1000);
            var cond = new ConditionDef { Stat = "budget", Min = 1500 };

            GuardChecker.Check(cond, ctx, cc).Should().BeFalse();
        }

        [Fact(DisplayName = "バジェットが 1000 で max 1500 のとき、満たす")]
        public void Check_BudgetMax_Met()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 1000);
            var cond = new ConditionDef { Stat = "budget", Max = 1500 };

            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "バジェットが 2000 で max 1500 のとき、満たさない")]
        public void Check_BudgetMax_NotMet()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 2000);
            var cond = new ConditionDef { Stat = "budget", Max = 1500 };

            GuardChecker.Check(cond, ctx, cc).Should().BeFalse();
        }

        [Fact(DisplayName = "damage 条件は自分の全リソースのダメージを合計して判定する")]
        public void Check_DamageStat_CountsAllOwnDamage()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(damage: 200);
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be_1", damage: 100);
            var ctx = MakeCtx(cc, field: field);

            var cond = new ConditionDef { Stat = "damage", Selector = new SelectorDef { Owner = "myself" }, Min = 300 };
            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();

            var condHigh = new ConditionDef { Stat = "damage", Selector = new SelectorDef { Owner = "myself" }, Min = 400 };
            GuardChecker.Check(condHigh, ctx, cc).Should().BeFalse();
        }
    }

    [Trait("対象", "リソース数条件の判定")]
    public class CountConditions
    {
        [Fact(DisplayName = "自分のリソース数が min 以上のとき満たし、未満のとき満たさない")]
        public void Check_CountSelf_Min()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "r2");
            var ctx = MakeCtx(cc, field: field);

            var cond = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 2 };
            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();

            var condHigh = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 3 };
            GuardChecker.Check(condHigh, ctx, cc).Should().BeFalse();
        }

        [Fact(DisplayName = "相手のリソース数が min 以上のとき、満たす")]
        public void Check_CountOpponent_Min()
        {
            var cc = Cc();
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "opp1");
            var ctx = MakeCtx(cc, oppField: oppField);

            var cond = new ConditionDef { Selector = new SelectorDef { Owner = "opponent" }, Min = 1 };
            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "backend ゾーンのリソース数で判定する")]
        public void Check_CountWithZone_Backend()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe_1");
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be_1");
            field.Backend[1] = TestFactory.MakeWireResource(instanceId: "be_2");
            var ctx = MakeCtx(cc, field: field);

            var cond = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", Zone = "backend" },
                Min = 2,
            };
            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "指定した faction のリソース数で判定する")]
        public void Check_CountWithFaction()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(cardId: "TST-0002", instanceId: "r2");
            var ctx = MakeCtx(cc, field: field);

            var cond = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", Faction = "SHE" },
                Min = 1,
            };
            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();

            var condThree = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", Faction = "SHE" },
                Min = 3,
            };
            GuardChecker.Check(condThree, ctx, cc).Should().BeFalse();
        }

        [Fact(DisplayName = "指定した card_id のリソース数で判定する")]
        public void Check_CountWithCardId()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "r2");
            var ctx = MakeCtx(cc, field: field);

            var cond = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", CardId = ["TST-0001"] },
                Min = 2,
            };
            GuardChecker.Check(cond, ctx, cc).Should().BeTrue();
        }
    }

    [Trait("対象", "全条件の判定")]
    public class CheckAll
    {
        [Fact(DisplayName = "条件が null または空のとき、満たしているとみなす")]
        public void CheckAll_NullOrEmpty_ReturnsTrue()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc);
            GuardChecker.CheckAll(null, ctx, cc).Should().BeTrue();
            GuardChecker.CheckAll([], ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "全ての条件を満たすとき、true を返す")]
        public void CheckAll_AllMet_ReturnsTrue()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 2000);
            var conds = new List<ConditionDef>
            {
                new() { Stat = "budget", Min = 1000 },
                new() { Stat = "budget", Max = 3000 },
            };

            GuardChecker.CheckAll(conds, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "一つでも条件を満たさないとき、false を返す")]
        public void CheckAll_OneFails_ReturnsFalse()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, budget: 2000);
            var conds = new List<ConditionDef>
            {
                new() { Stat = "budget", Min = 1000 },
                new() { Stat = "budget", Max = 1500 }, // 満たさない
            };

            GuardChecker.CheckAll(conds, ctx, cc).Should().BeFalse();
        }
    }

    [Trait("対象", "フェーズ条件の判定")]
    public class PhaseConditionCheck
    {
        [Fact(DisplayName = "ターンが 7 で turn_min 6 のとき、満たす")]
        public void CheckPhaseCondition_TurnMin_Met()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, turn: 7);
            var cond = new PhaseCondition { TurnMin = 6 };

            GuardChecker.CheckPhaseCondition(cond, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "ターンが 3 で turn_min 6 のとき、満たさない")]
        public void CheckPhaseCondition_TurnMin_NotMet()
        {
            var cc = Cc();
            var ctx = MakeCtx(cc, turn: 3);
            var cond = new PhaseCondition { TurnMin = 6 };

            GuardChecker.CheckPhaseCondition(cond, ctx, cc).Should().BeFalse();
        }

        [Fact(DisplayName = "turn_min と count の両方を満たすとき、満たす")]
        public void CheckPhaseCondition_TurnMinAndCount_BothMet()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "r2");
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "r3");
            var ctx = MakeCtx(cc, field: field, turn: 7);

            var cond = new PhaseCondition
            {
                TurnMin = 6,
                Count = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 3 },
            };

            GuardChecker.CheckPhaseCondition(cond, ctx, cc).Should().BeTrue();
        }

        [Fact(DisplayName = "turn_min は満たすが count が不足のとき、満たさない")]
        public void CheckPhaseCondition_TurnMetButCountNotMet()
        {
            var cc = Cc();
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "r1");
            var ctx = MakeCtx(cc, field: field, turn: 7);

            var cond = new PhaseCondition
            {
                TurnMin = 6,
                Count = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 3 },
            };

            GuardChecker.CheckPhaseCondition(cond, ctx, cc).Should().BeFalse();
        }
    }
}

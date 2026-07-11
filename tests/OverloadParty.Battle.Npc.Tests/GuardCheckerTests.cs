using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

public class GuardCheckerTests
{
    /// <summary>Shared setup for GuardChecker tests (seeded card cache and decision-context builder).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database", yield: 400, av: 800));
        }

        protected DecisionContext MakeCtx(
            GD.Field? field = null, GD.OpponentField? oppField = null,
            List<GD.UndeployedCard>? hand = null, long budget = 5000, long turn = 1)
        {
            return new DecisionContext(
                field ?? TestFactory.MakeWireField(),
                oppField ?? TestFactory.MakeWireOpponentField(),
                hand ?? [],
                budget,
                _cc)
            { CurrentTurn = turn };
        }
    }

    [Trait("対象", "条件未指定の判定")]
    public class NullCondition : Base
    {
        [Fact(DisplayName = "条件が null のとき、満たしているとみなす")]
        public void Check_NullCondition_ReturnsTrue()
        {
            var ctx = MakeCtx();
            GuardChecker.Check(null, ctx, _cc).Should().BeTrue();
        }
    }

    [Trait("対象", "ステータス条件の判定")]
    public class StatConditions : Base
    {
        [Fact(DisplayName = "バジェットが 2000 で min 1500 のとき、満たす")]
        public void Check_BudgetMin_Met()
        {
            var ctx = MakeCtx(budget: 2000);
            var cond = new ConditionDef { Stat = "budget", Min = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "バジェットが 1000 で min 1500 のとき、満たさない")]
        public void Check_BudgetMin_NotMet()
        {
            var ctx = MakeCtx(budget: 1000);
            var cond = new ConditionDef { Stat = "budget", Min = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeFalse();
        }

        [Fact(DisplayName = "バジェットが 1000 で max 1500 のとき、満たす")]
        public void Check_BudgetMax_Met()
        {
            var ctx = MakeCtx(budget: 1000);
            var cond = new ConditionDef { Stat = "budget", Max = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "バジェットが 2000 で max 1500 のとき、満たさない")]
        public void Check_BudgetMax_NotMet()
        {
            var ctx = MakeCtx(budget: 2000);
            var cond = new ConditionDef { Stat = "budget", Max = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeFalse();
        }

        [Fact(DisplayName = "damage 条件は自分の全リソースのダメージを合計して判定する")]
        public void Check_DamageStat_CountsAllOwnDamage()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(damage: 200);
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be_1", damage: 100);
            var ctx = MakeCtx(field: field);

            var cond = new ConditionDef { Stat = "damage", Selector = new SelectorDef { Owner = "myself" }, Min = 300 };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();

            var condHigh = new ConditionDef { Stat = "damage", Selector = new SelectorDef { Owner = "myself" }, Min = 400 };
            GuardChecker.Check(condHigh, ctx, _cc).Should().BeFalse();
        }
    }

    [Trait("対象", "リソース数条件の判定")]
    public class CountConditions : Base
    {
        [Fact(DisplayName = "自分のリソース数が min 以上のとき満たし、未満のとき満たさない")]
        public void Check_CountSelf_Min()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "r2");
            var ctx = MakeCtx(field: field);

            var cond = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 2 };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();

            var condHigh = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 3 };
            GuardChecker.Check(condHigh, ctx, _cc).Should().BeFalse();
        }

        [Fact(DisplayName = "相手のリソース数が min 以上のとき、満たす")]
        public void Check_CountOpponent_Min()
        {
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "opp1");
            var ctx = MakeCtx(oppField: oppField);

            var cond = new ConditionDef { Selector = new SelectorDef { Owner = "opponent" }, Min = 1 };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "backend ゾーンのリソース数で判定する")]
        public void Check_CountWithZone_Backend()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "fe_1");
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "be_1");
            field.Backend[1] = TestFactory.MakeWireResource(instanceId: "be_2");
            var ctx = MakeCtx(field: field);

            var cond = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", Zone = "backend" },
                Min = 2,
            };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "指定した faction のリソース数で判定する")]
        public void Check_CountWithFaction()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(cardId: "TST-0002", instanceId: "r2");
            var ctx = MakeCtx(field: field);

            var cond = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", Faction = "SHE" },
                Min = 1,
            };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();

            var condThree = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", Faction = "SHE" },
                Min = 3,
            };
            GuardChecker.Check(condThree, ctx, _cc).Should().BeFalse();
        }

        [Fact(DisplayName = "指定した card_id のリソース数で判定する")]
        public void Check_CountWithCardId()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(cardId: "TST-0001", instanceId: "r2");
            var ctx = MakeCtx(field: field);

            var cond = new ConditionDef
            {
                Selector = new SelectorDef { Owner = "myself", CardId = ["TST-0001"] },
                Min = 2,
            };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }
    }

    [Trait("対象", "全条件の判定")]
    public class CheckAll : Base
    {
        [Fact(DisplayName = "条件が null または空のとき、満たしているとみなす")]
        public void CheckAll_NullOrEmpty_ReturnsTrue()
        {
            var ctx = MakeCtx();
            GuardChecker.CheckAll(null, ctx, _cc).Should().BeTrue();
            GuardChecker.CheckAll([], ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "全ての条件を満たすとき、true を返す")]
        public void CheckAll_AllMet_ReturnsTrue()
        {
            var ctx = MakeCtx(budget: 2000);
            var conds = new List<ConditionDef>
            {
                new() { Stat = "budget", Min = 1000 },
                new() { Stat = "budget", Max = 3000 },
            };

            GuardChecker.CheckAll(conds, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "一つでも条件を満たさないとき、false を返す")]
        public void CheckAll_OneFails_ReturnsFalse()
        {
            var ctx = MakeCtx(budget: 2000);
            var conds = new List<ConditionDef>
            {
                new() { Stat = "budget", Min = 1000 },
                new() { Stat = "budget", Max = 1500 }, // fails
            };

            GuardChecker.CheckAll(conds, ctx, _cc).Should().BeFalse();
        }
    }

    [Trait("対象", "フェーズ条件の判定")]
    public class PhaseConditionCheck : Base
    {
        [Fact(DisplayName = "ターンが 7 で turn_min 6 のとき、満たす")]
        public void CheckPhaseCondition_TurnMin_Met()
        {
            var ctx = MakeCtx(turn: 7);
            var cond = new PhaseCondition { TurnMin = 6 };

            GuardChecker.CheckPhaseCondition(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "ターンが 3 で turn_min 6 のとき、満たさない")]
        public void CheckPhaseCondition_TurnMin_NotMet()
        {
            var ctx = MakeCtx(turn: 3);
            var cond = new PhaseCondition { TurnMin = 6 };

            GuardChecker.CheckPhaseCondition(cond, ctx, _cc).Should().BeFalse();
        }

        [Fact(DisplayName = "turn_min と count の両方を満たすとき、満たす")]
        public void CheckPhaseCondition_TurnMinAndCount_BothMet()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "r1");
            field.Frontend[1] = TestFactory.MakeWireResource(instanceId: "r2");
            field.Backend[0] = TestFactory.MakeWireResource(instanceId: "r3");
            var ctx = MakeCtx(field: field, turn: 7);

            var cond = new PhaseCondition
            {
                TurnMin = 6,
                Count = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 3 },
            };

            GuardChecker.CheckPhaseCondition(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact(DisplayName = "turn_min は満たすが count が不足のとき、満たさない")]
        public void CheckPhaseCondition_TurnMetButCountNotMet()
        {
            var field = TestFactory.MakeWireField();
            field.Frontend[0] = TestFactory.MakeWireResource(instanceId: "r1");
            var ctx = MakeCtx(field: field, turn: 7);

            var cond = new PhaseCondition
            {
                TurnMin = 6,
                Count = new ConditionDef { Selector = new SelectorDef { Owner = "myself" }, Min = 3 },
            };

            GuardChecker.CheckPhaseCondition(cond, ctx, _cc).Should().BeFalse();
        }
    }
}

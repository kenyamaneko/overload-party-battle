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

    /// <summary>Tests for Check treating a null condition as satisfied.</summary>
    public class NullCondition : Base
    {
        [Fact]
        public void Check_NullCondition_ReturnsTrue()
        {
            var ctx = MakeCtx();
            GuardChecker.Check(null, ctx, _cc).Should().BeTrue();
        }
    }

    /// <summary>Tests for Check evaluating stat-based conditions.</summary>
    public class StatConditions : Base
    {
        [Fact]
        public void Check_BudgetMin_Met()
        {
            var ctx = MakeCtx(budget: 2000);
            var cond = new ConditionDef { Stat = "budget", Min = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact]
        public void Check_BudgetMin_NotMet()
        {
            var ctx = MakeCtx(budget: 1000);
            var cond = new ConditionDef { Stat = "budget", Min = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeFalse();
        }

        [Fact]
        public void Check_BudgetMax_Met()
        {
            var ctx = MakeCtx(budget: 1000);
            var cond = new ConditionDef { Stat = "budget", Max = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact]
        public void Check_BudgetMax_NotMet()
        {
            var ctx = MakeCtx(budget: 2000);
            var cond = new ConditionDef { Stat = "budget", Max = 1500 };

            GuardChecker.Check(cond, ctx, _cc).Should().BeFalse();
        }

        [Fact]
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

    /// <summary>Tests for Check evaluating resource-count conditions.</summary>
    public class CountConditions : Base
    {
        [Fact]
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

        [Fact]
        public void Check_CountOpponent_Min()
        {
            var oppField = TestFactory.MakeWireOpponentField();
            oppField.Frontend[0] = TestFactory.MakeWireResource(instanceId: "opp1");
            var ctx = MakeCtx(oppField: oppField);

            var cond = new ConditionDef { Selector = new SelectorDef { Owner = "opponent" }, Min = 1 };
            GuardChecker.Check(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact]
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

        [Fact]
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

        [Fact]
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

    /// <summary>Tests for CheckAll requiring every condition to hold.</summary>
    public class CheckAll : Base
    {
        [Fact]
        public void CheckAll_NullOrEmpty_ReturnsTrue()
        {
            var ctx = MakeCtx();
            GuardChecker.CheckAll(null, ctx, _cc).Should().BeTrue();
            GuardChecker.CheckAll([], ctx, _cc).Should().BeTrue();
        }

        [Fact]
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

        [Fact]
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

    /// <summary>Tests for CheckPhaseCondition combining turn and count gates.</summary>
    public class PhaseConditionCheck : Base
    {
        [Fact]
        public void CheckPhaseCondition_TurnMin_Met()
        {
            var ctx = MakeCtx(turn: 7);
            var cond = new PhaseCondition { TurnMin = 6 };

            GuardChecker.CheckPhaseCondition(cond, ctx, _cc).Should().BeTrue();
        }

        [Fact]
        public void CheckPhaseCondition_TurnMin_NotMet()
        {
            var ctx = MakeCtx(turn: 3);
            var cond = new PhaseCondition { TurnMin = 6 };

            GuardChecker.CheckPhaseCondition(cond, ctx, _cc).Should().BeFalse();
        }

        [Fact]
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

        [Fact]
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

using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

public class EnumExtensionsTests
{
    [Trait("対象", "Phase 変換")]
    public class PhaseConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(Phase.Draw, "draw")]
        [InlineData(Phase.Main, "main")]
        [InlineData(Phase.Battle, "battle")]
        [InlineData(Phase.End, "end")]
        public void ToWireString_ReturnsExpected(Phase phase, string expected)
        {
            phase.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("draw", Phase.Draw)]
        [InlineData("main", Phase.Main)]
        [InlineData("battle", Phase.Battle)]
        [InlineData("end", Phase.End)]
        public void Parse_ValidInput_ReturnsExpected(string input, Phase expected)
        {
            EnumExtensions.ParsePhase(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (Phase)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParsePhase("unknown");
            act.Should().Throw<ArgumentException>().WithMessage("*phase*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(Phase.Draw)]
        [InlineData(Phase.Main)]
        [InlineData(Phase.Battle)]
        [InlineData(Phase.End)]
        public void Roundtrip(Phase phase)
        {
            EnumExtensions.ParsePhase(phase.ToWireString()).Should().Be(phase);
        }
    }

    [Trait("対象", "Rank 変換")]
    public class RankConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(Rank.Small, "small")]
        [InlineData(Rank.Medium, "medium")]
        [InlineData(Rank.Large, "large")]
        public void ToWireString_ReturnsExpected(Rank rank, string expected)
        {
            rank.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("small", Rank.Small)]
        [InlineData("medium", Rank.Medium)]
        [InlineData("large", Rank.Large)]
        public void Parse_ValidInput_ReturnsExpected(string input, Rank expected)
        {
            EnumExtensions.ParseRank(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (Rank)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParseRank("huge");
            act.Should().Throw<ArgumentException>().WithMessage("*rank*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(Rank.Small)]
        [InlineData(Rank.Medium)]
        [InlineData(Rank.Large)]
        public void Roundtrip(Rank rank)
        {
            EnumExtensions.ParseRank(rank.ToWireString()).Should().Be(rank);
        }
    }

    [Trait("対象", "InstanceFamily 変換")]
    public class InstanceFamilyConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(InstanceFamily.M, "M")]
        [InlineData(InstanceFamily.C, "C")]
        [InlineData(InstanceFamily.R, "R")]
        public void ToWireString_ReturnsExpected(InstanceFamily family, string expected)
        {
            family.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("M", InstanceFamily.M)]
        [InlineData("C", InstanceFamily.C)]
        [InlineData("R", InstanceFamily.R)]
        public void Parse_ValidInput_ReturnsExpected(string input, InstanceFamily expected)
        {
            EnumExtensions.ParseInstanceFamily(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (InstanceFamily)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParseInstanceFamily("X");
            act.Should().Throw<ArgumentException>().WithMessage("*instance family*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(InstanceFamily.M)]
        [InlineData(InstanceFamily.C)]
        [InlineData(InstanceFamily.R)]
        public void Roundtrip(InstanceFamily family)
        {
            EnumExtensions.ParseInstanceFamily(family.ToWireString()).Should().Be(family);
        }
    }

    [Trait("対象", "GameStatus 変換")]
    public class GameStatusConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(GameStatus.Playing, "playing")]
        [InlineData(GameStatus.Finished, "finished")]
        public void ToWireString_ReturnsExpected(GameStatus status, string expected)
        {
            status.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("playing", GameStatus.Playing)]
        [InlineData("finished", GameStatus.Finished)]
        public void Parse_ValidInput_ReturnsExpected(string input, GameStatus expected)
        {
            EnumExtensions.ParseGameStatus(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (GameStatus)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParseGameStatus("paused");
            act.Should().Throw<ArgumentException>().WithMessage("*game status*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(GameStatus.Playing)]
        [InlineData(GameStatus.Finished)]
        public void Roundtrip(GameStatus status)
        {
            EnumExtensions.ParseGameStatus(status.ToWireString()).Should().Be(status);
        }
    }

    [Trait("対象", "WinReason 変換")]
    public class WinReasonConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(WinReason.BudgetZero, "budget_zero")]
        [InlineData(WinReason.SystemDown, "system_down")]
        [InlineData(WinReason.DeckOut, "deck_out")]
        [InlineData(WinReason.TurnTimeout, "turn_timeout")]
        [InlineData(WinReason.Disconnect, "disconnect")]
        [InlineData(WinReason.TurnLimit, "turn_limit")]
        [InlineData(WinReason.Draw, "draw")]
        [InlineData(WinReason.LaunchFailure, "launch_failure")]
        [InlineData(WinReason.Surrender, "surrender")]
        public void ToWireString_ReturnsExpected(WinReason reason, string expected)
        {
            reason.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("budget_zero", WinReason.BudgetZero)]
        [InlineData("system_down", WinReason.SystemDown)]
        [InlineData("deck_out", WinReason.DeckOut)]
        [InlineData("turn_timeout", WinReason.TurnTimeout)]
        [InlineData("disconnect", WinReason.Disconnect)]
        [InlineData("turn_limit", WinReason.TurnLimit)]
        [InlineData("draw", WinReason.Draw)]
        [InlineData("launch_failure", WinReason.LaunchFailure)]
        [InlineData("surrender", WinReason.Surrender)]
        public void Parse_ValidInput_ReturnsExpected(string input, WinReason expected)
        {
            EnumExtensions.ParseWinReason(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (WinReason)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParseWinReason("timeout");
            act.Should().Throw<ArgumentException>().WithMessage("*win reason*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(WinReason.BudgetZero)]
        [InlineData(WinReason.SystemDown)]
        [InlineData(WinReason.DeckOut)]
        [InlineData(WinReason.TurnTimeout)]
        [InlineData(WinReason.Disconnect)]
        [InlineData(WinReason.TurnLimit)]
        [InlineData(WinReason.Draw)]
        [InlineData(WinReason.LaunchFailure)]
        [InlineData(WinReason.Surrender)]
        public void Roundtrip(WinReason reason)
        {
            EnumExtensions.ParseWinReason(reason.ToWireString()).Should().Be(reason);
        }
    }

    [Trait("対象", "ActionType 変換")]
    public class ActionTypeConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(ActionType.PlayCard, "play_card")]
        [InlineData(ActionType.Attack, "attack")]
        [InlineData(ActionType.ScaleUp, "scale_up")]
        [InlineData(ActionType.Monetize, "monetize")]
        [InlineData(ActionType.DiscardHand, "discard_hand")]
        [InlineData(ActionType.UseIgnition, "use_ignition")]
        [InlineData(ActionType.EndPhase, "end_phase")]
        [InlineData(ActionType.Forfeit, "forfeit")]
        public void ToWireString_ReturnsExpected(ActionType action, string expected)
        {
            action.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("play_card", ActionType.PlayCard)]
        [InlineData("attack", ActionType.Attack)]
        [InlineData("scale_up", ActionType.ScaleUp)]
        [InlineData("monetize", ActionType.Monetize)]
        [InlineData("discard_hand", ActionType.DiscardHand)]
        [InlineData("use_ignition", ActionType.UseIgnition)]
        [InlineData("end_phase", ActionType.EndPhase)]
        [InlineData("forfeit", ActionType.Forfeit)]
        public void Parse_ValidInput_ReturnsExpected(string input, ActionType expected)
        {
            EnumExtensions.ParseActionType(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (ActionType)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParseActionType("summon");
            act.Should().Throw<ArgumentException>().WithMessage("*action type*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(ActionType.PlayCard)]
        [InlineData(ActionType.Attack)]
        [InlineData(ActionType.ScaleUp)]
        [InlineData(ActionType.Monetize)]
        [InlineData(ActionType.DiscardHand)]
        [InlineData(ActionType.UseIgnition)]
        [InlineData(ActionType.EndPhase)]
        [InlineData(ActionType.Forfeit)]
        public void Roundtrip(ActionType action)
        {
            EnumExtensions.ParseActionType(action.ToWireString()).Should().Be(action);
        }
    }

    [Trait("対象", "Zone 変換")]
    public class ZoneConversion
    {
        [Theory(DisplayName = "各値を対応するワイヤ文字列に変換する")]
        [InlineData(Zone.Frontend, "frontend")]
        [InlineData(Zone.Backend, "backend")]
        [InlineData(Zone.Support, "support")]
        public void ToWireString_ReturnsExpected(Zone zone, string expected)
        {
            zone.ToWireString().Should().Be(expected);
        }

        [Theory(DisplayName = "ワイヤ文字列を対応する値に解析する")]
        [InlineData("frontend", Zone.Frontend)]
        [InlineData("backend", Zone.Backend)]
        [InlineData("support", Zone.Support)]
        public void Parse_ValidInput_ReturnsExpected(string input, Zone expected)
        {
            EnumExtensions.ParseZone(input).Should().Be(expected);
        }

        [Fact(DisplayName = "未定義の値を変換すると ArgumentOutOfRangeException を投げる")]
        public void ToWireString_InvalidValue_Throws()
        {
            var invalid = (Zone)999;
            var act = () => invalid.ToWireString();
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "未知のワイヤ文字列を解析すると ArgumentException を投げる")]
        public void Parse_InvalidInput_Throws()
        {
            var act = () => EnumExtensions.ParseZone("midfield");
            act.Should().Throw<ArgumentException>().WithMessage("*zone*");
        }

        [Theory(DisplayName = "ワイヤ文字列へ変換して解析すると元の値に戻る")]
        [InlineData(Zone.Frontend)]
        [InlineData(Zone.Backend)]
        [InlineData(Zone.Support)]
        public void Roundtrip(Zone zone)
        {
            EnumExtensions.ParseZone(zone.ToWireString()).Should().Be(zone);
        }
    }

    [Trait("対象", "カードタイプ分類")]
    public class GetCategory
    {
        [Theory(DisplayName = "カードタイプを対応する分類に変換する")]
        [InlineData("Compute", CardTypeCategory.Compute)]
        [InlineData("DataResource", CardTypeCategory.DataResource)]
        [InlineData("Platform", CardTypeCategory.Support)]
        [InlineData("Attachment", CardTypeCategory.Support)]
        [InlineData("Strategy", CardTypeCategory.Support)]
        [InlineData("Reactive", CardTypeCategory.Support)]
        [InlineData("Incident", CardTypeCategory.Support)]
        public void ValidCardType_ReturnsExpected(string cardType, CardTypeCategory expected)
        {
            EnumExtensions.GetCategory(cardType).Should().Be(expected);
        }

        [Fact(DisplayName = "未知のカードタイプを変換すると ArgumentException を投げる")]
        public void UnknownCardType_Throws()
        {
            var act = () => EnumExtensions.GetCategory("Unknown");
            act.Should().Throw<ArgumentException>().WithMessage("*card type*");
        }
    }
}

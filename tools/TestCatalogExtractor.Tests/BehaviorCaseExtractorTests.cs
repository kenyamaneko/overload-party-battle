namespace OverloadParty.Battle.TestCatalogExtractor.Tests;

[Trait("対象", "テスト観点レコード抽出")]
public class BehaviorCaseExtractorTests
{
    [Fact(DisplayName = "クラスにTraitとFactがあるとき、対象とケース名を1件抽出する")]
    public void ExtractsTargetAndCaseFromFact()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Fact(DisplayName = "入力が正の数のとき、そのまま返す")]
                public void Test1() { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().BeEquivalentTo(new[]
        {
            new BehaviorCaseRecord("整数変換", "入力が正の数のとき、そのまま返す", false, "Sample/IntegerConversionTests.cs"),
        });
    }

    [Fact(DisplayName = "外側と内側のネストしたクラスに別々のTraitがあるとき、直近のTraitを対象にする")]
    public void PrefersNearestTraitOverOuterClass()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            public class OuterTests
            {
                [Trait("対象", "外側")]
                public class Nested
                {
                    [Trait("対象", "内側")]
                    public class Inner
                    {
                        [Fact(DisplayName = "ケース")]
                        public void Test1() { }
                    }
                }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/OuterTests.cs");

        records.Should().ContainSingle(record => record.Target == "内側");
    }

    [Fact(DisplayName = "祖先クラスにTraitが1つも無いとき、Trait不在の例外になる")]
    public void ThrowsWhenNoAncestorHasTrait()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            public class NoTraitTests
            {
                [Fact(DisplayName = "ケース")]
                public void Test1() { }
            }
            """;

        var act = () => BehaviorCaseExtractor.Extract(source, "Sample/NoTraitTests.cs");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*[Trait(\"対象\", ...)] がありません*");
    }

    [Fact(DisplayName = "DisplayNameが無いとき、Traitとは異なるDisplayName不在の例外になる")]
    public void ThrowsWhenDisplayNameMissing()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Fact]
                public void Test1() { }
            }
            """;

        var act = () => BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DisplayName がありません*");
    }

    [Fact(DisplayName = "InlineDataが1件のとき、1件のケースとして抽出する")]
    public void ExtractsOneCaseForSingleInlineData()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Theory(DisplayName = "入力に応じた値を返す")]
                [InlineData(1)]
                public void Test1(int value) { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().HaveCount(1);
    }

    [Fact(DisplayName = "InlineDataが複数件のとき、件数ぶん同一内容のケースを展開する")]
    public void ExpandsOneRecordPerInlineData()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Theory(DisplayName = "入力に応じた値を返す")]
                [InlineData(0)]
                [InlineData(1)]
                [InlineData(-1)]
                public void Test1(int value) { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().HaveCount(3);
        records.Should().OnlyContain(record =>
            record.Target == "整数変換"
            && record.Case == "入力に応じた値を返す"
            && record.Source == "Sample/IntegerConversionTests.cs");
    }

    [Fact(DisplayName = "InlineDataを持たずMemberDataのみのTheoryのとき、1件のケースとして抽出する")]
    public void ExtractsOneCaseWhenTheoryHasNoInlineData()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Theory(DisplayName = "入力に応じた値を返す")]
                [MemberData(nameof(Cases))]
                public void Test1(int value) { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().HaveCount(1);
    }

    [Fact(DisplayName = "Skip引数があるとき、skippedはtrueになる")]
    public void MarksSkippedWhenSkipArgumentPresent()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Fact(DisplayName = "ケース", Skip = "未対応")]
                public void Test1() { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle(record => record.IsSkipped);
    }

    [Fact(DisplayName = "Skip引数が無いとき、skippedはfalseになる")]
    public void MarksNotSkippedWhenSkipArgumentAbsent()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Fact(DisplayName = "ケース")]
                public void Test1() { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle(record => !record.IsSkipped);
    }
}

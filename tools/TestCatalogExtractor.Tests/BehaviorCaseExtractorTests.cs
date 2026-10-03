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
            new BehaviorCaseRecord("整数変換", Array.Empty<string>(), "入力が正の数のとき、そのまま返す", false, "Sample/IntegerConversionTests.cs"),
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

    [Fact(DisplayName = "対象のTraitを持つクラスの内側に小分類のTraitを持つクラスが1つだけ入れ子になっているとき、抽出したレコードのグループはその小分類の値になる")]
    public void ExtractsSingleGroupFromOneNestedSubgroupTrait()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Trait("小分類", "異常入力")]
                public class InnerTests
                {
                    [Fact(DisplayName = "ケース")]
                    public void Test1() { }
                }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle().Which.Groups.Should().Equal("異常入力");
    }

    [Fact(DisplayName = "対象のTraitを持つクラスの内側に小分類のTraitを持つクラスが2段入れ子になっているとき、抽出したレコードのグループは外側のクラスの値、内側のクラスの値の順になる")]
    public void OrdersGroupsFromOuterToInnerWhenSubgroupTraitsNestTwoLevels()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Trait("小分類", "外側グループ")]
                public class MiddleTests
                {
                    [Trait("小分類", "内側グループ")]
                    public class InnerTests
                    {
                        [Fact(DisplayName = "ケース")]
                        public void Test1() { }
                    }
                }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle().Which.Groups.Should().Equal("外側グループ", "内側グループ");
    }

    [Fact(DisplayName = "対象のTraitと同じクラスに正常異常のTraitで正常系が指定されているとき、抽出したレコードのグループに正常系が含まれる")]
    public void IncludesNormalCaseGroupWhenNormalAbnormalTraitIsNormal()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            [Trait("正常異常", "正常系")]
            public class IntegerConversionTests
            {
                [Fact(DisplayName = "ケース")]
                public void Test1() { }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle().Which.Groups.Should().Contain("正常系");
    }

    [Fact(DisplayName = "対象のTraitを持つクラスの内側に小分類のTraitを持つクラスがあり、さらにその内側に正常異常のTraitで異常系が指定されたクラスが入れ子になっているとき、抽出したレコードのグループは小分類の値、異常系の順になる")]
    public void OrdersGroupsBySubgroupThenAbnormalCaseWhenNested()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Trait("小分類", "異常入力")]
                public class MiddleTests
                {
                    [Trait("正常異常", "異常系")]
                    public class InnerTests
                    {
                        [Fact(DisplayName = "ケース")]
                        public void Test1() { }
                    }
                }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle().Which.Groups.Should().Equal("異常入力", "異常系");
    }

    [Fact(DisplayName = "対象のTraitを持つクラス以外に小分類のTraitも正常異常のTraitも無いとき、抽出したレコードのグループは空になる")]
    public void ExtractsEmptyGroupsWhenNoSubgroupOrNormalAbnormalTraitExists()
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

        records.Should().ContainSingle().Which.Groups.Should().BeEmpty();
    }

    [Fact(DisplayName = "正常異常のTraitの値が正常系と異常系のどちらでもないとき、値が不正であることを示す例外になる")]
    public void ThrowsWhenNormalAbnormalTraitValueIsInvalid()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Trait("正常異常", "不明")]
                public class InnerTests
                {
                    [Fact(DisplayName = "ケース")]
                    public void Test1() { }
                }
            }
            """;

        var act = () => BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        var exception = act.Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("正常異常");
        exception.Message.Should().Contain("不明");
    }

    [Fact(DisplayName = "小分類のTraitに値が無いとき、値が無いことを示す例外になる")]
    public void ThrowsWhenSubgroupTraitValueIsMissing()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Trait("小分類")]
                public class InnerTests
                {
                    [Fact(DisplayName = "ケース")]
                    public void Test1() { }
                }
            }
            """;

        var act = () => BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        var exception = act.Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("小分類");
        exception.Message.Should().NotContain("正常異常");
    }

    [Theory(DisplayName = "対象のTraitを持つクラスより外側のクラスに小分類または正常異常のTraitがあるとき、外側に置かれていることを示す例外になる")]
    [InlineData("小分類", "異常入力")]
    [InlineData("正常異常", "異常系")]
    public void ThrowsWhenSubgroupOrNormalAbnormalTraitExistsOutsideTargetClass(string traitName, string traitValue)
    {
        var source = $$"""
            using Xunit;

            namespace Sample;

            [Trait("{{traitName}}", "{{traitValue}}")]
            public class OuterTests
            {
                [Trait("対象", "整数変換")]
                public class IntegerConversionTests
                {
                    [Fact(DisplayName = "ケース")]
                    public void Test1() { }
                }
            }
            """;

        var act = () => BehaviorCaseExtractor.Extract(source, "Sample/OuterTests.cs");

        var exception = act.Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("外側");
    }

    [Fact(DisplayName = "対象のTraitを持つクラスの内側に、小分類がグループAのクラスと小分類がグループBのクラスが兄弟として入れ子になっているとき、グループAのクラスのテストメソッドから抽出したレコードのグループはグループAだけになる")]
    public void ExtractsOnlyOwnGroupWhenSiblingNestedClassesHaveDifferentSubgroupTraits()
    {
        const string source = """
            using Xunit;

            namespace Sample;

            [Trait("対象", "整数変換")]
            public class IntegerConversionTests
            {
                [Trait("小分類", "グループA")]
                public class GroupATests
                {
                    [Fact(DisplayName = "ケースA")]
                    public void TestA() { }
                }

                [Trait("小分類", "グループB")]
                public class GroupBTests
                {
                    [Fact(DisplayName = "ケースB")]
                    public void TestB() { }
                }
            }
            """;

        var records = BehaviorCaseExtractor.Extract(source, "Sample/IntegerConversionTests.cs");

        records.Should().ContainSingle(record => record.Case == "ケースA").Which.Groups.Should().Equal("グループA");
    }
}

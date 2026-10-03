using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OverloadParty.Battle.TestCatalogExtractor;

/// <summary>
/// battle のテストソースを構文解析し、<c>[Trait]</c> / <c>[Fact|Theory(DisplayName)]</c> から
/// テスト観点カタログの中間レコードを抽出する。
/// </summary>
public static class BehaviorCaseExtractor
{
    private const string TargetTraitKey = "対象";
    private const string SubgroupTraitKey = "小分類";
    private const string NormalOrAbnormalTraitKey = "正常異常";
    private const string AttributeSuffix = "Attribute";

    private static readonly string[] AllowedNormalOrAbnormalValues = { "正常系", "異常系" };

    /// <summary>
    /// 1 ファイル分の C# ソースからテスト観点レコードを抽出する。
    /// </summary>
    /// <param name="sourceText">解析対象の C# ソースコード全文。</param>
    /// <param name="relativeSourcePath">レコードの Source に書き込む、tests ルートからの相対パス。</param>
    /// <returns>
    /// ソース中の <c>[Fact]</c> / <c>[Theory]</c> メソッドから抽出したレコードの列。
    /// <c>[Theory]</c> は <c>[InlineData]</c> の件数ぶん同一内容で展開し、
    /// 件数を静的に数えられないとき (<c>[MemberData]</c> 等) は 1 件とする。
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <c>[Fact]</c> / <c>[Theory]</c> メソッドの祖先クラスに <c>[Trait("対象", ...)]</c> が無い、
    /// または DisplayName が無いとき。
    /// </exception>
    public static IReadOnlyList<BehaviorCaseRecord> Extract(string sourceText, string relativeSourcePath)
    {
        var root = CSharpSyntaxTree.ParseText(sourceText).GetCompilationUnitRoot();

        var records = new List<BehaviorCaseRecord>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var testAttribute = FindAttribute(method.AttributeLists, "Fact")
                ?? FindAttribute(method.AttributeLists, "Theory");
            if (testAttribute is null)
            {
                continue;
            }

            var target = ResolveTarget(method, relativeSourcePath);
            var groups = ResolveGroups(method, relativeSourcePath);
            var displayName = ResolveDisplayName(testAttribute, method, relativeSourcePath);
            var skipped = HasNamedArgument(testAttribute, "Skip");
            var rowCount = Math.Max(CountAttributes(method.AttributeLists, "InlineData"), 1);

            for (var i = 0; i < rowCount; i++)
            {
                records.Add(new BehaviorCaseRecord(target, groups, displayName, skipped, relativeSourcePath));
            }
        }

        return records;
    }

    private static string ResolveTarget(MethodDeclarationSyntax method, string relativeSourcePath)
    {
        foreach (var classNode in method.Ancestors().OfType<ClassDeclarationSyntax>())
        {
            var traitAttribute = FindSingleTraitAttribute(classNode.AttributeLists, TargetTraitKey, method, relativeSourcePath);
            if (traitAttribute is null)
            {
                continue;
            }

            return GetPositionalStringArgument(traitAttribute, position: 1)
                ?? throw new InvalidOperationException(
                    $"{relativeSourcePath}: {method.Identifier.Text} の [Trait(\"対象\", ...)] に値がありません。");
        }

        throw new InvalidOperationException(
            $"{relativeSourcePath}: {method.Identifier.Text} を含むクラスに [Trait(\"対象\", ...)] がありません。");
    }

    /// <summary>
    /// メソッドの祖先クラスを内側から外側へ辿り、小分類・正常異常の Trait を収集する。
    /// </summary>
    /// <returns>外側から内側の順に並べたラベルの列。小分類・正常異常が無ければ空。</returns>
    /// <exception cref="InvalidOperationException">
    /// <c>[Trait("小分類", ...)]</c> に値が無い、<c>[Trait("正常異常", ...)]</c> の値が
    /// 「正常系」「異常系」のいずれでもない、同じクラスに同じキーの <c>[Trait]</c> が
    /// 2 つ以上ある、または <c>[Trait("対象", ...)]</c> を持つクラスより外側に
    /// <c>[Trait("小分類"|"正常異常", ...)]</c> があるとき。
    /// </exception>
    private static IReadOnlyList<string> ResolveGroups(MethodDeclarationSyntax method, string relativeSourcePath)
    {
        var collected = new List<string>();
        var foundTarget = false;
        foreach (var classNode in method.Ancestors().OfType<ClassDeclarationSyntax>())
        {
            var subgroupAttribute = FindSingleTraitAttribute(classNode.AttributeLists, SubgroupTraitKey, method, relativeSourcePath);
            var normalOrAbnormalAttribute = FindSingleTraitAttribute(classNode.AttributeLists, NormalOrAbnormalTraitKey, method, relativeSourcePath);

            if (foundTarget && (subgroupAttribute is not null || normalOrAbnormalAttribute is not null))
            {
                throw new InvalidOperationException(
                    $"{relativeSourcePath}: {method.Identifier.Text} の [Trait(\"小分類\"または\"正常異常\", ...)] が [Trait(\"対象\", ...)] を持つクラスより外側にあります。");
            }

            if (!foundTarget)
            {
                var classLabels = new List<string>();

                if (subgroupAttribute is not null)
                {
                    classLabels.Add(
                        GetPositionalStringArgument(subgroupAttribute, position: 1)
                            ?? throw new InvalidOperationException(
                                $"{relativeSourcePath}: {method.Identifier.Text} の [Trait(\"小分類\", ...)] に値がありません。"));
                }

                if (normalOrAbnormalAttribute is not null)
                {
                    var value = GetPositionalStringArgument(normalOrAbnormalAttribute, position: 1);
                    if (value is null || !AllowedNormalOrAbnormalValues.Contains(value))
                    {
                        throw new InvalidOperationException(
                            $"{relativeSourcePath}: {method.Identifier.Text} の [Trait(\"正常異常\", ...)] の値が「正常系」「異常系」のいずれでもありません: {value}");
                    }

                    classLabels.Add(value);
                }

                collected.InsertRange(0, classLabels);
            }

            if (FindSingleTraitAttribute(classNode.AttributeLists, TargetTraitKey, method, relativeSourcePath) is not null)
            {
                foundTarget = true;
            }
        }

        return collected;
    }

    private static string ResolveDisplayName(
        AttributeSyntax testAttribute, MethodDeclarationSyntax method, string relativeSourcePath)
    {
        return GetNamedStringArgument(testAttribute, "DisplayName")
            ?? throw new InvalidOperationException(
                $"{relativeSourcePath}: {method.Identifier.Text} に DisplayName がありません。");
    }

    /// <summary>
    /// クラスの属性から、指定した key を持つ <c>[Trait]</c> を1つだけ探す。
    /// </summary>
    /// <exception cref="InvalidOperationException">同じ key の <c>[Trait]</c> が2つ以上あるとき。</exception>
    private static AttributeSyntax? FindSingleTraitAttribute(
        SyntaxList<AttributeListSyntax> attributeLists, string key, MethodDeclarationSyntax method, string relativeSourcePath)
    {
        var matches = attributeLists
            .SelectMany(list => list.Attributes)
            .Where(attribute =>
                GetSimpleName(attribute) == "Trait"
                && GetPositionalStringArgument(attribute, position: 0) == key)
            .ToList();

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"{relativeSourcePath}: {method.Identifier.Text} を含むクラスに [Trait(\"{key}\", ...)] が複数あります。");
        }

        return matches.SingleOrDefault();
    }

    private static AttributeSyntax? FindAttribute(SyntaxList<AttributeListSyntax> attributeLists, string simpleName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute => GetSimpleName(attribute) == simpleName);
    }

    private static int CountAttributes(SyntaxList<AttributeListSyntax> attributeLists, string simpleName)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .Count(attribute => GetSimpleName(attribute) == simpleName);
    }

    private static bool HasNamedArgument(AttributeSyntax attribute, string name)
    {
        return attribute.ArgumentList?.Arguments
            .Any(argument => argument.NameEquals?.Name.Identifier.Text == name) ?? false;
    }

    private static string? GetPositionalStringArgument(AttributeSyntax attribute, int position)
    {
        var positionalArguments = attribute.ArgumentList?.Arguments
            .Where(argument => argument.NameEquals is null && argument.NameColon is null)
            .ToList();
        if (positionalArguments is null || position >= positionalArguments.Count)
        {
            return null;
        }

        return (positionalArguments[position].Expression as LiteralExpressionSyntax)?.Token.ValueText;
    }

    private static string? GetNamedStringArgument(AttributeSyntax attribute, string name)
    {
        var argument = attribute.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == name);
        return (argument?.Expression as LiteralExpressionSyntax)?.Token.ValueText;
    }

    private static string GetSimpleName(AttributeSyntax attribute)
    {
        var name = attribute.Name is QualifiedNameSyntax qualified
            ? qualified.Right.Identifier.Text
            : ((SimpleNameSyntax)attribute.Name).Identifier.Text;
        return name.EndsWith(AttributeSuffix, StringComparison.Ordinal)
            ? name[..^AttributeSuffix.Length]
            : name;
    }
}

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
    private const string AttributeSuffix = "Attribute";

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
            var displayName = ResolveDisplayName(testAttribute, method, relativeSourcePath);
            var skipped = HasNamedArgument(testAttribute, "Skip");
            var rowCount = Math.Max(CountAttributes(method.AttributeLists, "InlineData"), 1);

            for (var i = 0; i < rowCount; i++)
            {
                records.Add(new BehaviorCaseRecord(target, displayName, skipped, relativeSourcePath));
            }
        }

        return records;
    }

    private static string ResolveTarget(MethodDeclarationSyntax method, string relativeSourcePath)
    {
        foreach (var classNode in method.Ancestors().OfType<ClassDeclarationSyntax>())
        {
            var traitAttribute = FindTraitAttribute(classNode.AttributeLists);
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

    private static string ResolveDisplayName(
        AttributeSyntax testAttribute, MethodDeclarationSyntax method, string relativeSourcePath)
    {
        return GetNamedStringArgument(testAttribute, "DisplayName")
            ?? throw new InvalidOperationException(
                $"{relativeSourcePath}: {method.Identifier.Text} に DisplayName がありません。");
    }

    private static AttributeSyntax? FindTraitAttribute(SyntaxList<AttributeListSyntax> attributeLists)
    {
        return attributeLists
            .SelectMany(list => list.Attributes)
            .FirstOrDefault(attribute =>
                GetSimpleName(attribute) == "Trait"
                && GetPositionalStringArgument(attribute, position: 0) == TargetTraitKey);
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

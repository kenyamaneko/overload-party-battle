using System.Text.Json;
using OverloadParty.Battle.TestCatalogExtractor;

var testsRoot = GetRequiredArg(args, "--tests-root");
var outputPath = GetRequiredArg(args, "--output");

var records = new List<BehaviorCaseRecord>();
foreach (var filePath in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
{
    var relativeSourcePath = Path.GetRelativePath(testsRoot, filePath).Replace('\\', '/');
    var sourceText = await File.ReadAllTextAsync(filePath);
    records.AddRange(BehaviorCaseExtractor.Extract(sourceText, relativeSourcePath));
}

var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(outputPath, json);

Console.WriteLine($"{records.Count} 件のテスト観点レコードを {outputPath} に出力しました。");

static string GetRequiredArg(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0 || index + 1 >= args.Length)
    {
        throw new ArgumentException($"{name} <path> を指定してください。");
    }

    return args[index + 1];
}

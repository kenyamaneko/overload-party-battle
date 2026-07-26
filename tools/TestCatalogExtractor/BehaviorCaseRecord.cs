using System.Text.Json.Serialization;

namespace OverloadParty.Battle.TestCatalogExtractor;

/// <summary>
/// テスト観点カタログの中間 JSON レコード。common の csharp-json パーサが読む形式。
/// </summary>
/// <param name="Target">テスト対象の要素 (<c>[Trait("対象", ...)]</c> の値)。</param>
/// <param name="Case">ケース名 (<c>[Fact|Theory(DisplayName = ...)]</c> の値)。</param>
/// <param name="IsSkipped">テストが無効化されているかどうか。</param>
/// <param name="Source">由来ファイルの tests ルートからの相対パス。</param>
public sealed record BehaviorCaseRecord(
    [property: JsonPropertyName("target")] string Target,
    [property: JsonPropertyName("case")] string Case,
    [property: JsonPropertyName("skipped")] bool IsSkipped,
    [property: JsonPropertyName("source")] string Source);

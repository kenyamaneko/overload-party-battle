using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OverloadParty.Battle.Npc;

public static class AiConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithTypeConverter(new EffectPriorityEntryConverter())
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads all YAML files in the given directory and returns a dictionary keyed by model name.
    /// </summary>
    /// <param name="directory">YAML 設定ファイルを格納したディレクトリパス。</param>
    /// <returns>モデル名をキーとする AI 設定辞書。</returns>
    public static Dictionary<string, AiConfig> LoadAll(string directory)
    {
        var configs = new Dictionary<string, AiConfig>();
        foreach (var file in Directory.GetFiles(directory, "*.yaml"))
        {
            try
            {
                var config = Load(file);
                configs[config.Model] = config;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load AI config from '{file}': {ex.Message}", ex);
            }
        }
        return configs;
    }

    /// <summary>
    /// 単一の YAML ファイルから AI 設定を読み込みます。
    /// </summary>
    /// <param name="path">読み込む YAML ファイルパス。</param>
    /// <returns>パース済み AI 設定。</returns>
    public static AiConfig Load(string path)
    {
        var yaml = File.ReadAllText(path);
        return LoadFromString(yaml);
    }

    /// <summary>
    /// YAML 文字列から AI 設定を読み込みます。
    /// </summary>
    /// <param name="yaml">パース対象の YAML 文字列。</param>
    /// <returns>パース済み AI 設定。</returns>
    public static AiConfig LoadFromString(string yaml)
    {
        return Deserializer.Deserialize<AiConfig>(yaml);
    }
}

/// <summary>
/// Handles YAML where EffectPriorityEntry can be a plain int or an object.
///   deploy_free: 75          → EffectPriorityEntry { Priority = 75 }
///   budget_gain:
///     priority: 90
///     low_priority: 40       → EffectPriorityEntry { Priority = 90, LowPriority = 40, ... }
/// </summary>
internal sealed class EffectPriorityEntryConverter : IYamlTypeConverter
{
    /// <summary>
    /// この変換器が処理対象とする型か判定します。
    /// </summary>
    /// <param name="type">判定対象の型。</param>
    /// <returns>EffectPriorityEntry なら true。</returns>
    public bool Accepts(Type type) => type == typeof(EffectPriorityEntry);

    /// <summary>
    /// YAML から EffectPriorityEntry を読み出します。
    /// </summary>
    /// <param name="parser">YAML パーサ。</param>
    /// <param name="type">読み出し対象の型。</param>
    /// <param name="rootDeserializer">ネスト型のデシリアライズ用コールバック。</param>
    /// <returns>読み出した EffectPriorityEntry。</returns>
    public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        if (parser.TryConsume<Scalar>(out var scalar))
        {
            return new EffectPriorityEntry { Priority = int.Parse(scalar.Value) };
        }

        var entry = new EffectPriorityEntry();
        parser.Consume<MappingStart>();

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            switch (key)
            {
                case "priority":
                    entry.Priority = int.Parse(parser.Consume<Scalar>().Value);
                    break;
                case "low_priority":
                    entry.LowPriority = int.Parse(parser.Consume<Scalar>().Value);
                    break;
                case "threshold":
                    entry.Threshold = long.Parse(parser.Consume<Scalar>().Value);
                    break;
                case "hand_threshold":
                    entry.HandThreshold = int.Parse(parser.Consume<Scalar>().Value);
                    break;
                case "min_targets":
                    entry.MinTargets = int.Parse(parser.Consume<Scalar>().Value);
                    break;
                case "condition":
                    entry.Condition = (ConditionDef)rootDeserializer(typeof(ConditionDef))!;
                    break;
                default:
                    parser.SkipThisAndNestedEvents();
                    break;
            }
        }

        return entry;
    }

    /// <summary>
    /// 書き出しはサポートしないため常に例外を送出します。
    /// </summary>
    /// <param name="emitter">YAML 出力先。</param>
    /// <param name="value">書き出し対象の値。</param>
    /// <param name="type">書き出し対象の型。</param>
    /// <param name="rootSerializer">ネスト型のシリアライズ用コールバック。</param>
    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer rootSerializer)
    {
        throw new NotSupportedException();
    }
}

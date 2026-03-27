using System.Text.Json;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Registry for custom effect implementations that cannot be expressed with standard ops.
/// Builds Action&lt;OpContext&gt; closures from a custom name and optional YAML meta parameters.
/// </summary>
public interface ICustomEffectRegistry
{
    /// <summary>
    /// Builds a custom effect function for the given name and meta parameters.
    /// Returns null if the custom name is not registered.
    /// </summary>
    Action<OpContext>? Build(string customName, Dictionary<string, JsonElement>? meta);
}

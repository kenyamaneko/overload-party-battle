using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Tests;

internal static class EffectInfoTestExtensions
{
    public static EffectInfo WithCategory(this EffectInfo info, EffectCategory cat)
    {
        info.Categories.Add(cat);
        return info;
    }
}

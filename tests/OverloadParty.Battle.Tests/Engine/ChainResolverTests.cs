using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Tests for ChainResolver based on RULEBOOK.md §12:
/// - LIFO chain resolution
/// - Max chain level = 3
/// - Reactive cannot chain on unresolved reactive
/// </summary>
public class ChainResolverTests
{
    // ─── PushToChain ──────────────────────────────────────────

    [Fact]
    public void PushToChain_AssignsChainLevel()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });

        Assert.Single(state.ChainStack);
        Assert.Equal(1, state.ChainStack[0].ChainLevel);
    }

    [Fact]
    public void PushToChain_IncrementingLevels()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        Assert.Equal(2, state.ChainStack.Count);
        Assert.Equal(1, state.ChainStack[0].ChainLevel);
        Assert.Equal(2, state.ChainStack[1].ChainLevel);
    }

    /// <summary>
    /// Rulebook: 最大チェーン数 = 3
    /// </summary>
    [Fact]
    public void PushToChain_MaxChainLevel_Throws()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });

        var ex = Assert.Throws<GameRuleException>(() =>
            ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" }));
        Assert.Contains("chain stack full", ex.Message);
    }

    /// <summary>
    /// Rulebook: リアクティブに対して、別のリアクティブをチェーンすることはできない
    /// </summary>
    [Fact]
    public void PushToChain_ReactiveOnUnresolvedReactive_Throws()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        var ex = Assert.Throws<GameRuleException>(() =>
            ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" }));
        Assert.Contains("cannot chain reactive on unresolved reactive", ex.Message);
    }

    [Fact]
    public void PushToChain_ReactiveAfterResolvedReactive_OK()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        // Mark the reactive as resolved
        state.ChainStack[1].Resolved = true;

        // Should not throw — previous reactive is already resolved
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });
        Assert.Equal(3, state.ChainStack.Count);
    }

    // ─── CanChainReactive ─────────────────────────────────────

    [Fact]
    public void CanChainReactive_EmptyChain_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        Assert.False(ChainResolver.CanChainReactive(state));
    }

    [Fact]
    public void CanChainReactive_LastIsNotReactive_ReturnsTrue()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });

        Assert.True(ChainResolver.CanChainReactive(state));
    }

    [Fact]
    public void CanChainReactive_LastIsReactive_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        Assert.False(ChainResolver.CanChainReactive(state));
    }

    [Fact]
    public void CanChainReactive_FullChain_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });

        Assert.False(ChainResolver.CanChainReactive(state));
    }

    // ─── IsChainActive ────────────────────────────────────────

    [Fact]
    public void IsChainActive_NoEntries_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        Assert.False(ChainResolver.IsChainActive(state));
    }

    [Fact]
    public void IsChainActive_UnresolvedEntry_ReturnsTrue()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });

        Assert.True(ChainResolver.IsChainActive(state));
    }

    [Fact]
    public void IsChainActive_AllResolved_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        state.ChainStack[0].Resolved = true;

        Assert.False(ChainResolver.IsChainActive(state));
    }

    // ─── ChainActionToTrigger ─────────────────────────────────

    [Theory]
    [InlineData("reactive", TriggerType.Reactive)]
    [InlineData("attack", TriggerType.OnAttack)]
    [InlineData("unknown", TriggerType.Activate)]
    public void ChainActionToTrigger_MapsCorrectly(string action, TriggerType expected)
    {
        Assert.Equal(expected, ChainResolver.ChainActionToTrigger(action));
    }
}

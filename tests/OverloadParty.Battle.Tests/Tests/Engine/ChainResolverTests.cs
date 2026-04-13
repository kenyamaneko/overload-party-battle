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

        state.ChainStack.Should().ContainSingle();
        state.ChainStack[0].ChainLevel.Should().Be(1);
    }

    [Fact]
    public void PushToChain_IncrementingLevels()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        state.ChainStack.Should().HaveCount(2);
        state.ChainStack[0].ChainLevel.Should().Be(1);
        state.ChainStack[1].ChainLevel.Should().Be(2);
    }

    /// <summary>
    /// 最大チェーン数 = 3
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
        ex.Message.Should().Contain("chain stack full");
    }

    /// <summary>
    /// リアクティブに対して、別のリアクティブをチェーンすることはできない
    /// </summary>
    [Fact]
    public void PushToChain_ReactiveOnUnresolvedReactive_Throws()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        var ex = Assert.Throws<GameRuleException>(() =>
            ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" }));
        ex.Message.Should().Contain("cannot chain reactive on unresolved reactive");
    }

    [Fact]
    public void PushToChain_ReactiveAfterResolvedReactive_OK()
    {
        var state = TestFactory.MakeGameState();

        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        // Mark the reactive as resolved via public API
        ChainResolver.MarkResolved(state, chainLevel: 2);

        // Should not throw — previous reactive is already resolved
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });
        state.ChainStack.Should().HaveCount(3);
    }

    // ─── CanChainReactive ─────────────────────────────────────

    [Fact]
    public void CanChainReactive_EmptyChain_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.CanChainReactive(state).Should().BeFalse();
    }

    [Fact]
    public void CanChainReactive_LastIsNotReactive_ReturnsTrue()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });

        ChainResolver.CanChainReactive(state).Should().BeTrue();
    }

    [Fact]
    public void CanChainReactive_LastIsReactive_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "reactive" });

        ChainResolver.CanChainReactive(state).Should().BeFalse();
    }

    [Fact]
    public void CanChainReactive_FullChain_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "activate" });

        ChainResolver.CanChainReactive(state).Should().BeFalse();
    }

    // ─── IsChainActive ────────────────────────────────────────

    [Fact]
    public void IsChainActive_NoEntries_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.IsChainActive(state).Should().BeFalse();
    }

    [Fact]
    public void IsChainActive_UnresolvedEntry_ReturnsTrue()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });

        ChainResolver.IsChainActive(state).Should().BeTrue();
    }

    [Fact]
    public void IsChainActive_AllResolved_ReturnsFalse()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });
        ChainResolver.MarkResolved(state, chainLevel: 1);

        ChainResolver.IsChainActive(state).Should().BeFalse();
    }

    // ─── MarkResolved ─────────────────────────────────────────

    [Fact]
    public void MarkResolved_UnknownChainLevel_Throws()
    {
        var state = TestFactory.MakeGameState();
        ChainResolver.PushToChain(state, new ChainEntry { ActionType = "attack" });

        var act = () => ChainResolver.MarkResolved(state, chainLevel: 99);

        act.Should().Throw<GameRuleException>().WithMessage("*chain level 99*");
    }
}

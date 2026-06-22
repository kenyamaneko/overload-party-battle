using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc.Strategies;

/// <summary>
/// PlayCard 系の候補リスト構築を共通化する。
/// 各 Deploy/Immediate 戦略が繰り返していた
/// "ResolveCard → filter → priority → sort" の定型を 1 箇所に集約する。
/// </summary>
internal static class CardCandidateBuilder
{
    public record Candidate<TExtra>(GD.PlayCardAction Action, CardDefinition Card, int Priority, TExtra Extra);

    /// <summary>
    /// playActions を predicate でフィルタし、projector で (priority, extra) を計算。
    /// projector が null を返した候補は除外する。
    /// 結果は priority 降順でソート済み。
    /// </summary>
    public static List<Candidate<TExtra>> Build<TExtra>(
        IEnumerable<GD.PlayCardAction> playActions,
        ICardCache cardCache,
        Func<CardDefinition, bool> predicate,
        Func<GD.PlayCardAction, CardDefinition, (int Priority, TExtra Extra)?> projector)
    {
        return playActions
            .Select(a => (Action: a, Card: MustGet(cardCache, a.CardID)))
            .Where(p => predicate(p.Card))
            .Select(p => (p.Action, p.Card, Projected: projector(p.Action, p.Card)))
            .Where(p => p.Projected.HasValue)
            .Select(p => new Candidate<TExtra>(p.Action, p.Card, p.Projected!.Value.Priority, p.Projected.Value.Extra))
            .OrderByDescending(c => c.Priority)
            .ToList();
    }

    private static CardDefinition MustGet(ICardCache cc, string cardId) =>
        cc.Get(cardId)
            ?? throw new InvalidOperationException($"Card '{cardId}' not found in card cache");
}

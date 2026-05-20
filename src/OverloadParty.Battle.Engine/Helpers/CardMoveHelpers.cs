using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// カードのゾーン間移動（リポジトリ→手札、手札→トラッシュ等）を扱うヘルパー。
/// </summary>
public static class CardMoveHelpers
{
    /// <summary>
    /// リポジトリからカードを引いて手札に加える。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="count">引く枚数。</param>
    /// <returns>実際に引いた枚数。</returns>
    public static int DrawCards(BattleGameState state, long playerNum, int count)
    {
        var repo = state.GetRepository(playerNum);
        var hand = state.GetHand(playerNum);

        int toDraw = Math.Min(count, repo.Count);
        for (int i = 0; i < toDraw; i++)
        {
            var card = repo[0];
            repo.RemoveAt(0);
            hand.Add(new UndeployedCard
            {
                InstanceID = state.NextInstanceID(),
                CardID = card.CardID,
                ArtNo = card.ArtNo,
            });
        }
        return toDraw;
    }

    /// <summary>
    /// リポジトリから条件に合うカードを1枚探して手札に加える。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="predicate">カードの一致判定述語。</param>
    /// <returns>該当カードを見つけて手札に加えたか。</returns>
    public static bool SearchRepo(BattleGameState state, long playerNum, Predicate<UndeployedCard> predicate)
    {
        var repo = state.GetRepository(playerNum);
        var found = repo.Find(predicate);
        if (found is null)
        {
            return false;
        }

        repo.Remove(found);
        var hand = state.GetHand(playerNum);
        hand.Add(new UndeployedCard
        {
            InstanceID = state.NextInstanceID(),
            CardID = found.CardID,
            ArtNo = found.ArtNo,
        });
        return true;
    }

    /// <summary>
    /// 指定カードIDで新しいカードを手札に加える。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="cardID">追加するカードの ID。</param>
    public static void AddToHand(BattleGameState state, long playerNum, string cardID)
    {
        var hand = state.GetHand(playerNum);
        hand.Add(new UndeployedCard
        {
            InstanceID = state.NextInstanceID(),
            CardID = cardID,
        });
    }

    /// <summary>
    /// トラッシュからカードを手札に戻す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="instanceID">トラッシュから戻すカードのインスタンス ID。</param>
    /// <returns>該当カードを見つけて戻したか。</returns>
    public static bool TrashToHand(BattleGameState state, long playerNum, string instanceID)
    {
        var trash = state.GetTrash(playerNum);
        var idx = trash.FindIndex(c => c.InstanceID == instanceID);
        if (idx < 0)
        {
            return false;
        }

        var card = trash[idx];
        trash.RemoveAt(idx);

        var hand = state.GetHand(playerNum);
        hand.Add(new UndeployedCard
        {
            InstanceID = state.NextInstanceID(),
            CardID = card.CardID,
            ArtNo = card.ArtNo,
        });
        return true;
    }

    /// <summary>
    /// カードをトラッシュに加える。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="cardID">追加するカードの ID。</param>
    /// <param name="instanceID">カードのインスタンス ID。</param>
    /// <param name="artNo">アート番号。</param>
    public static void AddToTrash(BattleGameState state, long playerNum, string cardID, string instanceID, long artNo = 0)
    {
        var trash = state.GetTrash(playerNum);
        trash.Add(new UndeployedCard { InstanceID = instanceID, CardID = cardID, ArtNo = artNo });
    }

    /// <summary>
    /// 手札からカードを捨ててトラッシュに移動する。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="cardInstanceIDs">捨てるカードのインスタンス ID 一覧。</param>
    /// <returns>実際に捨てた枚数。</returns>
    public static int DiscardCards(BattleGameState state, long playerNum, List<string> cardInstanceIDs)
    {
        var hand = state.GetHand(playerNum);
        var discardSet = new HashSet<string>(cardInstanceIDs);
        var discardedCards = new List<UndeployedCard>();

        for (int i = hand.Count - 1; i >= 0; i--)
        {
            if (discardSet.Remove(hand[i].InstanceID))
            {
                discardedCards.Add(hand[i]);
                hand.RemoveAt(i);
            }
        }

        if (discardSet.Count > 0)
        {
            throw new GameRuleException($"some cards not found in hand: {string.Join(", ", discardSet)}");
        }

        foreach (var card in discardedCards)
        {
            AddToTrash(state, playerNum, card.CardID, card.InstanceID, card.ArtNo);
        }

        return discardedCards.Count;
    }
}

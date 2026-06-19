using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// プレイヤーがデッキで区分ごと (ルーチン / スペシャル) にセットした施策 ID を状態から解決します。
/// </summary>
public static class InitiativeSelection
{
    /// <summary>指定プレイヤーが該当区分にセットした施策 ID を返します。</summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号。</param>
    /// <param name="kind">施策の区分 (ルーチン / スペシャル)。</param>
    /// <returns>セットした施策の ID。</returns>
    public static string ResolveId(BattleGameState state, long playerNum, string kind) => (playerNum, kind) switch
    {
        (1, InitiativeKinds.Routine) => state.Player1RoutineId,
        (1, InitiativeKinds.Special) => state.Player1SpecialId,
        (2, InitiativeKinds.Routine) => state.Player2RoutineId,
        (2, InitiativeKinds.Special) => state.Player2SpecialId,
        _ => throw new ArgumentOutOfRangeException(
            nameof(playerNum), $"no initiative for (playerNum={playerNum}, kind={kind})"),
    };
}

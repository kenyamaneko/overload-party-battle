using System.Collections;

namespace OverloadParty.Battle.Models;

/// <summary>
/// Fixed-capacity zone with nullable slots.
/// Implements IEnumerable&lt;T&gt; yielding only non-null items,
/// while preserving slot-based indexing for direct access.
/// </summary>
public class Zone<T> : IEnumerable<T> where T : class
{
    private readonly T?[] _slots;

    public int Capacity { get; }

    public Zone(int capacity = BattleConstants.SlotsPerZone)
    {
        Capacity = capacity;
        _slots = new T?[capacity];
    }

    /// <summary>
    /// Direct slot access for PlayCardProcessor and serialization.
    /// </summary>
    public T? this[int index]
    {
        get => _slots[index];
        set => _slots[index] = value;
    }

    /// <summary>
    /// Returns the index of the first empty slot, or -1 if full.
    /// </summary>
    /// <returns>最初の空きスロットのインデックス。空きがなければ -1</returns>
    public int FindEmptySlot()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is null)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Removes the first item matching the predicate. Returns true if removed.
    /// </summary>
    /// <param name="predicate">削除対象を判定する述語</param>
    /// <returns>削除に成功した場合 true</returns>
    public bool Remove(Func<T, bool> predicate)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is { } item && predicate(item))
            {
                _slots[i] = null;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Removes all items matching the predicate. Returns the number of items removed.
    /// </summary>
    /// <param name="predicate">削除対象を判定する述語</param>
    /// <returns>削除した要素数</returns>
    public int RemoveAll(Func<T, bool> predicate)
    {
        int removed = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is { } item && predicate(item))
            {
                _slots[i] = null;
                removed++;
            }
        }
        return removed;
    }

    /// <summary>
    /// Places an item in the first empty slot. Returns true if placed.
    /// </summary>
    /// <param name="item">配置するアイテム</param>
    /// <returns>配置に成功した場合 true</returns>
    public bool TryPlace(T item)
    {
        var slot = FindEmptySlot();
        if (slot < 0)
        {
            return false;
        }
        _slots[slot] = item;
        return true;
    }

    /// <summary>
    /// Enumerates indices of empty (null) slots.
    /// </summary>
    /// <returns>空きスロットのインデックスの列</returns>
    public IEnumerable<int> EmptySlotIndices()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is null)
            {
                yield return i;
            }
        }
    }

    /// <summary>
    /// Returns a copy of the internal slot array for serialization.
    /// </summary>
    /// <returns>スロット配列のコピー</returns>
    public T?[] ToArray() => (T?[])_slots.Clone();

    /// <summary>
    /// Enumerates only non-null items.
    /// </summary>
    /// <returns>非 null アイテムの列挙子</returns>
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var slot in _slots)
        {
            if (slot is not null)
            {
                yield return slot;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

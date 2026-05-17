namespace YieldSaga;

/// <summary>
/// Spec §8: 次の Take までの (Effect, State) 列をまとめた塊。
/// Dispatcher が <see cref="Current"/> を 1 つずつ進めながら handler 群を呼ぶ。
/// Handler は <see cref="Remaining"/> で先読みしたり <see cref="TakeWhileAndMark"/> で
/// 連続同型 effect をまとめて取って <see cref="MarkAsProcessed"/> し、Dispatcher が次の
/// effect でその handler を再び呼んだとき <see cref="IsProcessed"/> で重複処理を回避する。
///
/// 並列実行に備え、Mark/Check 系は内部ロックで保護する。
/// </summary>
public sealed class EffectBatch<TState>
{
    private readonly IReadOnlyList<(Effect Effect, TState State)> _items;
    private readonly HashSet<(object Handler, Effect Effect)> _marks = new();
    private readonly object _lock = new();

    public EffectBatch(IReadOnlyList<(Effect Effect, TState State)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    /// <summary>現在指している位置（0 起点）。終端を越えると <see cref="HasCurrent"/> が false。</summary>
    public int Position { get; private set; }

    public int Count => _items.Count;

    /// <summary>有効な Current を指しているか。</summary>
    public bool HasCurrent => Position < _items.Count;

    /// <summary>現在処理中の Effect。<see cref="HasCurrent"/> が false の時に読むと例外。</summary>
    public Effect Current => _items[Position].Effect;

    /// <summary>現在 Effect 適用直後の State。</summary>
    public TState CurrentState => _items[Position].State;

    /// <summary>Current より後ろの (effect, state) 列（Current は含まない）。</summary>
    public IEnumerable<(Effect Effect, TState State)> Remaining
    {
        get
        {
            for (int i = Position + 1; i < _items.Count; i++)
                yield return _items[i];
        }
    }

    /// <summary>Dispatcher が呼ぶ。Position を 1 進めて、有効ならば true。</summary>
    internal bool MoveNext()
    {
        Position++;
        return Position < _items.Count;
    }

    /// <summary>この handler がこの effect をもう処理した、とマークする（先読み消化用）。</summary>
    public void MarkAsProcessed(object handler, Effect effect)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(effect);
        lock (_lock) _marks.Add((handler, effect));
    }

    public bool IsProcessed(object handler, Effect effect)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(effect);
        lock (_lock) return _marks.Contains((handler, effect));
    }

    /// <summary>
    /// Current を含む連続 effect から、T 型かつ <paramref name="pred"/> を満たすものを並びで取り、
    /// すべて <paramref name="handler"/> に対して MarkAsProcessed する。
    /// 最初の不一致で打ち切る。次の effect で handler が再び呼ばれた時 <see cref="IsProcessed"/>
    /// が true を返すので、handler 側でスキップできる。
    /// </summary>
    public IReadOnlyList<(T Effect, TState State)> TakeWhileAndMark<T>(object handler, Func<T, bool> pred)
        where T : Effect
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(pred);
        var result = new List<(T, TState)>();
        for (int i = Position; i < _items.Count; i++)
        {
            if (_items[i].Effect is T typed && pred(typed))
            {
                MarkAsProcessed(handler, _items[i].Effect);
                result.Add((typed, _items[i].State));
            }
            else
            {
                break;
            }
        }
        return result;
    }
}

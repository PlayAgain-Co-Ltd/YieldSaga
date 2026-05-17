namespace YieldSaga;

/// <summary>
/// Spec §6: 入力待ち中断シグナル。Prompt.Accepts を満たす入力で TryResolve すると IsResolved=true。
/// 1 回のみ解決可能。
/// </summary>
public sealed record Take(Prompt Prompt) : Signal
{
    private object? _resolved;

    public bool IsResolved { get; private set; }

    public object? ResolvedInput => IsResolved ? _resolved : null;

    public bool TryResolve(object input)
    {
        if (IsResolved) return false;
        if (!Prompt.Accepts(input)) return false;
        _resolved = input;
        IsResolved = true;
        return true;
    }
}

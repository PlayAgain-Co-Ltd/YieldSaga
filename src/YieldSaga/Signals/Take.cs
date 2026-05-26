namespace YieldSaga;

/// <summary>
/// Spec §6: 入力待ち中断シグナル。Prompt.Accepts を満たす入力で TryResolve すると IsResolved=true。
/// 1 回のみ解決可能。
///
/// typed 版は <see cref="Take{TInput}"/>。`Read.Input&lt;T&gt;(out var x, prompt)` で生成すると
/// 再開後 <c>x.Value</c> で cast 無しに入力にアクセスできる。
/// </summary>
public record Take(Prompt Prompt) : Signal
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

/// <summary>
/// Spec §6: 入力型を静的に保持する Take。`Read.Input&lt;TInput&gt;(out var x, prompt)` の戻り型 兼 ホルダー。
/// 再開後 <see cref="Value"/> で cast / null-forgive 無しに typed 入力を取得できる。
/// Runtime は基底の <see cref="Take"/> として扱うので Pipeline / Runtime は無改造。
/// </summary>
public sealed record Take<TInput>(Prompt Prompt) : Take(Prompt) where TInput : notnull
{
    /// <summary>解決済み入力を typed で返す。未解決時は <see cref="InvalidCastException"/>。</summary>
    public TInput Value => (TInput)ResolvedInput!;
}

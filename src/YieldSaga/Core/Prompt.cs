namespace YieldSaga;

/// <summary>
/// Spec §6: 何を待つかの仕様。Take が保持する。
/// </summary>
public abstract record Prompt
{
    public abstract bool Accepts(object input);

    /// <summary>入力なしで自動解決できるなら入力を返す。さもなくば null。</summary>
    public virtual object? GetAutoResolution() => null;
}

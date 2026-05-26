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

/// <summary>
/// 入力型を静的に保持する Prompt。`Accepts(TInput)` 実装で pattern match (`is T x &&`) が不要になり、
/// `Read.Input(out var x, prompt)` の型引数も推論される。SF2 の <c>BattleInputPrompt</c> は <c>PlayerInput</c>
/// 基底で半 typed だったが、YieldSaga には共通入力基底が無いので完全 typed にする方が旨味が大きい。
/// </summary>
public abstract record Prompt<TInput> : Prompt where TInput : notnull
{
    /// <summary>untyped 経路から typed Accepts に橋渡し。派生は typed 版だけ実装する。</summary>
    public sealed override bool Accepts(object input) => input is TInput typed && Accepts(typed);

    /// <summary>typed 入力受け入れ判定。</summary>
    public abstract bool Accepts(TInput input);

    /// <summary>
    /// typed 自動解決。解決可能なら <c>true</c> + <paramref name="input"/>。さもなくば <c>false</c>。
    /// (struct も class も受けるため <c>T?</c> 戻り値は避ける — notnull 制約下の <c>T?</c> は
    /// reference type 専用 nullable annotation で struct で動かない)
    /// </summary>
    public virtual bool TryGetAutoResolution(out TInput input)
    {
        input = default!;
        return false;
    }

    public sealed override object? GetAutoResolution()
        => TryGetAutoResolution(out var input) ? input : null;
}

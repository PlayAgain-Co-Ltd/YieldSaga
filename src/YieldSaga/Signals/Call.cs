namespace YieldSaga;

/// <summary>
/// Spec §2/§9: Intent を展開せよという信号。
///
/// <see cref="Scope"/> は **per-Call SignalScope**。Pipeline が Call を展開するとき、
/// dispatch scope (Runtime が組み立てる) と Merge して内側の再帰 (サブ Call / Interceptor 発の Signal も含む)
/// に持ち回る。SF2 の <c>WithTargetEnemy(invocation, id)</c> パターンに相当する書き味を提供する:
/// <code>
/// yield return new Call(intent, sender,
///     Scope: SignalScope.Empty.OnTake(t => t.Prompt is SelectEnemyPrompt ? id : null));
/// </code>
/// </summary>
public sealed record Call(
    Intent Intent,
    Sender Sender,
    SignalScope? Scope = null
) : Signal;

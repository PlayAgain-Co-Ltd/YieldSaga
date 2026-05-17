namespace YieldSaga;

/// <summary>
/// Spec §5: Event の発行に介入してルールを書き換える。
/// <paramref name="preState"/> は「この Event が適用される前」の State スナップショット。
/// </summary>
public interface IInterceptor<in TState, in TEvent> where TEvent : Event
{
    InterceptResult Apply(TEvent ev, TState preState);
}

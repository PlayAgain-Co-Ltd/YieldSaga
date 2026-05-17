namespace YieldSaga;

/// <summary>
/// Spec §4: Event を State に反映する純粋関数。副作用禁止、1 Event = 1 Applier。
/// </summary>
public interface IEventApplier<TState, in TEvent> where TEvent : Event
{
    TState Apply(TState state, TEvent ev);
}

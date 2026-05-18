namespace YieldSaga;

/// <summary>
/// Spec §10: 異なる State 型の Runtime 同士を繋ぐ単方向 adapter。
///
/// 出処側 Runtime の <typeparamref name="TFromEvent"/> が EffectDispatcher に流れたとき、
/// <see cref="BridgeHandler{TFromEvent, TToInput, TFromState, TToState}"/> 経由で呼ばれ、
/// 戻り値の <typeparamref name="TToInput"/> 列を宛先 Runtime の <c>Update</c> に同期で流す。
///
/// 単方向のみ（B→A も必要なら別 Bridge を書く）。MetaRuntime のような統合点は提供しない。
/// 複数 Runtime を Bridge で繋ぐと結果として 1 入力で全 State 横断のカスケードが立ち上がる。
/// </summary>
public interface IBridge<in TFromEvent, out TToInput> where TFromEvent : Event
{
    IEnumerable<TToInput> Bridge(TFromEvent ev);
}

namespace YieldSaga;

/// <summary>
/// Spec §3: Saga が展開した Effect 列を外側で wrap する。
/// TIntent は登録ディスパッチ用の型タグであり、決定的には Intent 継承チェーンに沿って積層する
/// （基底型に登録した Decorator は最外層 wrapper として走る）。
/// </summary>
public interface ISagaDecorator<in TIntent> where TIntent : Intent
{
    IEnumerable<Effect> Decorate(IEnumerable<Effect> effects);
}

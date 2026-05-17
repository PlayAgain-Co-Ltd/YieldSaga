namespace YieldSaga;

/// <summary>
/// Spec §5: この Interceptor / SagaDecorator が指定型より**後に**走るよう要求する。
/// 同じ Event 型 / Intent 型レベルで構築時にトポロジカルソートされる。
/// 循環は構築時に例外として検出される。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RunAfterAttribute(Type target) : Attribute
{
    public Type Target { get; } = target ?? throw new ArgumentNullException(nameof(target));
}

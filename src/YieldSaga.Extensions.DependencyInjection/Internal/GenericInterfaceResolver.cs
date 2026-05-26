namespace YieldSaga.Extensions.DependencyInjection.Internal;

internal static class GenericInterfaceResolver
{
    public static IReadOnlyList<Type[]> ResolveAll(Type implType, Type openGeneric)
    {
        ArgumentNullException.ThrowIfNull(implType);
        ArgumentNullException.ThrowIfNull(openGeneric);
        if (!openGeneric.IsGenericTypeDefinition)
            throw new ArgumentException($"{openGeneric} must be an open generic type definition (e.g. typeof(ISaga<>)).", nameof(openGeneric));

        var matches = new List<Type[]>();
        foreach (var i in implType.GetInterfaces())
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == openGeneric)
                matches.Add(i.GetGenericArguments());
        }
        return matches;
    }
}

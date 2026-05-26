namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddSagaDecoratorTests
{
    [Fact]
    public void Decorator_wraps_saga_effect_stream()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddSagaDecorator<PrefixDecorator>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(1), new TestSender());

        Assert.Equal(new[] { "before", "after" }, sp.GetRequiredService<PrefixDecorator>().Log);
    }
}

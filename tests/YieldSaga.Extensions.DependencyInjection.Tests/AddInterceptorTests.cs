namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddInterceptorTests
{
    [Fact]
    public void Interceptor_runs_via_Runtime()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddInterceptor<DoubleIncrementInterceptor>());
        using var sp = services.BuildServiceProvider();

        var runtime = sp.GetRequiredService<Runtime<CounterState>>();
        runtime.Dispatch(new IncrementIntent(5), new TestSender());

        // DoubleIncrementInterceptor replaces IncrementedEvent(5) with IncrementedEvent(10).
        Assert.Equal(10, runtime.Latest.Value);
    }
}

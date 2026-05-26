namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class LifecycleTests
{
    [Fact]
    public void Runtime_is_singleton_within_a_provider()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        var a = sp.GetRequiredService<Runtime<CounterState>>();
        var b = sp.GetRequiredService<Runtime<CounterState>>();

        Assert.Same(a, b);
    }

    [Fact]
    public void Saga_instance_is_singleton_and_reused_in_Runtime()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        var direct = sp.GetRequiredService<IncrementSaga>();
        // Resolve runtime so the Saga is also fetched via the deferred registration.
        sp.GetRequiredService<Runtime<CounterState>>();
        var direct2 = sp.GetRequiredService<IncrementSaga>();

        Assert.Same(direct, direct2);
    }

    [Fact]
    public void State_is_shared_across_resolves_of_the_same_Runtime()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<Runtime<CounterState>>().Dispatch(new IncrementIntent(3), new TestSender());
        var runtime = sp.GetRequiredService<Runtime<CounterState>>();

        Assert.Equal(3, runtime.Latest.Value);
    }

    [Fact]
    public void Two_runtimes_for_different_TState_are_independent()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>());
        services.AddYieldSagaRuntime<OtherState>(b => b
            .WithInitialState(OtherState.Initial)
            .AddSaga<TaggedSaga>()
            .AddEventApplier<TaggedApplier>());
        using var sp = services.BuildServiceProvider();

        var counter = sp.GetRequiredService<Runtime<CounterState>>();
        var other = sp.GetRequiredService<Runtime<OtherState>>();

        counter.Dispatch(new IncrementIntent(2), new TestSender());
        other.Dispatch(new TaggedIntent("hello"), new TestSender());

        Assert.Equal(2, counter.Latest.Value);
        Assert.Equal("hello", other.Latest.Tag);
    }
}

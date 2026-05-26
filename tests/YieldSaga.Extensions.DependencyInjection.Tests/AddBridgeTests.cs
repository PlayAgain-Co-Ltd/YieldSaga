namespace YieldSaga.Extensions.DependencyInjection.Tests;

public sealed class AddBridgeTests
{
    [Fact]
    public async Task One_way_bridge_drives_destination_runtime_via_dispatcher()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddBridge<IncrementedEvent, IncrementToTagInput, OtherState>(
                new IncrementToTagBridge(),
                _ => new TestSender()));
        services.AddYieldSagaRuntime<OtherState>(b => b
            .WithInitialState(OtherState.Initial)
            .AddSaga<TaggedSaga>()
            .AddEventApplier<TaggedApplier>()
            .AddIntentProducer<TagInputProducer>());
        using var sp = services.BuildServiceProvider();

        var counter = sp.GetRequiredService<Runtime<CounterState>>();
        var other = sp.GetRequiredService<Runtime<OtherState>>();
        var dispatcher = sp.GetRequiredService<EffectDispatcher<CounterState>>();

        var trace = counter.Dispatch(new IncrementIntent(3), new TestSender());
        await dispatcher.DispatchAsync(new EffectBatch<CounterState>(trace));

        Assert.Equal(3, counter.Latest.Value);
        Assert.Equal("+3", other.Latest.Tag);
    }

    [Fact]
    public void Cyclic_bridge_registration_does_not_stack_overflow_on_BuildServiceProvider()
    {
        // A↔B bridges. We just verify that resolving both runtimes doesn't blow up.
        // (Bridge dispatch is lazy, so the cycle only matters if effects actually fire.)
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<CounterState>(b => b
            .WithInitialState(CounterState.Zero)
            .AddSaga<IncrementSaga>()
            .AddEventApplier<IncrementApplier>()
            .AddBridge<IncrementedEvent, IncrementToTagInput, OtherState>(
                new IncrementToTagBridge(),
                _ => new TestSender()));
        services.AddYieldSagaRuntime<OtherState>(b => b
            .WithInitialState(OtherState.Initial)
            .AddSaga<TaggedSaga>()
            .AddEventApplier<TaggedApplier>()
            .AddIntentProducer<TagInputProducer>()
            // synthetic reverse bridge from B back to A — currently doesn't actually fire
            // since TaggedEvent isn't dispatched here, but registration must not deadlock.
            .AddBridge<TaggedEvent, int, CounterState>(
                new ReverseBridge(),
                _ => new TestSender()));
        using var sp = services.BuildServiceProvider();

        var a = sp.GetRequiredService<Runtime<CounterState>>();
        var b = sp.GetRequiredService<Runtime<OtherState>>();
        // Resolving both must succeed.
        Assert.NotNull(a);
        Assert.NotNull(b);

        // Dispatcher resolution forces EffectHandlerRegistry build for both states.
        Assert.NotNull(sp.GetRequiredService<EffectDispatcher<CounterState>>());
        Assert.NotNull(sp.GetRequiredService<EffectDispatcher<OtherState>>());
    }

    private sealed class ReverseBridge : IBridge<TaggedEvent, int>
    {
        public IEnumerable<int> Bridge(TaggedEvent ev) { yield return 1; }
    }
}

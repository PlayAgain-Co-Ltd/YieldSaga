namespace YieldSaga.Extensions.DependencyInjection.Tests;

// ─── Test domain (designed so every fixture in this assembly can coexist under a full scan) ────

public sealed record CounterState(int Value)
{
    public static CounterState Zero { get; } = new(0);
}

public sealed record OtherState(string Tag)
{
    public static OtherState Initial { get; } = new("init");
}

// Intents (each intent is handled by exactly one ISaga in this assembly).
public sealed record IncrementIntent(int Amount) : Intent;
public sealed record DecrementIntent(int Amount) : Intent;
public sealed record TaggedIntent(string Tag) : Intent;
public sealed record MultiIntent1(int Amount) : Intent;
public sealed record MultiIntent2(int Amount) : Intent;

// Events (each event is handled by exactly one IEventApplier in this assembly).
public sealed record IncrementedEvent(int Amount) : Event;
public sealed record DecrementedEvent(int Amount) : Event;
public sealed record TaggedEvent(string Tag) : Event;
public sealed record MultiEvent1(int Amount) : Event;
public sealed record MultiEvent2(int Amount) : Event;
public sealed record WrongOnlyEvent(string Note) : Event;

public sealed record TestSender : Sender;

// ─── Sagas ───────────────────────────────────────────────────

public sealed class IncrementSaga : ISaga<IncrementIntent>
{
    public IEnumerable<Effect> Run(IncrementIntent intent, Sender sender)
    {
        yield return new IncrementedEvent(intent.Amount);
    }
}

public sealed class DecrementSaga : ISaga<DecrementIntent>
{
    public IEnumerable<Effect> Run(DecrementIntent intent, Sender sender)
    {
        yield return new DecrementedEvent(intent.Amount);
    }
}

public sealed class TaggedSaga : ISaga<TaggedIntent>
{
    public IEnumerable<Effect> Run(TaggedIntent intent, Sender sender)
    {
        yield return new TaggedEvent(intent.Tag);
    }
}

// Multi-interface saga — exclusive intents so it never collides with the single-intent sagas
// above, even under a full assembly scan.
public sealed class MultiSaga : ISaga<MultiIntent1>, ISaga<MultiIntent2>
{
    public IEnumerable<Effect> Run(MultiIntent1 intent, Sender sender)
    {
        yield return new MultiEvent1(intent.Amount);
    }
    public IEnumerable<Effect> Run(MultiIntent2 intent, Sender sender)
    {
        yield return new MultiEvent2(intent.Amount);
    }
}

// Type with no ISaga<> implementation — used to verify the 0-match error path.
public sealed class NotASaga { }

// ─── Appliers ────────────────────────────────────────────────

public sealed class IncrementApplier : IEventApplier<CounterState, IncrementedEvent>
{
    public CounterState Apply(CounterState s, IncrementedEvent e) => s with { Value = s.Value + e.Amount };
}

public sealed class DecrementApplier : IEventApplier<CounterState, DecrementedEvent>
{
    public CounterState Apply(CounterState s, DecrementedEvent e) => s with { Value = s.Value - e.Amount };
}

public sealed class TaggedApplier : IEventApplier<OtherState, TaggedEvent>
{
    public OtherState Apply(OtherState s, TaggedEvent e) => s with { Tag = e.Tag };
}

// Multi-interface applier — exclusive events so it never collides under scan.
public sealed class MultiApplier
    : IEventApplier<CounterState, MultiEvent1>, IEventApplier<CounterState, MultiEvent2>
{
    public CounterState Apply(CounterState s, MultiEvent1 e) => s with { Value = s.Value + e.Amount };
    public CounterState Apply(CounterState s, MultiEvent2 e) => s with { Value = s.Value - e.Amount };
}

// Applier targeting a different state — wrong-state error path; uses a dedicated event so it does
// not conflict with TaggedApplier under a scan of OtherState builders.
public sealed class WrongStateApplier : IEventApplier<OtherState, WrongOnlyEvent>
{
    public OtherState Apply(OtherState s, WrongOnlyEvent e) => s with { Tag = e.Note };
}

// ─── Interceptors ────────────────────────────────────────────

public sealed class DoubleIncrementInterceptor : IInterceptor<CounterState, IncrementedEvent>
{
    public InterceptResult Apply(IncrementedEvent ev, CounterState preState)
        => InterceptResult.Emit(new IncrementedEvent(ev.Amount * 2));
}

// ─── Producers ───────────────────────────────────────────────

public sealed class IntInputProducer : IIntentProducer<int, CounterState>
{
    public bool CanProduce(int input, CounterState state) => true;
    public Intent Produce(int input, CounterState state) => new IncrementIntent(input);
}

public sealed class StopAtTenAutoProducer : IAutoIntentProducer<CounterState>
{
    public bool CanProduce(CounterState state) => state.Value < 10;
    public Intent Produce(CounterState state) => new IncrementIntent(1);
}

// ─── EffectHandlers ──────────────────────────────────────────

public sealed class RecordingHandler : IEffectHandler<IncrementedEvent, CounterState>
{
    public List<int> Seen { get; } = new();
    public Task OnEffectAsync(IncrementedEvent effect, CounterState state, EffectBatch<CounterState> batch)
    {
        Seen.Add(effect.Amount);
        return Task.CompletedTask;
    }
}

// ─── SagaDecorators ──────────────────────────────────────────

public sealed class PrefixDecorator : ISagaDecorator<IncrementIntent>
{
    public List<string> Log { get; } = new();
    public IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
    {
        Log.Add("before");
        foreach (var e in effects) yield return e;
        Log.Add("after");
    }
}

// ─── Bridges ─────────────────────────────────────────────────

public sealed record IncrementToTagInput(string Tag);

public sealed class IncrementToTagBridge : IBridge<IncrementedEvent, IncrementToTagInput>
{
    public IEnumerable<IncrementToTagInput> Bridge(IncrementedEvent ev)
    {
        yield return new IncrementToTagInput($"+{ev.Amount}");
    }
}

public sealed class TagInputProducer : IIntentProducer<IncrementToTagInput, OtherState>
{
    public bool CanProduce(IncrementToTagInput input, OtherState state) => true;
    public Intent Produce(IncrementToTagInput input, OtherState state) => new TaggedIntent(input.Tag);
}

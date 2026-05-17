namespace YieldSaga.Tests.Integration;

/// <summary>
/// Spec §6 / §7: 未解決 Take で中断、入力到着で Resume。
/// 鍵となるのは「saga が yield return take した後、Resume 後の MoveNext で saga 内の
/// 次行が実行され、そこで take.ResolvedInput を読める」という C# iterator の挙動。
/// </summary>
public class TakePauseTests
{
    private sealed record Game(int Number, int? Guess);
    private sealed record SubmitGuessIntent : Intent;
    private sealed record System(string Name) : Sender;

    private sealed record GuessedEvent(int Value) : Event;

    private sealed record AnyIntPrompt : Prompt
    {
        public override bool Accepts(object input) => input is int;
    }

    private sealed record OnlySevenPrompt : Prompt
    {
        public override bool Accepts(object input) => input is int n && n == 7;
    }

    private sealed record AutoZeroPrompt : Prompt
    {
        public override bool Accepts(object input) => input is int;
        public override object? GetAutoResolution() => 0;
    }

    private sealed class GuessSaga : ISaga<SubmitGuessIntent>
    {
        private readonly Prompt _prompt;
        public GuessSaga(Prompt prompt) { _prompt = prompt; }
        public IEnumerable<Effect> Run(SubmitGuessIntent intent, Sender sender)
        {
            var take = new Take(_prompt);
            yield return take;
            // C# iterator は yield 後の続きを Resume 時に実行する。
            yield return new GuessedEvent((int)take.ResolvedInput!);
        }
    }

    private sealed class GuessedApplier : IEventApplier<Game, GuessedEvent>
    {
        public Game Apply(Game state, GuessedEvent ev) => state with { Guess = ev.Value };
    }

    private static Runtime<Game> Build(Prompt prompt)
    {
        var sagas = new SagaRegistry();
        sagas.Register(new GuessSaga(prompt));
        var appliers = new EventApplierRegistry<Game>();
        appliers.Register(new GuessedApplier());
        return new Runtime<Game>(new Game(42, null), new EffectPipeline(sagas), appliers);
    }

    [Fact]
    public void Pauses_at_unresolved_Take()
    {
        var rt = Build(new AnyIntPrompt());
        var trace = rt.Dispatch(new SubmitGuessIntent(), new System("t"));

        var single = Assert.Single(trace);
        Assert.IsType<Take>(single.Effect);
        Assert.True(rt.IsPaused);
        Assert.Same(single.Effect, rt.PendingTake);
        Assert.Null(rt.Latest.Guess);
    }

    [Fact]
    public void Resume_with_accepted_input_continues_and_updates_state()
    {
        var rt = Build(new AnyIntPrompt());
        rt.Dispatch(new SubmitGuessIntent(), new System("t"));

        var trace = rt.Resume(17);

        Assert.False(rt.IsPaused);
        Assert.Null(rt.PendingTake);
        var single = Assert.Single(trace);
        Assert.Equal(new GuessedEvent(17), single.Effect);
        Assert.Equal(17, rt.Latest.Guess);
    }

    [Fact]
    public void Resume_with_rejected_input_stays_paused()
    {
        var rt = Build(new OnlySevenPrompt());
        rt.Dispatch(new SubmitGuessIntent(), new System("t"));

        var trace = rt.Resume(3);

        Assert.Empty(trace);
        Assert.True(rt.IsPaused);
        Assert.Null(rt.Latest.Guess);

        // 受け入れる入力を渡せば続行できる
        var ok = rt.Resume(7);
        Assert.Equal(new GuessedEvent(7), ok[0].Effect);
        Assert.False(rt.IsPaused);
    }

    [Fact]
    public void Auto_resolution_does_not_pause()
    {
        var rt = Build(new AutoZeroPrompt());
        var trace = rt.Dispatch(new SubmitGuessIntent(), new System("t"));

        Assert.False(rt.IsPaused);
        Assert.Equal(2, trace.Count); // Take + Event
        Assert.IsType<Take>(trace[0].Effect);
        Assert.Equal(new GuessedEvent(0), trace[1].Effect);
        Assert.Equal(0, rt.Latest.Guess);
    }

    [Fact]
    public void Dispatch_while_paused_throws()
    {
        var rt = Build(new AnyIntPrompt());
        rt.Dispatch(new SubmitGuessIntent(), new System("t"));

        Assert.Throws<InvalidOperationException>(() =>
            rt.Dispatch(new SubmitGuessIntent(), new System("t")));
    }

    [Fact]
    public void Resume_without_pause_throws()
    {
        var rt = Build(new AnyIntPrompt());
        Assert.Throws<InvalidOperationException>(() => rt.Resume(1));
    }
}

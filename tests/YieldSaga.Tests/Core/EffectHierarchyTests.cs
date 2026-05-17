namespace YieldSaga.Tests.Core;

public class EffectHierarchyTests
{
    private sealed record SampleEvent(int Value) : Event;
    private sealed record SampleIntent(string Name) : Intent;
    private sealed record SampleSender(string Id) : Sender;

    [Fact]
    public void Event_is_an_Effect()
    {
        Effect e = new SampleEvent(1);
        Assert.IsAssignableFrom<Event>(e);
    }

    [Fact]
    public void Signal_is_an_Effect_but_not_an_Event()
    {
        var prompt = new TestPrompt();
        Effect s = new Take(prompt);
        Assert.IsAssignableFrom<Signal>(s);
        Assert.False(s is Event);
    }

    [Fact]
    public void Records_have_value_equality()
    {
        Assert.Equal(new SampleEvent(1), new SampleEvent(1));
        Assert.NotEqual(new SampleEvent(1), new SampleEvent(2));
        Assert.Equal(new SampleIntent("x"), new SampleIntent("x"));
        Assert.Equal(new SampleSender("s"), new SampleSender("s"));
    }

    private sealed record TestPrompt : Prompt
    {
        public override bool Accepts(object input) => true;
    }
}

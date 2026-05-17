namespace YieldSaga.Tests.Signals;

public class CallTests
{
    private sealed record GreetIntent(string Name) : Intent;
    private sealed record UserSender(string Id) : Sender;

    [Fact]
    public void Call_carries_intent_and_sender()
    {
        var intent = new GreetIntent("ada");
        var sender = new UserSender("u1");
        var call = new Call(intent, sender);

        Assert.Same(intent, call.Intent);
        Assert.Same(sender, call.Sender);
        Assert.Null(call.Scope);
    }

    [Fact]
    public void Call_can_carry_per_Call_scope()
    {
        var scope = SignalScope.Empty.OnTake(_ => 1);
        var call = new Call(new GreetIntent("ada"), new UserSender("u1"), Scope: scope);

        Assert.Same(scope, call.Scope);
    }

    [Fact]
    public void Call_is_a_Signal()
    {
        Effect c = new Call(new GreetIntent("x"), new UserSender("u"));
        Assert.IsAssignableFrom<Signal>(c);
    }
}

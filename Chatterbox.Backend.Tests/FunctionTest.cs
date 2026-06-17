using Chatterbox.Backend;
using Xunit;

namespace Chatterbox.Backend.Tests;

public class FunctionTest
{
    [Fact]
    public void RegisteredEvent_HasExpectedType()
    {
        var evt = new RegisteredEvent("alice");

        Assert.Equal("registered", evt.Type);
        Assert.Equal("alice", evt.DisplayName);
    }

    [Fact]
    public void MessageEvent_HasExpectedPayload()
    {
        var evt = new MessageEvent("alice", "hello");

        Assert.Equal("message", evt.Type);
        Assert.Equal("alice", evt.From);
        Assert.Equal("hello", evt.Text);
    }

    [Fact]
    public void UsersEvent_HasExpectedType()
    {
        var evt = new UsersEvent(new object[] { new { DisplayName = "alice" } });

        Assert.Equal("users", evt.Type);
        Assert.Single(evt.Users);
    }
}

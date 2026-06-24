using Chatterbox.Backend;
using System.Text.Json;
using System.Reflection;
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
    public void RegisteredEvent_SerializesWithLowercaseJson()
    {
        var json = JsonSerializer.Serialize(new RegisteredEvent("alice"));

        using var document = JsonDocument.Parse(json);

        Assert.Equal("registered", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("alice", document.RootElement.GetProperty("displayName").GetString());
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
    public void MessageEvent_SerializesWithLowercaseJson()
    {
        var json = JsonSerializer.Serialize(new MessageEvent("alice", "hello"));

        using var document = JsonDocument.Parse(json);

        Assert.Equal("message", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("alice", document.RootElement.GetProperty("from").GetString());
        Assert.Equal("hello", document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void UsersEvent_HasExpectedType()
    {
        var evt = new UsersEvent(new object[] { new { DisplayName = "alice" } });

        Assert.Equal("users", evt.Type);
        Assert.Single(evt.Users);
    }

    [Fact]
    public void UsersEvent_SerializesWithLowercaseJson()
    {
        var json = JsonSerializer.Serialize(new UsersEvent(new object[] { new { DisplayName = "alice" } }));

        using var document = JsonDocument.Parse(json);

        Assert.Equal("users", document.RootElement.GetProperty("type").GetString());
        Assert.True(document.RootElement.GetProperty("users").ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public void PresenceRecord_MapsConnectionIdWithLowercaseAttributeName()
    {
        var property = typeof(PresenceRecord).GetProperty(nameof(PresenceRecord.ConnectionId));

        Assert.NotNull(property);
        Assert.Contains(property!.GetCustomAttributesData(), attribute =>
            attribute.AttributeType.Name == "DynamoDBPropertyAttribute" &&
            attribute.ConstructorArguments.Count == 1 &&
            string.Equals(attribute.ConstructorArguments[0].Value?.ToString(), "connectionId", StringComparison.Ordinal));
    }

    [Fact]
    public void MessageRequest_DeserializesLowercaseJson()
    {
        var request = JsonSerializer.Deserialize<MessageRequest>(
            "{\"to\":\"alice\",\"text\":\"hello\"}",
            new JsonSerializerOptions()
        );

        Assert.NotNull(request);
        Assert.Equal("alice", request!.To);
        Assert.Equal("hello", request.Text);
    }

}

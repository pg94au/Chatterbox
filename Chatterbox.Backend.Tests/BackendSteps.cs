using AwesomeAssertions;
using Reqnroll;
using System.Net.WebSockets;

namespace Chatterbox.Backend.Tests;

[Binding]
public class BackendSteps(FeatureContext featureContext)
{
    [Given(@"a websocket connection (.+) is established")]
    public async Task AWebsocketConnectionIsEstablished(string websocketName)
    {
        var webSocket = GetWebSocketConnection(websocketName);
        var flociServiceUrl = featureContext.Get<Uri>("FlociServiceUrl");
        var webSocketApiId = featureContext.Get<string>("WebSocketApiId");
        var stageName = featureContext.Get<string>("StageName");

        var webSocketEndpoint = $"ws://{flociServiceUrl.Host}:{flociServiceUrl.Port}/ws/{webSocketApiId}/{stageName}";
        Console.WriteLine($"Connecting websocket '{websocketName}' to endpoint: {webSocketEndpoint}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await webSocket.ConnectAsync(new Uri(webSocketEndpoint), cts.Token);
        webSocket.State.Should().Be(WebSocketState.Open);
    }

    [When("websocket connection (.+) is closed")]
    public void WhenWebsocketConnectionIsClosed(string websocketName)
    {
        var webSocket = GetWebSocketConnection(websocketName);
        if (webSocket.State == WebSocketState.Open)
        {
            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, $"Closing connection {websocketName}", CancellationToken.None).Wait();
        }
    }


    [When("a register request is sent to (.+) for \"(.*)\"")]
    public async Task WhenARegisterRequestIsSentTo(string websocketName, string displayName)
    {
        var webSocket = GetWebSocketConnection(websocketName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await webSocket.SendMessageAsync(new RegisterRequest(displayName), cts.Token);
    }

    [Then("the user joined event is received from (.+) for \"(.*)\"")]
    public async Task ThenTheUserJoinedEventIsReceivedFrom(string websocketName, string displayName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var userJoinedEvent = await ReceiveMessage<UserJoinedEvent>(websocketName, cts.Token);
        userJoinedEvent.Should().NotBeNull();
        userJoinedEvent!.Type.Should().Be("userJoined");
        userJoinedEvent.DisplayName.Should().Be(displayName);
    }

    [Then("the user left event is received from (.+) for \"(.*)\"")]
    public async Task ThenTheUserLeftEventIsReceivedFrom(string websocketName, string displayName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var userLeftEvent = await ReceiveMessage<UserLeftEvent>(websocketName, cts.Token);
        userLeftEvent.Should().NotBeNull();
        userLeftEvent!.Type.Should().Be("userLeft");
        userLeftEvent.DisplayName.Should().Be(displayName);
    }

    [Then("the registered event is received from (.+) for \"(.*)\"")]
    public async Task ThenTheRegisteredEventIsReceivedFrom(string websocketName, string displayName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var registeredEvent = await ReceiveMessage<RegisteredEvent>(websocketName, cts.Token);
        registeredEvent.Should().NotBeNull();
        registeredEvent.Type.Should().Be("registered");
        registeredEvent.DisplayName.Should().Be(displayName);
    }

    [When(@"a list users request is sent to (.+)")]
    public async Task WhenAListUsersRequestIsSentTo(string websocketName)
    {
        var webSocket = GetWebSocketConnection(websocketName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await webSocket.SendMessageAsync(new ListUsersRequest(), cts.Token);
    }

    [Then(@"the returned list of users from (.+) includes")]
    public async Task ThenTheReturnedListOfUsersFromIncludes(string websocketName, Table expectedUsers)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var usersEvent = await ReceiveMessage<UsersEvent>(websocketName, cts.Token);
        usersEvent.Should().NotBeNull();
        usersEvent.Type.Should().Be("users");

        var expectedDisplayNames = expectedUsers.Rows.Select(row => row["DisplayName"]).ToArray();
        usersEvent.Users.Should().HaveCount(expectedDisplayNames.Length);

        var actualDisplayNames = usersEvent.Users.Select(user => user.DisplayName).ToArray();
        actualDisplayNames.Should().BeEquivalentTo(expectedDisplayNames, options => options.WithoutStrictOrdering());

        foreach (var user in usersEvent.Users)
        {
            user.ConnectedAt.Should().BeGreaterThan(0);
        }
    }

    [When("a send message request is sent to (.+) for \"(.+)\" with the message \"(.*)\"")]
    public async Task WhenASendMessageRequestIsSentToAForWithTheMessage(string websocketName, string receipientName, string text)
    {
        var webSocket = GetWebSocketConnection(websocketName);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await webSocket.SendMessageAsync(new MessageRequest(receipientName, text), cts.Token);
    }

    [Then("the message event is received from (.+) with the message \"(.*)\" from \"(.+)\"")]
    public async Task ThenTheMessageEventIsReceivedFromBWithTheMessageFrom(string websocketName, string text, string from)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var messageEvent = await ReceiveMessage<MessageEvent>(websocketName, cts.Token);
        messageEvent.Should().NotBeNull();
        messageEvent!.Type.Should().Be("message");
        
        messageEvent.Text.Should().Be(text);
        messageEvent.From.Should().Be(from);
    }

    [Then("no response is received from (.+)")]
    public async Task ThenNoResponseIsReceivedFrom(string websocketName)
    {
        var webSocket = GetWebSocketConnection(websocketName);

        await webSocket.NothingReceived(TimeSpan.FromSeconds(3));
    }


    private ClientWebSocket GetWebSocketConnection(string websocketName)
    {
        var webSockets = featureContext.ContainsKey("WebSocketConnections")
            ? featureContext.Get<Dictionary<string, ClientWebSocket>>("WebSocketConnections")
            : new Dictionary<string, ClientWebSocket>();

        if (webSockets.TryGetValue(websocketName, out var existingWebSocket))
        {
            return existingWebSocket;
        }

        var newWebSocket = new ClientWebSocket();
        webSockets[websocketName] = newWebSocket;
        featureContext.Set(webSockets, "WebSocketConnections");
        return newWebSocket;
    }

    private async Task<T> ReceiveMessage<T>(string websocketName, CancellationToken cancellationToken) where T : class
    {
        var webSocket = GetWebSocketConnection(websocketName);

        var message = await webSocket.ReceiveMessage<T>(cancellationToken);
        message.Should().NotBeNull();
        return message!;
    }
}

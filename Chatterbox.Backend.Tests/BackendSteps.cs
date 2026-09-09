using AwesomeAssertions;
using Reqnroll;
using System.Net.WebSockets;

namespace Chatterbox.Backend.Tests;

[Binding]
public class BackendSteps(FeatureContext featureContext)
{
    [Given("a websocket connection is established")]
    public async Task AWebsocketConnectionIsEstablished()
    {
        var flociServiceUrl = featureContext.Get<Uri>("FlociServiceUrl");
        var _webSocketApiId = featureContext.Get<string>("WebSocketApiId");
        var _stageName = featureContext.Get<string>("StageName");

        var webSocketEndpoint = $"ws://{flociServiceUrl.Host}:{flociServiceUrl.Port}/ws/{_webSocketApiId}/{_stageName}";
        Console.WriteLine($"Connecting to WebSocket endpoint: {webSocketEndpoint}");

        var clientWebSocket = featureContext.Get<ClientWebSocket>("ClientWebSocket");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await clientWebSocket.ConnectAsync(new Uri(webSocketEndpoint), cts.Token);
        clientWebSocket.State.Should().Be(WebSocketState.Open);
    }

    [When("a register request is sent for {string}")]
    public async Task WhenARegisterRequestIsSentFor(string displayName)
    {
        var clientWebSocket = featureContext.Get<ClientWebSocket>("ClientWebSocket");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        await clientWebSocket.SendMessageAsync(new RegisterRequest(displayName), cts.Token);
    }

    [Then("the user joined event is received for {string}")]
    public async Task ThenTheUserJoinedEventIsReceivedFor(string displayName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var userJoinedEvent = await ReceiveMessage<UserJoinedEvent>(cts.Token);
        userJoinedEvent.Should().NotBeNull();
        userJoinedEvent!.Type.Should().Be("userJoined");
        userJoinedEvent.DisplayName.Should().Be(displayName);
    }

    [Then("the registered event is received for {string}")]
    public async Task ThenTheRegisteredEventIsReceivedFor(string displayName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var registeredEvent = await ReceiveMessage<RegisteredEvent>(cts.Token);
        registeredEvent.Should().NotBeNull();
        registeredEvent.Type.Should().Be("registered");
        registeredEvent.DisplayName.Should().Be(displayName);
    }

    [When("a list users request is sent")]
    public async Task WhenAListUsersRequestIsSent()
    {
        var clientWebSocket = featureContext.Get<ClientWebSocket>("ClientWebSocket");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await clientWebSocket.SendMessageAsync(new ListUsersRequest(), cts.Token);
    }

    [Then("the returned list of users includes")]
    public async Task ThenTheReturnedListOfUsersIncludes(Table expectedUsers)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var usersEvent = await ReceiveMessage<UsersEvent>(cts.Token);
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


    [Then("a list users request returns")]
    public async Task ThenAListUsersRequestReturns(Table expectedUsers)
    {
        var clientWebSocket = featureContext.Get<ClientWebSocket>("ClientWebSocket");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await clientWebSocket.SendMessageAsync(new ListUsersRequest(), cts.Token);

        var usersEvent = await ReceiveMessage<UsersEvent>(cts.Token);
        usersEvent.Should().NotBeNull();
        usersEvent.Type.Should().Be("users");

        var expectedDisplayNames = expectedUsers.Rows.Select(row => row["DisplayName"]).ToArray();
        usersEvent.Users.Should().HaveCount(expectedDisplayNames.Length);

        var actualDisplayNames = usersEvent.Users.Select(user => user.DisplayName).ToArray();
        actualDisplayNames.Should().BeEquivalentTo(expectedDisplayNames, options => options.WithStrictOrdering());

        foreach (var user in usersEvent.Users)
        {
            user.ConnectedAt.Should().BeGreaterThan(0);
        }
    }

    private async Task<T> ReceiveMessage<T>(CancellationToken cancellationToken) where T : class
    {
        var clientWebSocket = featureContext.Get<ClientWebSocket>("ClientWebSocket");

        var message = await clientWebSocket.ReceiveMessage<T>(cancellationToken);
        message.Should().NotBeNull();
        return message!;
    }
}

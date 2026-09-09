using Amazon.CloudFormation;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Reqnroll;
using System.Net.WebSockets;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[Binding]
public class BackendSteps
{
    private static FlociContainer _flociContainer = null!;
    private static AmazonCloudFormationClient _cfClient = null!;
    private string _stackName = null!;
    private static string _flociNetworkName = string.Empty;
    private ClientWebSocket _webSocketClient = null!;
    private string? _webSocketApiId;
    private string? _stageName;

    [BeforeFeature]
    public static async Task BeforeFeature()
    {
        await StartFlociContainer();

        Console.WriteLine($"Floci at: {_flociContainer.GetConnectionString()}");

        _cfClient = CreateCloudFormationClient();
    }

    [BeforeScenario]
    public async Task BeforeScenario()
    {
        var templateBody = LoadTemplateYaml();

        _stackName = await LambdaDeploymentHelper.DeployCloudFormation(_flociContainer, _cfClient, templateBody);

        Console.WriteLine($"Deployed stack: {_stackName}");

        var response = await _cfClient.DescribeStacksAsync();
        var stacks = response.Stacks;
        stacks.Should().NotBeEmpty();

        _webSocketApiId = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "WebSocketApiId")?.OutputValue;
        _webSocketApiId.Should().NotBeNullOrEmpty();

        _stageName = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "StageName")?.OutputValue;
        _stageName.Should().NotBeNullOrEmpty();
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (_webSocketClient.State == WebSocketState.Open)
        {
            await _webSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, "Scenario complete", CancellationToken.None);
        }
        _webSocketClient.Dispose();

        await LambdaDeploymentHelper.DeleteCloudFormation(_cfClient, _stackName);
    }

    [AfterFeature]
    public static async Task AfterFeature()
    {
        await _flociContainer.StopAsync();
        await _flociContainer.DisposeAsync();
    }

    [Given("the cloud formation stack is deployed")]
    public void GivenTheCloudFormationStackIsDeployed()
    {
        _cfClient.Should().NotBeNull();
        _webSocketApiId.Should().NotBeNullOrEmpty();
        _stageName.Should().NotBeNullOrEmpty();
    }

    [Given("a websocket connection is established")]
    public async Task AWebsocketConnectionIsEstablished()
    {
        var serviceUrl = new Uri(_flociContainer.GetConnectionString());
        var webSocketEndpoint = $"ws://{serviceUrl.Host}:{serviceUrl.Port}/ws/{_webSocketApiId}/{_stageName}";
        Console.WriteLine($"Connecting to WebSocket endpoint: {webSocketEndpoint}");

        _webSocketClient = new ClientWebSocket();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _webSocketClient.ConnectAsync(new Uri(webSocketEndpoint), cts.Token);
        _webSocketClient.State.Should().Be(WebSocketState.Open);
    }

    [When("a register request is sent for {string}")]
    public async Task WhenARegisterRequestIsSentFor(string displayName)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _webSocketClient.SendMessageAsync(new RegisterRequest(displayName), cts.Token);
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _webSocketClient.SendMessageAsync(new ListUsersRequest(), cts.Token);
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _webSocketClient.SendMessageAsync(new ListUsersRequest(), cts.Token);

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
        var message = await _webSocketClient.ReceiveMessage<T>(cancellationToken);
        message.Should().NotBeNull();
        return message!;
    }

    private static AmazonCloudFormationClient CreateCloudFormationClient()
    {
        var config = new AmazonCloudFormationConfig
        {
            AuthenticationRegion = "us-east-1",
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            ServiceURL = _flociContainer.GetConnectionString()
        };

        return new AmazonCloudFormationClient(config);
    }

    private static async Task StartFlociContainer()
    {
        _flociNetworkName = $"floci_network-{Guid.NewGuid():N}";

        var network = new NetworkBuilder()
            .WithName(_flociNetworkName)
            .Build();

        _flociContainer = new FlociBuilder("floci/floci:2.0.1")
            .WithCleanUp(true)
            .WithName($"floci-{Guid.NewGuid():N}")
            .WithNetwork(network)
            .WithNetworkAliases("floci")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", AccessMode.ReadWrite)
            .WithPortBinding(4566, true)
            .WithEnvironment("LOG_LEVEL", "DEBUG")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request =>
                        request.ForPort(4566)
                            .ForPath("/_floci/health")))
            .Build();

        await _flociContainer.StartAsync();
    }

    private static string LoadTemplateYaml()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "template.yaml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find template.yaml in the repository tree.");
    }
}

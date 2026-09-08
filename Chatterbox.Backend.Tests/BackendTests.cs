using Amazon.CloudFormation;
using AwesomeAssertions;
using Chatterbox.Backend;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using NUnit.Framework;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Containers;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class BackendTests
{
    private FlociContainer _flociContainer = null!;

    private AmazonCloudFormationClient _cfClient = null!;

    private string _stackName = string.Empty;

    private string _flociNetworkName = string.Empty;

    [SetUp]
    public async Task SetUp()
    {
        await StartFlociContainer();

        //Console.WriteLine($"Floci at: {_flociContainer.GetConnectionString()}");

        _cfClient = CreateCloudFormationClient();

        var templateBody = LoadTemplateYaml();

        _stackName = await LambdaDeploymentHelper.DeployCloudFormation(_flociContainer, _cfClient, templateBody);
    }

    private AmazonCloudFormationClient CreateCloudFormationClient()
    {
        var config = new AmazonCloudFormationConfig
        {
            AuthenticationRegion = "us-east-1",
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            ServiceURL = "http://localhost:4566" //_flociContainer.GetConnectionString()
        };

        return new AmazonCloudFormationClient(config);
    }

    private async Task StartFlociContainer()
    {
        _flociNetworkName = $"floci_network-{Guid.NewGuid():N}";

        var network = new NetworkBuilder()
            .WithName(_flociNetworkName)
            .Build();

        var sessionId = ResourceReaper.DefaultSessionId;

        //_flociContainer = new FlociBuilder("floci/floci:latest")
        //    .WithCleanUp(true)
        //    .WithName($"floci-{Guid.NewGuid():N}")
        //    .WithNetwork(network)
        //    .WithNetworkAliases("floci")
        //    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", AccessMode.ReadWrite)
        //    .WithPortBinding(4566, true)
        //    .WithEnvironment("LOG_LEVEL", "DEBUG")
        //    .WithWaitStrategy(
        //        Wait.ForUnixContainer()
        //            .UntilHttpRequestIsSucceeded(request =>
        //                request.ForPort(4566)
        //                    .ForPath("/_floci/health")))
        //    .Build();

        //await _flociContainer.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (!string.IsNullOrWhiteSpace(_stackName))
        {
            Console.WriteLine($"Deleting CloudFormation stack: {_stackName}");
            await LambdaDeploymentHelper.DeleteCloudFormation(_cfClient, _stackName);
        }

        await LambdaDeploymentHelper.CleanupDockerNetworkAsync(_flociNetworkName);

        if (_flociContainer is not null)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));

            await _flociContainer.StopAsync();
            await _flociContainer.DisposeAsync();
        }
    }


    [Test]
    public async Task FirstUserCanRegisterToEmptyChatroom()
    {
        var response = await _cfClient.DescribeStacksAsync();
        var stacks = response.Stacks;
        stacks.Should().NotBeEmpty();

        var webSocketApiId = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "WebSocketApiId")?.OutputValue;
        webSocketApiId.Should().NotBeNullOrEmpty();

        var stageName = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "StageName")?.OutputValue;
        stageName.Should().NotBeNullOrEmpty();

        var serviceUrl = new Uri("http://localhost:4566" /*_flociContainer.GetConnectionString()*/);
        //var webSocketEndpoint = $"ws://{serviceUrl.Host}:{serviceUrl.Port}/ws/{webSocketApiId}/{stageName}";
        var webSocketEndpoint = $"ws://{serviceUrl.Host}:{serviceUrl.Port}/_aws/execute-api/{webSocketApiId}/{stageName}";
        Console.WriteLine($"Connecting to WebSocket endpoint: {webSocketEndpoint}");

        // Establish websocket connection to service endpoint.
        using var client = new ClientWebSocket();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(new Uri(webSocketEndpoint!), cancellation.Token);
        client.State.Should().Be(WebSocketState.Open);

        using var testTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Register as a new user.
        var registerRequest = new RegisterRequest("Paul");
        await client.SendMessageAsync(registerRequest, testTimeoutCts.Token);

        // Should receive user joined event.
        var userJoinedEvent = await client.ReceiveMessage<UserJoinedEvent>(testTimeoutCts.Token);
        userJoinedEvent.Should().NotBeNull();
        userJoinedEvent!.Type.Should().Be("userJoined");
        userJoinedEvent.DisplayName.Should().Be("Paul");

        // Should receive registered event.
        var registeredEvent = await client.ReceiveMessage<RegisteredEvent>(testTimeoutCts.Token);
        registeredEvent.Should().NotBeNull();
        registeredEvent.Type.Should().Be("registered");
        registeredEvent.DisplayName.Should().Be("Paul");

        // List users.
        var listUsersRequest = new ListUsersRequest();
        await client.SendMessageAsync(listUsersRequest, testTimeoutCts.Token);

        // Should receive users event with the registered user.
        var usersEvent = await client.ReceiveMessage<UsersEvent>(testTimeoutCts.Token);
        usersEvent.Should().NotBeNull();
        usersEvent.Type.Should().Be("users");
        usersEvent.Users.Should().HaveCount(1);
        usersEvent.Users.First().DisplayName.Should().Be("Paul");
        usersEvent.Users.First().ConnectedAt.Should().BeGreaterThan(0);
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

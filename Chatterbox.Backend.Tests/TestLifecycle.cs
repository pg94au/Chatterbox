using Amazon.CloudFormation;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Reqnroll;
using System.Net.WebSockets;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[Binding]
public class TestLifecycle
{
    private static FlociContainer _flociContainer = null!;
    private static AmazonCloudFormationClient _cfClient = null!;
    private static string _flociNetworkName = string.Empty;
    private string _stackName = string.Empty;
    private string? _webSocketApiId;
    private string? _stageName;

    [BeforeFeature]
    public static async Task BeforeFeature(FeatureContext featureContext)
    {
        await StartFlociContainer();

        Console.WriteLine($"Floci at: {_flociContainer.GetConnectionString()}");
        featureContext.Add("FlociServiceUrl", new Uri(_flociContainer.GetConnectionString()));

        _cfClient = CreateCloudFormationClient();
    }

    [BeforeScenario]
    public async Task BeforeScenario(FeatureContext featureContext)
    {
        var templateBody = LoadTemplateYaml();

        _stackName = await LambdaDeploymentHelper.DeployCloudFormation(_flociContainer, _cfClient, templateBody);

        Console.WriteLine($"Deployed stack: {_stackName}");

        var response = await _cfClient.DescribeStacksAsync();
        var stacks = response.Stacks;
        stacks.Should().NotBeEmpty();

        _webSocketApiId = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "WebSocketApiId")?.OutputValue;
        _webSocketApiId.Should().NotBeNullOrEmpty();
        featureContext.Set(_webSocketApiId, "WebSocketApiId");

        _stageName = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "StageName")?.OutputValue;
        _stageName.Should().NotBeNullOrEmpty();
        featureContext.Set(_stageName, "StageName");

        var defaultWebSocket = new ClientWebSocket();
        var webSockets = new Dictionary<string, ClientWebSocket>
        {
            ["default"] = defaultWebSocket
        };

        featureContext.Set(webSockets, "WebSocketConnections");
        featureContext.Set(defaultWebSocket, "ClientWebSocket");
    }

    [AfterScenario]
    public async Task AfterScenario(FeatureContext featureContext)
    {
        if (featureContext.ContainsKey("WebSocketConnections"))
        {
            var webSockets = featureContext.Get<Dictionary<string, ClientWebSocket>>("WebSocketConnections");
            foreach (var clientWebSocket in webSockets.Values)
            {
                if (clientWebSocket.State == WebSocketState.Open)
                {
                    await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Scenario complete", CancellationToken.None);
                }

                clientWebSocket.Dispose();
            }
        }

        if (!string.IsNullOrWhiteSpace(_stackName))
        {
            await LambdaDeploymentHelper.DeleteCloudFormation(_cfClient, _stackName);
        }
    }

    [AfterScenario]
    public void AfterScenarioCleanup(FeatureContext featureContext)
    {
        featureContext.Remove("WebSocketConnections");
        featureContext.Remove("ClientWebSocket");
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
}

using Amazon;
using Amazon.CloudFormation;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;
using Reqnroll;
using System.Net.WebSockets;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[Binding]
public class TestLifecycle(FeatureContext featureContext)
{
    private static FlociContainer _flociContainer = null!;
    private static AmazonCloudFormationClient _cfClient = null!;
    private static string _flociNetworkName = string.Empty;
    private static string _stackName = string.Empty;
    private static Uri? _flociServiceUrl;
    private static string? _webSocketApiId;
    private static string? _stageName;

    [BeforeTestRun]
    public static async Task BeforeFeature()
    {
        TestContext.Progress.Info("Starting Floci container");
        await StartFlociContainer();

        TestContext.Progress.Info($"Floci at: {_flociContainer.GetConnectionString()}");
        _flociServiceUrl = new Uri(_flociContainer.GetConnectionString());

        _cfClient = CreateCloudFormationClient();

        var templateBody = LoadTemplateYaml();

        TestContext.Progress.Info("Deploying Cloudformation stack");
        _stackName = await LambdaDeploymentHelper.DeployCloudFormation(_flociContainer, _cfClient, templateBody);

        TestContext.Progress.Info($"Deployed stack: {_stackName}");

        var response = await _cfClient.DescribeStacksAsync();
        var stacks = response.Stacks;
        stacks.Should().NotBeEmpty();

        var webSocketApiId = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "WebSocketApiId")?.OutputValue;
        webSocketApiId.Should().NotBeNullOrEmpty();
        _webSocketApiId = webSocketApiId;

        var stageName = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "StageName")?.OutputValue;
        stageName.Should().NotBeNullOrEmpty();
        _stageName = stageName;
    }

    [BeforeFeature]
    public static void BeforeFeature(FeatureContext featureContext)
    {
        featureContext.Add("FlociServiceUrl", _flociServiceUrl);
        featureContext.Add("WebSocketApiId", _webSocketApiId);
        featureContext.Add("StageName", _stageName);
    }

    [AfterScenario]
    public async Task AfterScenario()
    {
        if (featureContext.ContainsKey("WebSocketConnections"))
        {
            var webSockets = featureContext.Get<Dictionary<string, ClientWebSocket>>("WebSocketConnections");
            foreach (var clientWebSocket in webSockets.Values)
            {
                try
                {
                    if (clientWebSocket.State == WebSocketState.Open)
                    {
                        await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Scenario complete",
                            CancellationToken.None);
                    }

                    clientWebSocket.Dispose();
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        // Delete all items in the Dynamo table
        var tableName = "chatterbox-connections-prod";
        var dynamoClient = new Amazon.DynamoDBv2.AmazonDynamoDBClient(new Amazon.DynamoDBv2.AmazonDynamoDBConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            AuthenticationRegion = "us-east-1",
            ServiceURL = _flociContainer.GetConnectionString()
        });
        var scanResponse = await dynamoClient.ScanAsync(new Amazon.DynamoDBv2.Model.ScanRequest
        {
            TableName = tableName,
            AttributesToGet = ["displayName"],
        });
        foreach (var item in scanResponse.Items)
        {
            var deleteRequest = new Amazon.DynamoDBv2.Model.DeleteItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
                {
                    { "displayName", item["displayName"] }
                }
            };
            await dynamoClient.DeleteItemAsync(deleteRequest);
        }

        featureContext.Remove("WebSocketConnections");
        featureContext.Remove("ClientWebSocket");
    }

    [AfterTestRun]
    public static async Task AfterFeature()
    {
        await _flociContainer.StopAsync();
        await _flociContainer.DisposeAsync();
    }

    [Given("the cloud formation stack is deployed")]
    public void GivenTheCloudFormationStackIsDeployed()
    {
        _cfClient.Should().NotBeNull();
        var webSocketApiId = featureContext.Get<string>("WebSocketApiId");
        webSocketApiId.Should().NotBeNullOrEmpty();
        var stageName = featureContext.Get<string>("StageName");
        stageName.Should().NotBeNullOrEmpty();
    }

    private static async Task StartFlociContainer()
    {
        var sessionId = ResourceReaper.DefaultSessionId;

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
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__KEY", "org.testcontainers.resource-reaper-session")
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__VALUE", sessionId.ToString())
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

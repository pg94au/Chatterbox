using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;
using System.Net.WebSockets;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class ExperimentalTests
{
    private FlociContainer _flociContainer = null!;

    private AmazonCloudFormationClient _cfClient = null!;

    [SetUp]
    public async Task SetUp()
    {
        await StartFlociContainer();

        Console.WriteLine($"Floci at: {_flociContainer.GetConnectionString()}");

        _cfClient = CreateCloudFormationClient();

        var templateBody = LoadTemplateYaml();

        await LambdaDeploymentHelper.DeployCloudFormation(_flociContainer, _cfClient, templateBody);
    }

    private AmazonCloudFormationClient CreateCloudFormationClient()
    {
        var config = new AmazonCloudFormationConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            ServiceURL = _flociContainer.GetConnectionString()
        };

        return new AmazonCloudFormationClient(config);
    }

    private async Task StartFlociContainer()
    {
        _flociContainer = new FlociBuilder("floci/floci:1.6.0")
            .WithName($"floci-{Guid.NewGuid():N}")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", AccessMode.ReadWrite)
            .WithPortBinding(4566, true)
            .WithEnvironment("FLOCI_DEFAULT_REGION", "us-east-1")
            .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
            .WithEnvironment("AWS_REGION", "us-east-1")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request =>
                        request.ForPort(4566)
                            .ForPath("/_floci/health")))
            .Build();

        await _flociContainer.StartAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_flociContainer is not null)
        {
            await _flociContainer.StopAsync();
            await _flociContainer.DisposeAsync();
        }
    }


    [Test]
    public async Task Foo()
    {
        var response = await _cfClient.DescribeStacksAsync();
        var stacks = response.Stacks;
        stacks.Should().NotBeEmpty();

        var webSocketApiId = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "WebSocketApiId")?.OutputValue;
        webSocketApiId.Should().NotBeNullOrEmpty();

        var stageName = stacks[0].Outputs.FirstOrDefault(o => o.OutputKey == "StageName")?.OutputValue;
        stageName.Should().NotBeNullOrEmpty();

        var serviceUrl = new Uri(_flociContainer.GetConnectionString());
        var webSocketEndpoint = $"ws://{serviceUrl.Host}:{serviceUrl.Port}/ws/{webSocketApiId}/{stageName}";
        Console.WriteLine($"Connecting to WebSocket endpoint: {webSocketEndpoint}");

        using var client = new ClientWebSocket();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.ConnectAsync(new Uri(webSocketEndpoint!), cancellation.Token);

        client.State.Should().Be(WebSocketState.Open);
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

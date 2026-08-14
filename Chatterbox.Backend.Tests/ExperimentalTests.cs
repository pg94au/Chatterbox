using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using NUnit.Framework;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class ExperimentalTests
{
    private IContainer _flociContainer = null!;

    private AmazonCloudFormationClient _cfClient = null!;

    [SetUp]
    public async Task SetUp()
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

        Console.WriteLine($"Floci at: {_flociContainer.GetConnectionString()}");

        var config = new AmazonCloudFormationConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            ServiceURL = _flociContainer.GetConnectionString()
        };

        _cfClient = new AmazonCloudFormationClient(config);
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
        var stageName = "prod";
        var bucketName = $"chatterbox-bucket-{Guid.NewGuid():N}"; // Unique name per test run
        var bucketKey = $"chatterbox-{stageName}/{Guid.NewGuid():N}/lambda.zip";

        var packagePath = await LambdaDeploymentHelper.CreateLambdaPackage();

        // TODO: We will need to get the AWS endpoint URL from TestContainers
        Console.WriteLine("Uploading artifact...");
        await LambdaDeploymentHelper.UploadArtifactAsync(bucketName, bucketKey, packagePath, _flociContainer.GetConnectionString());

        var templateBody = LoadTemplateYaml();

        var stackName = $"test-stack-{Guid.NewGuid():N}"; // Unique name per test run

        var createRequest = new CreateStackRequest
        {
            StackName = stackName,
            TemplateBody = templateBody,
            Parameters =
            [
                new Parameter { ParameterKey = "LambdaCodeBucket", ParameterValue = bucketName },
                new Parameter { ParameterKey = "LambdaCodeKey", ParameterValue = bucketKey },
                new Parameter { ParameterKey = "StageName", ParameterValue = stageName },
                new Parameter { ParameterKey = "AwsServiceUrl", ParameterValue = _flociContainer.GetConnectionString() },
            ],
            OnFailure = OnFailure.ROLLBACK, // Auto-cleanup if creation fails
        };

        // Trigger the creation in AWS
        await _cfClient.CreateStackAsync(createRequest);

        // Wait in-process until CloudFormation finishes deploying the infrastructure
        await WaitForStackStatusAsync(stackName, StackStatus.CREATE_COMPLETE);

        await DisplayStackOutputsAsync(stackName);
    }








    // Helper method to poll the CloudFormation API in-process
    private async Task WaitForStackStatusAsync(string stackName, StackStatus targetStatus)
    {
        while (true)
        {
            try
            {
                var response = await _cfClient.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
                var currentStatus = response.Stacks[0].StackStatus;

                if (currentStatus == targetStatus) break;

                // If it transitions into a failure state, throw immediately to fail the test fast
                if (currentStatus.Value.EndsWith("_FAILED") || currentStatus == StackStatus.ROLLBACK_COMPLETE)
                {
                    throw new Exception($"Stack entered an unexpected state: {currentStatus}");
                }
            }
            catch (AmazonCloudFormationException ex) when (ex.ErrorCode == "ValidationError" &&
                                                           targetStatus == StackStatus.DELETE_COMPLETE)
            {
                // The stack is successfully deleted and no longer exists in AWS
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5)); // Poll every 5 seconds
        }
    }

    private async Task DisplayStackOutputsAsync(string stackName)
    {
        var response = await _cfClient.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
        var outputs = response.Stacks[0].Outputs;

        foreach (var output in outputs)
        {
            Console.WriteLine($"Output Key: {output.OutputKey}, Value: {output.OutputValue}");
        }
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

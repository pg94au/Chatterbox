using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using NUnit.Framework;
using System.Diagnostics;
using System.IO.Compression;
using Testcontainers.Floci;
using InvalidOperationException = System.InvalidOperationException;

namespace Chatterbox.Backend.Tests;

public class LambdaDeploymentHelper
{
    public static async Task<string> CreateLambdaPackage()
    {
        var repositoryRoot = LambdaDeploymentHelper.FindRepositoryRoot();
        var publishDirectory = await LambdaDeploymentHelper.PublishBackendAsync(repositoryRoot);

        var packagePath = Path.Combine(Path.GetTempPath(), "chatterbox-backend-package", Guid.NewGuid().ToString("N"), "lambda.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath) ?? throw new InvalidOperationException("Invalid package path."));

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        await ZipFile.CreateFromDirectoryAsync(publishDirectory, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return packagePath;
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "template.yaml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    public static async Task<string> PublishBackendAsync(string repositoryRoot)
    {
        var backendProject = Path.Combine(repositoryRoot, "Chatterbox.Backend", "Chatterbox.Backend.csproj");
        var publishDirectory = Path.Combine(Path.GetTempPath(), "chatterbox-backend-publish", Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(publishDirectory);

        await RunProcessAsync(
            "dotnet",
            $"publish \"{backendProject}\" -c Release -o \"{publishDirectory}\"",
            repositoryRoot);

        return publishDirectory;
    }

    public static async Task<string> RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        startInfo.Environment["AWS_ACCESS_KEY_ID"] = "test";
        startInfo.Environment["AWS_SECRET_ACCESS_KEY"] = "test";
        startInfo.Environment["AWS_SESSION_TOKEN"] = "test";
        startInfo.Environment["AWS_DEFAULT_REGION"] = "us-east-1";
        startInfo.Environment["AWS_REGION"] = "us-east-1";
        startInfo.Environment["DEFAULT_REGION"] = "us-east-1";
        startInfo.Environment["FLOCI_DEFAULT_REGION"] = "us-east-1";
        startInfo.Environment["FLOCI_REGION"] = "us-east-1";
        startInfo.Environment["AWS_EC2_METADATA_DISABLED"] = "true";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{error}");
        }

        return string.IsNullOrWhiteSpace(output) ? error.Trim() : output.Trim();
    }

    public static async Task UploadArtifactAsync(string bucketName, string s3Key, string packagePath, string awsEndpoint)
    {
        //await RunAwsAsync($"s3api create-bucket --bucket {bucketName} --create-bucket-configuration LocationConstraint=ca-central-1", awsEndpoint);
        await RunAwsAsync($"s3api create-bucket --bucket {bucketName}", awsEndpoint);

        await RunAwsAsync($"s3 cp \"{packagePath}\" s3://{bucketName}/{s3Key} --checksum-algorithm SHA256", awsEndpoint);
    }

    private static async Task<string> RunAwsAsync(string arguments, string awsEndpoint)
    {
        return await RunProcessAsync(
            "aws",
            $"{arguments} --region us-east-1 --endpoint-url \"{awsEndpoint}\"",
            FindRepositoryRoot());
    }

    public static async Task<string> DeployCloudFormation(FlociContainer flociContainer, AmazonCloudFormationClient cfClient, string templateBody)
    {
        var stageName = "prod";
        var bucketName = $"chatterbox-bucket-{Guid.NewGuid():N}"; // Unique name per test run
        var bucketKey = $"chatterbox-{stageName}/{Guid.NewGuid():N}/lambda.zip";

        var packagePath = await LambdaDeploymentHelper.CreateLambdaPackage();

        TestContext.Progress.Info("Uploading artifact...");
        await LambdaDeploymentHelper.UploadArtifactAsync(bucketName, bucketKey, packagePath, flociContainer.GetConnectionString());

        var stackName = $"test-stack-{Guid.NewGuid():N}"; // Unique name per test run

        var flociInsideUrl = new UriBuilder(flociContainer.GetConnectionString())
        {
            Host = flociContainer.Name.Trim('/'), // Has '/' prefix?
            Port = 4566
        };
        TestContext.Progress.Info($"Floci inside URL: {flociInsideUrl}");

        var createRequest = new CreateStackRequest
        {
            StackName = stackName,
            TemplateBody = templateBody,
            Parameters =
            [
                new Parameter { ParameterKey = "LambdaCodeBucket", ParameterValue = bucketName },
                new Parameter { ParameterKey = "LambdaCodeKey", ParameterValue = bucketKey },
                new Parameter { ParameterKey = "StageName", ParameterValue = stageName },
                new Parameter { ParameterKey = "AwsServiceUrl", ParameterValue = flociInsideUrl.ToString() },
            ],
            OnFailure = OnFailure.ROLLBACK, // Auto-cleanup if creation fails
        };

        // Trigger the creation in AWS
        await cfClient.CreateStackAsync(createRequest);

        // Wait in-process until CloudFormation finishes deploying the infrastructure
        await WaitForStackStatusAsync(stackName, StackStatus.CREATE_COMPLETE, cfClient);

        await DisplayStackOutputsAsync(stackName, cfClient);

        return stackName;
    }

    public static async Task DeleteCloudFormation(AmazonCloudFormationClient cfClient, string stackName)
    {
        try
        {
            await cfClient.DeleteStackAsync(new DeleteStackRequest { StackName = stackName });
            await WaitForStackStatusAsync(stackName, StackStatus.DELETE_COMPLETE, cfClient);
        }
        catch (AmazonCloudFormationException ex) when (ex.ErrorCode == "ValidationError" &&
                                                     ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            // The stack was already cleaned up or never created.
        }
    }

    public static async Task CleanupDockerNetworkAsync(string? networkName)
    {
        if (string.IsNullOrWhiteSpace(networkName))
        {
            return;
        }

        try
        {
            var containerIds = await RunProcessAsync("docker", $"ps -aq --filter network={networkName}", FindRepositoryRoot());

            foreach (var containerId in containerIds.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(containerId))
                {
                    await RunProcessAsync("docker", $"rm -f {containerId}", FindRepositoryRoot());
                }
            }
        }
        catch
        {
            // The network or containers may already be gone.
        }

        try
        {
            await RunProcessAsync("docker", $"network rm {networkName}", FindRepositoryRoot());
        }
        catch
        {
            // Ignore if the network already disappeared.
        }
    }

    // Helper method to poll the CloudFormation API in-process
    private static async Task WaitForStackStatusAsync(string stackName, StackStatus targetStatus, AmazonCloudFormationClient cfClient)
    {
        while (true)
        {
            try
            {
                var response = await cfClient.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
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

    private static async Task DisplayStackOutputsAsync(string stackName, AmazonCloudFormationClient cfClient)
    {
        var response = await cfClient.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
        var outputs = response.Stacks[0].Outputs;

        foreach (var output in outputs)
        {
            TestContext.Progress.Info($"Output Key: {output.OutputKey}, Value: {output.OutputValue}");
        }
    }

}

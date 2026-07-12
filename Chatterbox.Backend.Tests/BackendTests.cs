using AwesomeAssertions;
using NUnit.Framework;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.WebSockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class BackendTests
{
    private IContainer _container = null!;
    private string _awsEndpoint = string.Empty;

    [SetUp]
    public async Task Setup()
    {
        _container = new ContainerBuilder("ministackorg/ministack:latest")
            .WithName($"ministack-test-{Guid.NewGuid():N}")
            .WithEnvironment("LOG_LEVEL", "DEBUG")
            .WithPortBinding(0, 4566)
            .Build();

        await _container.StartAsync();

        var hostPort = _container.GetMappedPublicPort(4566);
        _awsEndpoint = $"http://localhost:{hostPort}";
    }

    [TearDown]
    public async Task Teardown()
    {
        if (_container is not null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    [Test]
    public async Task Foo()
    {
        var repositoryRoot = FindRepositoryRoot();
        var stackName = "chatterbox-test";
        var stageName = "prod";
        var tableName = $"chatterbox-connections-{stackName}";

        var publishDirectory = await PublishBackendAsync(repositoryRoot);
        var packagePath = CreateLambdaPackage(publishDirectory);
        var bucketName = $"chatterbox-backend-tests-{Guid.NewGuid():N}";
        var s3Key = $"chatterbox-{stageName}/{Guid.NewGuid():N}/lambda.zip";

        await EnsureBucketExistsAsync(bucketName);
        await UploadArtifactAsync(bucketName, s3Key, packagePath);
        var endpoint = await DeployStackAsync(repositoryRoot, bucketName, s3Key, stackName, stageName, tableName);

        endpoint.Should().NotBeNullOrWhiteSpace();
        endpoint.Should().NotBe("None");
        Console.WriteLine(endpoint);

        // Extract port from _awsEndpoint
        var port = new Uri(_awsEndpoint).Port;

        // Endpoint is wss://{apiId}.execute-api.{region}.amazonaws.com/{stage}
        // MiniStack routes WebSocket APIs via LocalStack-compat path:
        //   ws://localhost:{port}/_aws/execute-api/{apiId}/{stage}
        var endpointUri = new Uri(endpoint);
        var apiId = endpointUri.Host.Split('.')[0];
        var stage = endpointUri.AbsolutePath.TrimStart('/');
        var wsEndpoint = new UriBuilder("ws", "localhost", port, $"/_aws/execute-api/{apiId}/{stage}").Uri;

        // Test WebSocket connection
        using var webSocket = new ClientWebSocket();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await webSocket.ConnectAsync(wsEndpoint, cancellationTokenSource.Token);

        webSocket.State.Should().Be(WebSocketState.Open);
        Console.WriteLine($"Successfully connected to WebSocket at {wsEndpoint}");
    }

    private static string FindRepositoryRoot()
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

    private static async Task<string> PublishBackendAsync(string repositoryRoot)
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

    private static string CreateLambdaPackage(string publishDirectory)
    {
        var packagePath = Path.Combine(Path.GetTempPath(), "chatterbox-backend-package", Guid.NewGuid().ToString("N"), "lambda.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath) ?? throw new InvalidOperationException("Invalid package path."));

        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        ZipFile.CreateFromDirectory(publishDirectory, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return packagePath;
    }

    private async Task EnsureBucketExistsAsync(string bucketName)
    {
        await RunAwsAsync($"s3api create-bucket --bucket {bucketName} --create-bucket-configuration LocationConstraint=ca-central-1");
    }

    private async Task UploadArtifactAsync(string bucketName, string s3Key, string packagePath)
    {
        await RunAwsAsync($"s3 cp \"{packagePath}\" s3://{bucketName}/{s3Key} --checksum-algorithm SHA256");
    }

    private async Task<string> DeployStackAsync(string repositoryRoot, string bucketName, string s3Key, string stackName, string stageName, string tableName)
    {
        await RunAwsAsync(
            $"cloudformation deploy --template-file \"{Path.Combine(repositoryRoot, "template.yaml")}\" --stack-name {stackName} --parameter-overrides Environment={stageName} TableName={tableName} LambdaCodeBucket={bucketName} LambdaCodeKey={s3Key} StageName={stageName} --capabilities CAPABILITY_NAMED_IAM");

        return await RunAwsAsync(
            $"cloudformation describe-stacks --stack-name {stackName} --query \"Stacks[0].Outputs[?OutputKey=='WebSocketEndpoint'].OutputValue | [0]\" --output text");
    }

    private async Task<string> RunAwsAsync(string arguments)
    {
        return await RunProcessAsync(
            "aws",
            $"{arguments} --region ca-central-1 --endpoint-url \"{_awsEndpoint}\"",
            FindRepositoryRoot());
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, string workingDirectory)
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
}

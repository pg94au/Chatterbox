using AwesomeAssertions;
using NUnit.Framework;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class BackendTests
{
    private IContainer? _container;
    private string _awsEndpoint = string.Empty;

    [SetUp]
    public async Task Setup()
    {
        //_container = new ContainerBuilder("ministackorg/ministack:latest")
        //    .WithName($"ministack-test-{Guid.NewGuid():N}")
        //    .WithEnvironment("LAMBDA_EXECUTOR", "docker")
        //    .WithEnvironment("LOG_LEVEL", "DEBUG")
        //    .WithPortBinding(0, 4566)
        //    .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock")
        //    .Build();

        //await _container.StartAsync();

        //var hostPort = _container.GetMappedPublicPort(4566);
        //_awsEndpoint = $"http://localhost:{hostPort}";

        var flociContainer = new FlociBuilder("floci/floci:1.5.33")
            .WithName($"floci-{Guid.NewGuid()}")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", AccessMode.ReadWrite)
            .WithPortBinding(4566, true)
            .WithEnvironment("FLOCI_DEFAULT_REGION", "us-east-1")
            .WithEnvironment("FLOCI_REGION", "us-east-1")
            .WithEnvironment("DEFAULT_REGION", "us-east-1")
            .WithEnvironment("AWS_DEFAULT_REGION", "us-east-1")
            .WithEnvironment("AWS_REGION", "us-east-1")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request =>
                        request.ForPort(4566)
                            .ForPath("/_localstack/health")))
            .Build();

        await flociContainer.StartAsync();

        _awsEndpoint = flociContainer.GetConnectionString();

        _container = flociContainer;
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
        var websocketApiEndpoint = GetWebSocketApiEndpointForLambda();
        var endpoint = await DeployStackAsync(repositoryRoot, bucketName, s3Key, stackName, stageName, tableName, websocketApiEndpoint);

        endpoint.Should().NotBeNullOrWhiteSpace();
        endpoint.Should().NotBe("None");
        Console.WriteLine(endpoint);

        // Endpoint is wss://{apiId}.execute-api.{region}.amazonaws.com/{stage}
        var endpointUri = new Uri(endpoint);
        var apiId = endpointUri.Host.Split('.')[0];
        var stage = endpointUri.AbsolutePath.TrimStart('/');

        // Extract port from _awsEndpoint for the WebSocket client connection.
        var port = new Uri(_awsEndpoint).Port;
        var wsEndpoint = new UriBuilder("ws", "localhost", port, $"/ws/{apiId}/{stage}").Uri;

        // Test WebSocket connection
        using var webSocket = new ClientWebSocket();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await webSocket.ConnectAsync(wsEndpoint, cancellationTokenSource.Token);

        webSocket.State.Should().Be(WebSocketState.Open);
        Console.WriteLine($"Successfully connected to WebSocket at {wsEndpoint}");

        const string displayName = "Alice";
        var registerMessage = Encoding.UTF8.GetBytes($"{{\"action\":\"register\",\"displayName\":\"{displayName}\"}}");

        await webSocket.SendAsync(
            registerMessage,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationTokenSource.Token);

        var receiveBuffer = new byte[4096];
        var received = await webSocket.ReceiveAsync(receiveBuffer, cancellationTokenSource.Token);

        received.MessageType.Should().Be(WebSocketMessageType.Text);

        using var message = JsonDocument.Parse(receiveBuffer.AsMemory(0, received.Count));
        var root = message.RootElement;

        root.GetProperty("type").GetString().Should().Be("registered");
        root.GetProperty("displayName").GetString().Should().Be(displayName);
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
        await RunAwsAsync($"s3api create-bucket --bucket {bucketName}");
    }

    private async Task UploadArtifactAsync(string bucketName, string s3Key, string packagePath)
    {
        await RunAwsAsync($"s3 cp \"{packagePath}\" s3://{bucketName}/{s3Key} --checksum-algorithm SHA256");
    }

    private async Task<string> DeployStackAsync(string repositoryRoot, string bucketName, string s3Key, string stackName, string stageName, string tableName, string websocketApiEndpoint)
    {
        await RunAwsAsync(
            $"cloudformation deploy --template-file \"{Path.Combine(repositoryRoot, "template.yaml")}\" --stack-name {stackName} --parameter-overrides Environment={stageName} TableName={tableName} LambdaCodeBucket={bucketName} LambdaCodeKey={s3Key} StageName={stageName} WebSocketApiEndpoint={websocketApiEndpoint} --capabilities CAPABILITY_NAMED_IAM");

        return await RunAwsAsync(
            $"cloudformation describe-stacks --stack-name {stackName} --query \"Stacks[0].Outputs[?OutputKey=='WebSocketEndpoint'].OutputValue | [0]\" --output text");
    }

    private string GetWebSocketApiEndpointForLambda()
    {
        var uri = new Uri(_awsEndpoint);

        // The Lambda runs inside a Docker container, so localhost points to itself.
        // Use the host gateway name so the Lambda can reach the Floci container.
        var host = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? "host.docker.internal"
            : uri.Host;

        var builder = new UriBuilder(uri.Scheme, host, uri.Port)
        {
            Path = uri.AbsolutePath.TrimEnd('/')
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private async Task<string> RunAwsAsync(string arguments)
    {
        return await RunProcessAsync(
            "aws",
            $"{arguments} --region us-east-1 --endpoint-url \"{_awsEndpoint}\"",
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
        startInfo.Environment["AWS_DEFAULT_REGION"] = "us-east-1";
        startInfo.Environment["AWS_REGION"] = "us-east-1";
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

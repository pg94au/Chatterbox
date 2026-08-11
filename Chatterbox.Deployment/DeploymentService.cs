using Amazon;
using Amazon.CDK;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Chatterbox.Deployment;

/// <summary>
/// Deploys the Chatterbox infrastructure using AWS CDK v2 and the AWS SDK.
/// Equivalent to the original <c>deploy.ps1</c> workflow.
/// </summary>
public sealed class DeploymentService
{
    private const string ArtifactPrefix = "chatterbox-";
    private static readonly TimeSpan MaxStatusPollingDelay = TimeSpan.FromMinutes(30);

    private readonly DeploymentOptions _options;
    private readonly RegionEndpoint _regionEndpoint;

    /// <summary>
    /// Initializes a new <see cref="DeploymentService"/>.
    /// </summary>
    public DeploymentService(DeploymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.S3Bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Region);

        _options = options;
        _regionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
    }

    /// <summary>
    /// Deploys the Chatterbox stack and returns deployment outputs.
    /// </summary>
    public async Task<DeploymentResult> DeployAsync(CancellationToken cancellationToken = default)
    {
        string projectRoot = ResolveProjectRoot();
        string backendRoot = Path.Combine(projectRoot, "Chatterbox.Backend");
        string publishDirectory = Path.Combine(backendRoot, "publish");
        string zipPath = Path.Combine(publishDirectory, "lambda.zip");

        BuildLambda(backendRoot);
        PackageLambda(publishDirectory, zipPath);

        IAmazonS3 s3Client = CreateS3Client();
        await EnsureS3BucketAsync(s3Client, cancellationToken);
        await ApplyLifecyclePolicyAsync(s3Client, cancellationToken);
        await PurgeOldPublishArtifacts(publishDirectory, zipPath);

        string s3Key = ComputeArtifactKey(backendRoot);
        await UploadLambdaAsync(s3Client, zipPath, s3Key, cancellationToken);

        string templateBody = SynthesizeTemplate(projectRoot, s3Key);
        IAmazonCloudFormation cloudFormation = CreateCloudFormationClient();
        await DeployCloudFormationAsync(cloudFormation, templateBody, cancellationToken);

        return await ReadOutputsAsync(cloudFormation, s3Key, cancellationToken);
    }

    private static void BuildLambda(string backendRoot)
    {
        Console.WriteLine("[1/6] Building Lambda function...");

        using var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.Arguments = "publish -c Release -o publish";
        process.StartInfo.WorkingDirectory = backendRoot;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Lambda build failed: {error}");
        }

        Console.WriteLine("  Build successful");
    }

    private static void PackageLambda(string publishDirectory, string zipPath)
    {
        Console.WriteLine("[2/6] Packaging Lambda ZIP...");

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(publishDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        Console.WriteLine($"  Package created: {zipPath}");
    }

    private async Task EnsureS3BucketAsync(IAmazonS3 s3Client, CancellationToken cancellationToken)
    {
        Console.WriteLine("[3/6] Checking S3 bucket...");

        try
        {
            await s3Client.HeadBucketAsync(
                new HeadBucketRequest { BucketName = _options.S3Bucket },
                cancellationToken);
            Console.WriteLine($"  Bucket '{_options.S3Bucket}' exists");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException($"S3 bucket '{_options.S3Bucket}' does not exist. Create it before deploying with a non-interactive deployment library.");
        }
    }

    private async Task ApplyLifecyclePolicyAsync(IAmazonS3 s3Client, CancellationToken cancellationToken)
    {
        Console.WriteLine("[4/6] Applying S3 lifecycle policy...");

        var request = new PutLifecycleConfigurationRequest
        {
            BucketName = _options.S3Bucket,
            Configuration = new LifecycleConfiguration
            {
                Rules = new List<LifecycleRule>
                {
                    new()
                    {
                        Id = "ExpireDeploymentArtifacts",
                        Status = LifecycleRuleStatus.Enabled,
                        Filter = new LifecycleFilter
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = ArtifactPrefix }
                        },
                        Expiration = new LifecycleRuleExpiration { Days = 30 },
                        AbortIncompleteMultipartUpload = new AbortIncompleteMultipartUpload { DaysAfterInitiation = 7 }
                    }
                }
            }
        };

        await s3Client.PutLifecycleConfigurationAsync(request, cancellationToken);
        Console.WriteLine("  Lifecycle policy applied");
    }

    private static async Task PurgeOldPublishArtifacts(string publishDirectory, string zipPath)
    {
        // Remove stale artifacts so the ZIP represents the fresh build.
        await Task.Yield();

        foreach (string file in Directory.EnumerateFiles(publishDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (!file.Equals(zipPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private async Task UploadLambdaAsync(IAmazonS3 s3Client, string zipPath, string s3Key, CancellationToken cancellationToken)
    {
        Console.WriteLine("[5/6] Uploading to S3...");

        using FileStream fileStream = File.OpenRead(zipPath);

        var request = new PutObjectRequest
        {
            BucketName = _options.S3Bucket,
            Key = s3Key,
            InputStream = fileStream
        };

        await s3Client.PutObjectAsync(request, cancellationToken);
        Console.WriteLine($"  Uploaded to s3://{_options.S3Bucket}/{s3Key}");
    }

    private string SynthesizeTemplate(string projectRoot, string s3Key)
    {
        Console.WriteLine("[6/6] Synthesizing CloudFormation template...");

        string outputDirectory = Path.Combine(projectRoot, "Chatterbox.Deployment", "cdk.out");

        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        var app = new App(new AppProps { Outdir = outputDirectory });
        _ = new ChatterboxStack(
            app,
            _options.StackName,
            new ChatterboxStackProps
            {
                Env = new Environment { Region = _options.Region },
                DeploymentOptions = _options,
                LambdaCodeBucket = _options.S3Bucket,
                LambdaCodeKey = s3Key
            });
        app.Synth();

        string templatePath = Path.Combine(outputDirectory, $"{_options.StackName}.template.json");
        return File.ReadAllText(templatePath);
    }

    private async Task DeployCloudFormationAsync(IAmazonCloudFormation cloudFormation, string templateBody, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Deploying CloudFormation stack '{_options.StackName}'...");

        try
        {
            await cloudFormation.CreateStackAsync(
                new CreateStackRequest
                {
                    StackName = _options.StackName,
                    TemplateBody = templateBody,
                    Capabilities = new List<string> { Capability.CAPABILITY_NAMED_IAM },
                    OnFailure = OnFailure.ROLLBACK
                },
                cancellationToken);

            await WaitForStackAsync(cloudFormation, StackStatus.CREATE_COMPLETE, cancellationToken);
        }
        catch (AlreadyExistsException)
        {
            await cloudFormation.UpdateStackAsync(
                new UpdateStackRequest
                {
                    StackName = _options.StackName,
                    TemplateBody = templateBody,
                    Capabilities = new List<string> { Capability.CAPABILITY_NAMED_IAM }
                },
                cancellationToken);

            await WaitForStackAsync(cloudFormation, StackStatus.UPDATE_COMPLETE, cancellationToken);
        }
    }

    private async Task WaitForStackAsync(IAmazonCloudFormation cloudFormation, StackStatus terminalStatus, CancellationToken cancellationToken)
    {
        DateTime start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < MaxStatusPollingDelay)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await cloudFormation.DescribeStacksAsync(
                new DescribeStacksRequest { StackName = _options.StackName },
                cancellationToken);

            Stack? stack = response.Stacks.FirstOrDefault();
            if (stack is null)
            {
                throw new InvalidOperationException($"Stack '{_options.StackName}' not found during creation/update.");
            }

            switch (stack.StackStatus.Value)
            {
                case var s when s == terminalStatus:
                    return;
                case var s when s.EndsWith("_FAILED", StringComparison.Ordinal) || s.EndsWith("_ROLLBACK_COMPLETE", StringComparison.Ordinal):
                    string reason = stack.StackStatusReason ?? "No reason provided";
                    throw new InvalidOperationException($"CloudFormation stack '{_options.StackName}' failed with status {stack.StackStatus}: {reason}");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        throw new TimeoutException($"Timed out waiting for CloudFormation stack '{_options.StackName}' to reach {terminalStatus}.");
    }

    private async Task<DeploymentResult> ReadOutputsAsync(IAmazonCloudFormation cloudFormation, string s3Key, CancellationToken cancellationToken)
    {
        var response = await cloudFormation.DescribeStacksAsync(
            new DescribeStacksRequest { StackName = _options.StackName },
            cancellationToken);

        Stack stack = response.Stacks.First();
        Dictionary<string, string> outputs = stack.Outputs.ToDictionary(o => o.OutputKey, o => o.OutputValue, StringComparer.Ordinal);

        string endpoint = outputs.GetValueOrDefault("WebSocketEndpoint") ?? string.Empty;

        Console.WriteLine();
        Console.WriteLine("Stack Outputs:");
        Console.WriteLine($"  WebSocket Endpoint: {endpoint}");

        return new DeploymentResult(
            StackName: _options.StackName,
            WebSocketEndpoint: endpoint,
            WebSocketApiId: outputs.GetValueOrDefault("WebSocketApiId") ?? string.Empty,
            WebSocketApiExecutionArn: outputs.GetValueOrDefault("WebSocketApiExecutionArn") ?? string.Empty,
            LambdaFunctionArn: outputs.GetValueOrDefault("LambdaFunctionArn") ?? string.Empty,
            LambdaFunctionName: outputs.GetValueOrDefault("LambdaFunctionName") ?? string.Empty,
            ConnectionsTableName: outputs.GetValueOrDefault("ConnectionsTableName") ?? string.Empty,
            StageName: _options.StageName,
            LambdaCodeBucket: _options.S3Bucket,
            LambdaCodeKey: s3Key,
            AwsServiceUrl: string.IsNullOrWhiteSpace(_options.AwsServiceUrl) ? null : _options.AwsServiceUrl);
    }

    private string ComputeArtifactKey(string backendRoot)
    {
        string[] artifactInputs = Directory
            .EnumerateFiles(backendRoot, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f =>
            {
                string extension = Path.GetExtension(f).ToLowerInvariant();
                return extension is ".cs" or ".csproj" or ".json" or ".props" or ".targets" or ".config" or ".resx";
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        var hashBuilder = new StringBuilder();
        foreach (string file in artifactInputs)
        {
            string relativePath = file.Substring(backendRoot.Length + 1);
            hashBuilder.AppendLine(relativePath);
            hashBuilder.AppendLine(File.ReadAllText(file));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashBuilder.ToString()));
        string artifactHash = Convert.ToHexString(hash).ToLowerInvariant()[..12];

        return $"{ArtifactPrefix}{_options.Environment}/{artifactHash}/lambda.zip";
    }

    private string ResolveProjectRoot()
    {
        if (!string.IsNullOrWhiteSpace(_options.ProjectRootDirectory))
        {
            return Path.GetFullPath(_options.ProjectRootDirectory);
        }

        string assemblyLocation = typeof(DeploymentService).Assembly.Location;
        string? directory = Path.GetDirectoryName(assemblyLocation);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "Chatterbox.slnx")) ||
                Directory.Exists(Path.Combine(directory, "Chatterbox.Backend")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException("Unable to resolve project root directory.");
    }

    private IAmazonS3 CreateS3Client()
    {
        AmazonS3Config config = CreateSdkConfig();
        return new AmazonS3Client(config);
    }

    private IAmazonCloudFormation CreateCloudFormationClient()
    {
        AmazonCloudFormationConfig config = CreateSdkConfig();
        return new AmazonCloudFormationClient(config);
    }

    private T CreateSdkConfig<T>() where T : Amazon.Runtime.ClientConfig, new()
    {
        var config = new T
        {
            RegionEndpoint = _regionEndpoint,
            ServiceURL = string.IsNullOrWhiteSpace(_options.AwsServiceUrl) ? null : _options.AwsServiceUrl
        };

        return config;
    }
}

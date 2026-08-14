using NUnit.Framework;
using System.Diagnostics;
using System.IO.Compression;
using Amazon.CloudFormation.Model;
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
        await RunAwsAsync($"s3api create-bucket --bucket {bucketName} --create-bucket-configuration LocationConstraint=ca-central-1", awsEndpoint);

        await RunAwsAsync($"s3 cp \"{packagePath}\" s3://{bucketName}/{s3Key} --checksum-algorithm SHA256", awsEndpoint);
    }

    private static async Task<string> RunAwsAsync(string arguments, string awsEndpoint)
    {
        return await RunProcessAsync(
            "aws",
            $"{arguments} --endpoint-url \"{awsEndpoint}\"",
            FindRepositoryRoot());
    }

}

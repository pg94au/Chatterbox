namespace Chatterbox.Deployment;

/// <summary>
/// Input options for deploying the Chatterbox AWS stack.
/// Mirrors the parameters accepted by <c>deploy.ps1</c>.
/// </summary>
/// <param name="S3Bucket">S3 bucket used to store deployment artifacts.</param>
/// <param name="Environment">Deployment environment (e.g. dev, staging, prod).</param>
/// <param name="Region">AWS region to deploy into.</param>
/// <param name="AwsServiceUrl">Optional service endpoint URL for local AWS-compatible stacks (e.g. LocalStack/Floci).</param>
/// <param name="AwsInternalServiceUrl">Optional override used by the Lambda for the API Gateway management endpoint. If omitted, <see cref="AwsServiceUrl"/> is used.</param>
/// <param name="ProjectRootDirectory">Root of the repository. Defaults to the parent of the directory containing this assembly.</param>
public sealed record DeploymentOptions(
    string S3Bucket,
    string Environment = "prod",
    string Region = "ca-central-1",
    string AwsServiceUrl = "",
    string AwsInternalServiceUrl = "",
    string? ProjectRootDirectory = null)
{
    public string StackName => $"chatterbox-{Environment}";

    public string StageName => Environment;

    public string TableName => $"chatterbox-connections-{Environment}";

    public string LambdaHandler => "Chatterbox.Backend::Chatterbox.Backend.Functions_Handler_Generated::Handler";

    public string EffectiveAwsServiceUrl =>
        !string.IsNullOrWhiteSpace(AwsInternalServiceUrl)
            ? AwsInternalServiceUrl
            : AwsServiceUrl;
}

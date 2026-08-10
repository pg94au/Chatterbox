namespace Chatterbox.Deployment;

/// <summary>
/// Result of a Chatterbox deployment.
/// Mirrors the stack outputs produced by <c>template.yaml</c>.
/// </summary>
/// <param name="StackName">Name of the deployed CloudFormation stack.</param>
/// <param name="WebSocketEndpoint">WebSocket endpoint URL.</param>
/// <param name="WebSocketApiId">WebSocket API ID.</param>
/// <param name="WebSocketApiExecutionArn">WebSocket API execution ARN.</param>
/// <param name="LambdaFunctionArn">Lambda function ARN.</param>
/// <param name="LambdaFunctionName">Lambda function name.</param>
/// <param name="ConnectionsTableName">DynamoDB connections table name.</param>
/// <param name="StageName">API Gateway stage name.</param>
/// <param name="LambdaCodeBucket">S3 bucket containing the Lambda package.</param>
/// <param name="LambdaCodeKey">S3 key of the Lambda package.</param>
/// <param name="AwsServiceUrl">Override URL used for AWS service calls, if any.</param>
public sealed record DeploymentResult(
    string StackName,
    string WebSocketEndpoint,
    string WebSocketApiId,
    string WebSocketApiExecutionArn,
    string LambdaFunctionArn,
    string LambdaFunctionName,
    string ConnectionsTableName,
    string StageName,
    string LambdaCodeBucket,
    string LambdaCodeKey,
    string? AwsServiceUrl);

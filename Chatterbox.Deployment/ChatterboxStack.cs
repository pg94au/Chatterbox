using Amazon.CDK;
using Amazon.CDK.AWS.ApiGatewayV2;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;

namespace Chatterbox.Deployment;

/// <summary>
/// CDK v2 stack that recreates the resources declared in <c>template.yaml</c>.
/// Uses L1 constructs so the synthesized template matches the original
/// CloudFormation resource types, logical IDs, parameters, and outputs as
/// closely as possible.
/// </summary>
public sealed class ChatterboxStack : Stack
{
    public ChatterboxStack(Construct scope, string id, DeploymentOptions options)
        : base(scope, id, new StackProps
        {
            StackName = options.StackName,
            Description = "Chatterbox WebSocket Chat Backend - Lambda, DynamoDB, API Gateway, and IAM",
            Env = new Environment { Region = options.Region },
        })
    {
        var lambdaCodeBucket = new CfnParameter(this, "LambdaCodeBucket", new CfnParameterProps
        {
            Type = "String",
            Description = "S3 bucket containing the Lambda deployment package",
            MinLength = 3,
        });

        var lambdaCodeKey = new CfnParameter(this, "LambdaCodeKey", new CfnParameterProps
        {
            Type = "String",
            Description = "S3 key (path) to the Lambda ZIP file",
            MinLength = 1,
        });

        var lambdaHandler = new CfnParameter(this, "LambdaHandler", new CfnParameterProps
        {
            Type = "String",
            Description = "Lambda handler function",
            Default = options.LambdaHandler,
        });

        var stageName = new CfnParameter(this, "StageName", new CfnParameterProps
        {
            Type = "String",
            Description = "API Gateway stage name",
            Default = options.StageName,
            AllowedValues = new[] { "dev", "staging", "prod" },
        });

        var tableName = new CfnParameter(this, "TableName", new CfnParameterProps
        {
            Type = "String",
            Description = "DynamoDB table name for connection tracking",
            Default = options.TableName,
        });

        var awsServiceUrl = new CfnParameter(this, "AwsServiceUrl", new CfnParameterProps
        {
            Type = "String",
            Description = "Override URL for the API Gateway management API endpoint",
            Default = "",
        });

        var commonTags = new[]
        {
            new CfnTag { Key = "Application", Value = "Chatterbox" },
            new CfnTag { Key = "Environment", Value = stageName.ValueAsString },
        };

        var apiGatewayTags = new Dictionary<string, string>
        {
            ["Application"] = "Chatterbox",
            ["Environment"] = stageName.ValueAsString,
        };

        var connectionsTable = new CfnTable(this, "ConnectionsTable", new CfnTableProps
        {
            TableName = $"{tableName.ValueAsString}-{stageName.ValueAsString}",
            BillingMode = "PAY_PER_REQUEST",
            AttributeDefinitions = new[]
            {
                new CfnTable.AttributeDefinitionProperty { AttributeName = "displayName", AttributeType = "S" },
                new CfnTable.AttributeDefinitionProperty { AttributeName = "connectionId", AttributeType = "S" },
            },
            KeySchema = new[]
            {
                new CfnTable.KeySchemaProperty { AttributeName = "displayName", KeyType = "HASH" },
            },
            GlobalSecondaryIndexes = new[]
            {
                new CfnTable.GlobalSecondaryIndexProperty
                {
                    IndexName = "ConnectionIndex",
                    KeySchema = new[]
                    {
                        new CfnTable.KeySchemaProperty { AttributeName = "connectionId", KeyType = "HASH" },
                    },
                    Projection = new CfnTable.ProjectionProperty { ProjectionType = "ALL" },
                },
            },
            Tags = commonTags,
        });

        var lambdaExecutionRole = new CfnRole(this, "LambdaExecutionRole", new CfnRoleProps
        {
            RoleName = $"chatterbox-websocket-lambda-role-{stageName.ValueAsString}",
            AssumeRolePolicyDocument = new Dictionary<string, object>
            {
                ["Version"] = "2012-10-17",
                ["Statement"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Allow",
                        ["Principal"] = new Dictionary<string, string> { ["Service"] = "lambda.amazonaws.com" },
                        ["Action"] = "sts:AssumeRole",
                    },
                },
            },
            ManagedPolicyArns = new[] { "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole" },
            Tags = commonTags,
        });

        _ = new CfnPolicy(this, "LambdaAccessPolicy", new CfnPolicyProps
        {
            PolicyName = $"chatterbox-lambda-access-policy-{stageName.ValueAsString}",
            Roles = new[] { lambdaExecutionRole.Ref },
            PolicyDocument = new Dictionary<string, object>
            {
                ["Version"] = "2012-10-17",
                ["Statement"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Allow",
                        ["Action"] = new[]
                        {
                            "dynamodb:DescribeTable",
                            "dynamodb:GetItem",
                            "dynamodb:PutItem",
                            "dynamodb:UpdateItem",
                            "dynamodb:DeleteItem",
                            "dynamodb:Query",
                            "dynamodb:Scan",
                        },
                        ["Resource"] = new[]
                        {
                            connectionsTable.AttrArn,
                            Fn.Sub("${TableArn}/index/*", new Dictionary<string, string> { ["TableArn"] = connectionsTable.AttrArn }),
                        },
                    },
                    new Dictionary<string, object>
                    {
                        ["Effect"] = "Allow",
                        ["Action"] = "execute-api:ManageConnections",
                        ["Resource"] = new[]
                        {
                            Fn.Sub(
                                "arn:aws:execute-api:${AWS::Region}:${AWS::AccountId}:${WebSocketApi}/${StageName}/POST/@connections/*",
                                new Dictionary<string, string>
                                {
                                    ["WebSocketApi"] = webSocketApi.Ref,
                                    ["StageName"] = stageName.ValueAsString,
                                }),
                        },
                    },
                },
            },
        });

        var webSocketHandler = new CfnFunction(this, "WebSocketHandler", new CfnFunctionProps
        {
            FunctionName = $"chatterbox-websocket-handler-{stageName.ValueAsString}",
            Runtime = "dotnet10",
            Handler = lambdaHandler.ValueAsString,
            Role = lambdaExecutionRole.AttrArn,
            Code = new CfnFunction.CodeProperty
            {
                S3Bucket = lambdaCodeBucket.ValueAsString,
                S3Key = lambdaCodeKey.ValueAsString,
            },
            MemorySize = 512,
            Timeout = 30,
            Environment = new CfnFunction.EnvironmentProperty
            {
                Variables = new Dictionary<string, string>
                {
                    ["CONNECTIONS_TABLE"] = connectionsTable.Ref,
                    ["AWS_SERVICE_URL"] = awsServiceUrl.ValueAsString,
                },
            },
            Tags = commonTags,
        });

        var webSocketApi = new CfnApi(this, "WebSocketApi", new CfnApiProps
        {
            Name = $"chatterbox-websocket-api-{stageName.ValueAsString}",
            ProtocolType = "WEBSOCKET",
            RouteSelectionExpression = "$request.body.action",
            Tags = apiGatewayTags,
        });

        var accessLogGroup = new CfnLogGroup(this, "WebSocketAccessLogGroup", new CfnLogGroupProps
        {
            LogGroupName = $"/aws/apigateway/chatterbox-websocket-api-{stageName.ValueAsString}",
            RetentionInDays = 14,
        });

        var webSocketIntegration = new CfnIntegration(this, "WebSocketIntegration", new CfnIntegrationProps
        {
            ApiId = webSocketApi.Ref,
            IntegrationType = "AWS_PROXY",
            IntegrationUri = Fn.Sub(
                "arn:aws:apigateway:${AWS::Region}:lambda:path/2015-03-31/functions/${WebSocketHandlerArn}/invocations",
                new Dictionary<string, string> { ["WebSocketHandlerArn"] = webSocketHandler.AttrArn }),
            IntegrationMethod = "POST",
        });

        _ = new CfnRoute(this, "ConnectRoute", new CfnRouteProps
        {
            ApiId = webSocketApi.Ref,
            RouteKey = "$connect",
            AuthorizationType = "NONE",
            Target = Fn.Sub("integrations/${WebSocketIntegration}", new Dictionary<string, string> { ["WebSocketIntegration"] = webSocketIntegration.Ref }),
        });

        _ = new CfnRoute(this, "DisconnectRoute", new CfnRouteProps
        {
            ApiId = webSocketApi.Ref,
            RouteKey = "$disconnect",
            AuthorizationType = "NONE",
            Target = Fn.Sub("integrations/${WebSocketIntegration}", new Dictionary<string, string> { ["WebSocketIntegration"] = webSocketIntegration.Ref }),
        });

        _ = new CfnRoute(this, "RegisterRoute", new CfnRouteProps
        {
            ApiId = webSocketApi.Ref,
            RouteKey = "register",
            AuthorizationType = "NONE",
            Target = Fn.Sub("integrations/${WebSocketIntegration}", new Dictionary<string, string> { ["WebSocketIntegration"] = webSocketIntegration.Ref }),
        });

        _ = new CfnRoute(this, "ListUsersRoute", new CfnRouteProps
        {
            ApiId = webSocketApi.Ref,
            RouteKey = "listUsers",
            AuthorizationType = "NONE",
            Target = Fn.Sub("integrations/${WebSocketIntegration}", new Dictionary<string, string> { ["WebSocketIntegration"] = webSocketIntegration.Ref }),
        });

        _ = new CfnRoute(this, "MessageRoute", new CfnRouteProps
        {
            ApiId = webSocketApi.Ref,
            RouteKey = "message",
            AuthorizationType = "NONE",
            Target = Fn.Sub("integrations/${WebSocketIntegration}", new Dictionary<string, string> { ["WebSocketIntegration"] = webSocketIntegration.Ref }),
        });

        _ = new CfnStage(this, "WebSocketStage", new CfnStageProps
        {
            ApiId = webSocketApi.Ref,
            StageName = stageName.ValueAsString,
            AutoDeploy = true,
            AccessLogSettings = new CfnStage.AccessLogSettingsProperty
            {
                DestinationArn = accessLogGroup.AttrArn,
                Format = "{\"requestId\":\"$context.requestId\",\"eventType\":\"$context.eventType\",\"routeKey\":\"$context.routeKey\",\"connectionId\":\"$context.connectionId\",\"status\":\"$context.status\",\"ip\":\"$context.identity.sourceIp\",\"userAgent\":\"$context.identity.userAgent\",\"requestTime\":\"$context.requestTime\"}",
            },
            DefaultRouteSettings = new CfnStage.RouteSettingsProperty
            {
                DetailedMetricsEnabled = true,
                LoggingLevel = "INFO",
            },
            Tags = apiGatewayTags,
        });

        _ = new CfnPermission(this, "ApiInvokePermission", new CfnPermissionProps
        {
            FunctionName = webSocketHandler.Ref,
            Action = "lambda:InvokeFunction",
            Principal = "apigateway.amazonaws.com",
            SourceArn = Fn.Sub("arn:aws:execute-api:${AWS::Region}:${AWS::AccountId}:${WebSocketApi}/*/*", new Dictionary<string, string> { ["WebSocketApi"] = webSocketApi.Ref }),
        });

        _ = new CfnOutput(this, "WebSocketApiId", new CfnOutputProps
        {
            Description = "WebSocket API ID",
            Value = webSocketApi.Ref,
            ExportName = Fn.Sub("${AWS::StackName}-WebSocketApiId"),
        });

        _ = new CfnOutput(this, "WebSocketApiExecutionArn", new CfnOutputProps
        {
            Description = "WebSocket API Execution ARN",
            Value = Fn.Sub("arn:aws:execute-api:${AWS::Region}:${AWS::AccountId}:${WebSocketApi}", new Dictionary<string, string> { ["WebSocketApi"] = webSocketApi.Ref }),
            ExportName = Fn.Sub("${AWS::StackName}-WebSocketApiExecutionArn"),
        });

        _ = new CfnOutput(this, "WebSocketEndpoint", new CfnOutputProps
        {
            Description = "WebSocket endpoint URL",
            Value = Fn.Sub("wss://${WebSocketApi}.execute-api.${AWS::Region}.amazonaws.com/${StageName}", new Dictionary<string, string>
            {
                ["WebSocketApi"] = webSocketApi.Ref,
                ["StageName"] = stageName.ValueAsString,
            }),
            ExportName = Fn.Sub("${AWS::StackName}-WebSocketEndpoint"),
        });

        _ = new CfnOutput(this, "LambdaFunctionArn", new CfnOutputProps
        {
            Description = "Lambda function ARN",
            Value = webSocketHandler.AttrArn,
            ExportName = Fn.Sub("${AWS::StackName}-LambdaFunctionArn"),
        });

        _ = new CfnOutput(this, "LambdaFunctionName", new CfnOutputProps
        {
            Description = "Lambda function name",
            Value = webSocketHandler.Ref,
            ExportName = Fn.Sub("${AWS::StackName}-LambdaFunctionName"),
        });

        _ = new CfnOutput(this, "ConnectionsTableName", new CfnOutputProps
        {
            Description = "DynamoDB connections table name",
            Value = connectionsTable.Ref,
            ExportName = Fn.Sub("${AWS::StackName}-ConnectionsTableName"),
        });

        _ = new CfnOutput(this, "StageName", new CfnOutputProps
        {
            Description = "API Gateway stage name",
            Value = stageName.ValueAsString,
            ExportName = Fn.Sub("${AWS::StackName}-StageName"),
        });
    }
}

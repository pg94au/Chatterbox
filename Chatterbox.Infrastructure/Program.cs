using Pulumi;
using Pulumi.Aws.ApiGatewayV2;
using Pulumi.Aws.DynamoDB;
using Pulumi.Aws.DynamoDB.Inputs;
using Pulumi.Aws.Iam;
using Pulumi.Aws.Lambda;
using Pulumi.Aws.Lambda.Inputs;
using System.Text.Json;
using WebSocketDeployment = Pulumi.Aws.ApiGatewayV2.Deployment;

return await Pulumi.Deployment.RunAsync(() =>
{
    var config = new Config();
    var lambdaPackagePath = config.Get("lambdaPackagePath") ?? "../Chatterbox.Backend/publish/lambda.zip";
    var lambdaHandler = config.Get("lambdaHandler")
        ?? "Chatterbox.Backend::Chatterbox.Backend.Functions_Handler_Generated::Handler";
    var stageName = config.Get("stageName") ?? "prod";
    var tableName = config.Get("tableName") ?? "chatterbox-connections";

    // Validate Lambda package exists
    var absoluteLambdaPath = Path.GetFullPath(lambdaPackagePath);
    if (!File.Exists(absoluteLambdaPath))
    {
        throw new FileNotFoundException(
            $"Lambda package not found at: {absoluteLambdaPath}\n\n" +
            "Please build and package the Lambda function first:\n" +
            "  cd Chatterbox.Backend\n" +
            "  dotnet publish -c Release -o publish\n" +
            "  Compress-Archive -Path publish\\* -DestinationPath publish\\lambda.zip -Force\n\n" +
            "Or set a custom path:\n" +
            "  pulumi config set lambdaPackagePath <path-to-lambda.zip>");
    }

    var lambdaAssumeRolePolicy = """
    {
      "Version": "2012-10-17",
      "Statement": [
        {
          "Effect": "Allow",
          "Principal": {
            "Service": "lambda.amazonaws.com"
          },
          "Action": "sts:AssumeRole"
        }
      ]
    }
    """;

    var lambdaRole = new Role("chatWebsocketLambdaRole", new RoleArgs
    {
        Name = "chatterbox-websocket-lambda-role",
        AssumeRolePolicy = lambdaAssumeRolePolicy,
        Tags =
        {
            { "Application", "Chatterbox" },
            { "ManagedBy", "Pulumi" }
        }
    });

    _ = new RolePolicyAttachment("chatWebsocketLambdaBasicExecution", new RolePolicyAttachmentArgs
    {
        Role = lambdaRole.Name,
        PolicyArn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
    });

    var connectionsTable = new Table("chatConnectionsTable", new TableArgs
    {
        Name = tableName,
        BillingMode = "PAY_PER_REQUEST",
        HashKey = "displayName",
        Attributes =
        {
            new TableAttributeArgs
            {
                Name = "displayName",
                Type = "S"
            },
            new TableAttributeArgs
            {
                Name = "connectionId",
                Type = "S"
            },
            new TableAttributeArgs
            {
                Name = "connectedAt",
                Type = "N"
            }
        },
        GlobalSecondaryIndexes =
        {
            new TableGlobalSecondaryIndexArgs
            {
                Name = "ConnectionIndex",
                HashKey = "connectionId",
                ProjectionType = "ALL"
            }
        },
        Tags =
        {
            { "Application", "Chatterbox" },
            { "ManagedBy", "Pulumi" }
        }
    });

    var websocketFunction = new Function("chatWebsocketHandler", new FunctionArgs
    {
        Name = "chatterbox-websocket-handler",
        Runtime = Runtime.Dotnet10,
        Handler = lambdaHandler,
        Role = lambdaRole.Arn,
        MemorySize = 512,
        Timeout = 30,
        Code = new FileArchive(absoluteLambdaPath),
        Environment = new FunctionEnvironmentArgs
        {
            Variables =
            {
                { "CONNECTIONS_TABLE", connectionsTable.Name }
            }
        },
        Tags =
        {
            { "Application", "Chatterbox" },
            { "ManagedBy", "Pulumi" }
        }
    });

    var websocketApi = new Api("chatWebsocketApi", new ApiArgs
    {
        Name = "chatterbox-websocket-api",
        ProtocolType = "WEBSOCKET",
        RouteSelectionExpression = "$request.body.action",
        Tags =
        {
            { "Application", "Chatterbox" },
            { "ManagedBy", "Pulumi" }
        }
    });

    var websocketIntegration = new Integration("chatWebsocketIntegration", new IntegrationArgs
    {
        ApiId = websocketApi.Id,
        IntegrationType = "AWS_PROXY",
        IntegrationMethod = "POST",
        IntegrationUri = websocketFunction.InvokeArn
    });

    var integrationTarget = websocketIntegration.Id.Apply(id => $"integrations/{id}");

    var connectRoute = new Route("chatWebsocketConnectRoute", new RouteArgs
    {
        ApiId = websocketApi.Id,
        RouteKey = "$connect",
        AuthorizationType = "NONE",
        Target = integrationTarget
    });

    var disconnectRoute = new Route("chatWebsocketDisconnectRoute", new RouteArgs
    {
        ApiId = websocketApi.Id,
        RouteKey = "$disconnect",
        AuthorizationType = "NONE",
        Target = integrationTarget
    });

    var registerRoute = new Route("chatWebsocketRegisterRoute", new RouteArgs
    {
        ApiId = websocketApi.Id,
        RouteKey = "register",
        AuthorizationType = "NONE",
        Target = integrationTarget
    });

    var listUsersRoute = new Route("chatWebsocketListUsersRoute", new RouteArgs
    {
        ApiId = websocketApi.Id,
        RouteKey = "listUsers",
        AuthorizationType = "NONE",
        Target = integrationTarget
    });

    var messageRoute = new Route("chatWebsocketMessageRoute", new RouteArgs
    {
        ApiId = websocketApi.Id,
        RouteKey = "message",
        AuthorizationType = "NONE",
        Target = integrationTarget
    });

    var deployment = new WebSocketDeployment("chatWebsocketDeployment", new DeploymentArgs
    {
        ApiId = websocketApi.Id,
        Description = "WebSocket deployment for the Chatterbox backend"
    }, new CustomResourceOptions
    {
        DependsOn =
        {
            connectRoute,
            disconnectRoute,
            registerRoute,
            listUsersRoute,
            messageRoute
        }
    });

    var stage = new Stage("chatWebsocketStage", new StageArgs
    {
        ApiId = websocketApi.Id,
        Name = stageName,
        DeploymentId = deployment.Id,
        AutoDeploy = false
    });

    _ = new RolePolicy("chatWebsocketLambdaAccessPolicy", new RolePolicyArgs
    {
        Role = lambdaRole.Id,
        Policy = Output.Tuple(connectionsTable.Arn, websocketApi.ExecutionArn, stage.Name).Apply(values =>
        {
            var tableArn = values.Item1;
            var executionArn = values.Item2;
            var currentStage = values.Item3;
            var tableIndexArn = $"{tableArn}/index/*";
            var manageConnectionsArn = $"{executionArn}/{currentStage}/POST/@connections/*";

            return JsonSerializer.Serialize(new
            {
                Version = "2012-10-17",
                Statement = new object[]
                {
                    new
                    {
                        Effect = "Allow",
                        Action = new[]
                        {
                            "dynamodb:DescribeTable",
                            "dynamodb:GetItem",
                            "dynamodb:PutItem",
                            "dynamodb:UpdateItem",
                            "dynamodb:DeleteItem",
                            "dynamodb:Query",
                            "dynamodb:Scan"
                        },
                        Resource = new[]
                        {
                            tableArn,
                            tableIndexArn
                        }
                    },
                    new
                    {
                        Effect = "Allow",
                        Action = new[]
                        {
                            "execute-api:ManageConnections"
                        },
                        Resource = new[]
                        {
                            manageConnectionsArn
                        }
                    }
                }
            });
        })
    });

    _ = new Permission("chatWebsocketApiInvokePermission", new PermissionArgs
    {
        Action = "lambda:InvokeFunction",
        Function = websocketFunction.Name,
        Principal = "apigateway.amazonaws.com",
        SourceArn = Output.Format($"{websocketApi.ExecutionArn}/*/*")
    });

    return new Dictionary<string, object?>
    {
        ["websocketApiId"] = websocketApi.Id,
        ["websocketApiExecutionArn"] = websocketApi.ExecutionArn,
        ["websocketEndpoint"] = stage.InvokeUrl,
        ["lambdaFunctionArn"] = websocketFunction.Arn,
        ["lambdaFunctionName"] = websocketFunction.Name,
        ["connectionsTableName"] = connectionsTable.Name,
        ["stageName"] = stage.Name
    };
});

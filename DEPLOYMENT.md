# Chatterbox Deployment Guide

This guide covers deploying the Chatterbox WebSocket backend to AWS using CloudFormation.

## Prerequisites

- AWS CLI configured with credentials
- .NET 10 SDK installed
- PowerShell (for build scripts)
- S3 bucket for Lambda code (create one if needed)

## Deployment Steps

### 1. Build and Package Lambda

```powershell
# Build the Lambda project
cd Chatterbox.Backend
dotnet publish -c Release -o publish

# Package as ZIP
Compress-Archive -Path publish\* -DestinationPath publish\lambda.zip -Force
```

### 2. Upload Lambda Package to S3

```powershell
# Create S3 bucket (if you don't have one)
aws s3 mb s3://your-lambda-deployments --region us-east-1

# Upload the Lambda ZIP
aws s3 cp publish\lambda.zip s3://your-lambda-deployments/chatterbox-backend/lambda.zip
```

### 3. Deploy CloudFormation Stack

```powershell
cd ..\

# Deploy with default parameters
aws cloudformation deploy `
  --template-file template.yaml `
  --stack-name chatterbox-backend `
  --parameter-overrides `
	LambdaCodeBucket=your-lambda-deployments `
	LambdaCodeKey=chatterbox-backend/lambda.zip `
  --capabilities CAPABILITY_NAMED_IAM `
  --region us-east-1
```

### 4. Get WebSocket Endpoint

```powershell
# Get the WebSocket URL
aws cloudformation describe-stacks `
  --stack-name chatterbox-backend `
  --query 'Stacks[0].Outputs[?OutputKey==`WebSocketEndpoint`].OutputValue' `
  --output text
```

## Parameters

You can customize the deployment with these parameters:

- **LambdaCodeBucket**: S3 bucket containing lambda.zip (required)
- **LambdaCodeKey**: S3 key path (default: `chatterbox-backend/lambda.zip`)
- **LambdaHandler**: Handler function (default: auto-generated)
- **StageName**: API stage (default: `prod`, options: `dev`, `staging`, `prod`)
- **TableName**: DynamoDB table name (default: `chatterbox-connections`)

Example with custom parameters:

```powershell
aws cloudformation deploy `
  --template-file template.yaml `
  --stack-name chatterbox-backend-dev `
  --parameter-overrides `
	LambdaCodeBucket=my-bucket `
	LambdaCodeKey=lambda/chatterbox.zip `
	StageName=dev `
	TableName=chatterbox-dev-connections `
  --capabilities CAPABILITY_NAMED_IAM `
  --region us-east-1
```

## Updates

To update the stack after code changes:

```powershell
# 1. Rebuild and reupload Lambda
cd Chatterbox.Backend
dotnet publish -c Release -o publish
Compress-Archive -Path publish\* -DestinationPath publish\lambda.zip -Force
aws s3 cp publish\lambda.zip s3://your-lambda-deployments/chatterbox-backend/lambda.zip

# 2. Update stack (forces Lambda to reload)
cd ..\
aws cloudformation deploy `
  --template-file template.yaml `
  --stack-name chatterbox-backend `
  --parameter-overrides LambdaCodeBucket=your-lambda-deployments `
  --capabilities CAPABILITY_NAMED_IAM
```

## Resources Created

The CloudFormation stack creates:

- **DynamoDB Table**: Connection tracking with GSI on connectionId
- **IAM Role**: Lambda execution role with CloudWatch Logs access
- **IAM Policy**: DynamoDB and API Gateway permissions
- **Lambda Function**: WebSocket handler (.NET 10 custom runtime)
- **API Gateway**: WebSocket API with 5 routes ($connect, $disconnect, register, listUsers, message)
- **API Gateway Stage**: Deployment stage (prod/dev/staging)
- **Lambda Permission**: Allows API Gateway to invoke Lambda

## Outputs

After deployment, the stack exports:

- **WebSocketEndpoint**: The wss:// URL to connect to
- **LambdaFunctionArn**: Lambda function ARN
- **ConnectionsTableName**: DynamoDB table name
- **WebSocketApiId**: API Gateway ID

## Delete Stack

```powershell
aws cloudformation delete-stack --stack-name chatterbox-backend
```

## Troubleshooting

**Lambda fails to invoke:**
- Check CloudWatch Logs for errors: `/aws/lambda/chatterbox-websocket-handler`
- Verify Lambda has correct IAM permissions
- Ensure CONNECTIONS_TABLE environment variable is set

**WebSocket connection fails:**
- Verify the endpoint URL format: `wss://xxxxx.execute-api.region.amazonaws.com/prod`
- Check API Gateway execution logs (enable in stage settings)

**DynamoDB errors:**
- Verify Lambda role has dynamodb:* permissions on the table
- Check table exists and is in ACTIVE state

## .NET 10 Runtime Note

AWS Lambda doesn't natively support .NET 10 yet. The template uses `provided.al2023` runtime, which requires:

1. Your publish output must be self-contained or include the .NET runtime
2. Update your .csproj to target `linux-x64` for Lambda:

```xml
<PublishAot>false</PublishAot>
<RuntimeIdentifier>linux-x64</RuntimeIdentifier>
<SelfContained>false</SelfContained>
```

Alternatively, wait for AWS to release native .NET 10 support and update the template to use `dotnet10` runtime.

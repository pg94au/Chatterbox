# CloudFormation Deployment Instructions

## Prerequisites

1. **AWS CLI installed and configured**
   ```powershell
   aws configure
   ```

2. **Build and package the Lambda function**
   ```powershell
   cd Chatterbox.Backend
   dotnet publish -c Release -o publish
   Compress-Archive -Path publish\* -DestinationPath publish\lambda.zip -Force
   ```

3. **Upload Lambda package to S3**
   ```powershell
   # Create an S3 bucket (one-time setup)
   aws s3 mb s3://your-lambda-deployments-bucket

   # Upload the Lambda ZIP
   aws s3 cp publish\lambda.zip s3://your-lambda-deployments-bucket/chatterbox-backend/lambda.zip
   ```

## Deploy the Stack

### Option 1: Deploy with S3 Code Location

```powershell
aws cloudformation deploy `
  --template-file template.yaml `
  --stack-name chatterbox-backend `
  --parameter-overrides `
	LambdaCodeBucket=your-lambda-deployments-bucket `
	LambdaCodeKey=chatterbox-backend/lambda.zip `
	StageName=prod `
  --capabilities CAPABILITY_NAMED_IAM
```

### Option 2: Use Parameter File

Create a `parameters.json`:
```json
[
  {
	"ParameterKey": "LambdaCodeBucket",
	"ParameterValue": "your-lambda-deployments-bucket"
  },
  {
	"ParameterKey": "LambdaCodeKey",
	"ParameterValue": "chatterbox-backend/lambda.zip"
  },
  {
	"ParameterKey": "StageName",
	"ParameterValue": "prod"
  },
  {
	"ParameterKey": "TableName",
	"ParameterValue": "chatterbox-connections"
  }
]
```

Deploy:
```powershell
aws cloudformation deploy `
  --template-file template.yaml `
  --stack-name chatterbox-backend `
  --parameter-overrides file://parameters.json `
  --capabilities CAPABILITY_NAMED_IAM
```

## View Outputs

After deployment completes, view the WebSocket endpoint and other outputs:

```powershell
aws cloudformation describe-stacks --stack-name chatterbox-backend --query 'Stacks[0].Outputs'
```

## Update the Stack

After making changes to Lambda code:

1. Rebuild and package
2. Upload new ZIP to S3
3. Update Lambda function:
   ```powershell
   aws lambda update-function-code `
	 --function-name chatterbox-websocket-handler `
	 --s3-bucket your-lambda-deployments-bucket `
	 --s3-key chatterbox-backend/lambda.zip
   ```

Or redeploy the entire stack:
```powershell
aws cloudformation deploy `
  --template-file template.yaml `
  --stack-name chatterbox-backend `
  --capabilities CAPABILITY_NAMED_IAM
```

## Delete the Stack

To remove all resources:

```powershell
aws cloudformation delete-stack --stack-name chatterbox-backend
```

## Important Notes

### .NET Runtime Version
- **CloudFormation template uses `dotnet8` runtime** because AWS Lambda doesn't support .NET 10 yet
- Update the runtime in `template.yaml` when .NET 10 becomes available
- Alternatively, use a custom runtime with .NET 10

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `LambdaCodeBucket` | (empty) | S3 bucket containing Lambda ZIP |
| `LambdaCodeKey` | `chatterbox-backend/lambda.zip` | S3 path to ZIP file |
| `LambdaHandler` | `Chatterbox.Backend::Chatterbox.Backend.Functions_Handler_Generated::Handler` | Lambda handler |
| `StageName` | `prod` | API Gateway stage (dev/staging/prod) |
| `TableName` | `chatterbox-connections` | DynamoDB table name |

### Resource Names

The stack creates resources with these names:
- Lambda: `chatterbox-websocket-handler`
- DynamoDB: `chatterbox-connections` (or custom)
- API Gateway: `chatterbox-websocket-api`
- IAM Role: `chatterbox-websocket-lambda-role`

## Troubleshooting

### Permission Errors
Ensure your AWS credentials have permissions for:
- CloudFormation (create/update/delete stacks)
- Lambda (create/update functions)
- DynamoDB (create tables)
- API Gateway (create APIs)
- IAM (create roles/policies)

### Lambda Code Not Found
- Verify S3 bucket and key are correct
- Ensure Lambda ZIP exists at the specified location
- Check bucket permissions allow Lambda service access

### Stack Rollback
If deployment fails, check the CloudFormation events:
```powershell
aws cloudformation describe-stack-events --stack-name chatterbox-backend
```

# Chatterbox Multi-Environment Deployment

## Overview

The deployment script supports multiple isolated environments in the same AWS account/region.

## Usage

```powershell
.\deploy.ps1 -S3Bucket <bucket-name> [-Environment <env-name>] [-Region <region>]
```

## Parameters

- **`S3Bucket`** (required) - S3 bucket for Lambda deployment packages
- **`Environment`** (optional, default: `prod`) - Environment name for resource isolation
- **`Region`** (optional, default: `ca-central-1`) - AWS region

## Examples

**Production deployment:**
```powershell
.\deploy.ps1 -S3Bucket my-deployment-bucket -Environment prod
```

**Test/experimental deployment:**
```powershell
.\deploy.ps1 -S3Bucket my-deployment-bucket -Environment test
```

**Development deployment:**
```powershell
.\deploy.ps1 -S3Bucket my-deployment-bucket -Environment dev
```

## Resource Isolation

Each environment gets its own:

- **CloudFormation Stack**: `chatterbox-{environment}`
- **Lambda Function**: `chatterbox-{environment}-handler`
- **API Gateway Stage**: `{environment}`
- **DynamoDB Table**: `chatterbox-{environment}-connections`
- **IAM Role**: `chatterbox-{environment}-lambda-role`
- **S3 Package Path**: `chatterbox-{environment}/lambda.zip`

## Benefits

- ✅ Test changes without affecting production
- ✅ Multiple developers can have isolated stacks
- ✅ Easy to tear down test environments
- ✅ Each environment has its own WebSocket endpoint
- ✅ No resource conflicts or shared state

## Cleanup

To delete an environment:

```powershell
aws cloudformation delete-stack --stack-name chatterbox-test --region ca-central-1
```

## Example Workflow

```powershell
# Deploy stable production
.\deploy.ps1 -S3Bucket my-bucket -Environment prod

# Deploy experimental test version
.\deploy.ps1 -S3Bucket my-bucket -Environment test

# Make changes, test against 'test' environment
# ...

# When satisfied, deploy to production
.\deploy.ps1 -S3Bucket my-bucket -Environment prod

# Clean up test environment
aws cloudformation delete-stack --stack-name chatterbox-test
```

## CloudFormation Template Requirements

Your `template.yaml` must:

1. Accept an `Environment` parameter:
```yaml
Parameters:
  Environment:
	Type: String
	Default: prod
	Description: Deployment environment name
```

2. Use it to name resources:
```yaml
Resources:
  LambdaFunction:
	Type: AWS::Lambda::Function
	Properties:
	  FunctionName: !Sub chatterbox-${Environment}-handler

  ConnectionsTable:
	Type: AWS::DynamoDB::Table
	Properties:
	  TableName: !Sub chatterbox-${Environment}-connections
```

## Notes

- The IAM policy in `iam-deployment-policy.json` already supports `chatterbox-*` wildcard naming
- Each environment is completely isolated - changes to one don't affect others
- Resource names follow the pattern `chatterbox-{environment}-{resource}`

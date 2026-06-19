# Chatterbox Deployment IAM Setup

## Problem
Your IAM user (`limited`) doesn't have sufficient permissions to deploy the Chatterbox infrastructure.

## Solution

### Option 1: Attach Policy to Existing User (Recommended)

1. **Create the custom policy:**
   ```bash
   aws iam create-policy \
	 --policy-name ChatterboxDeploymentPolicy \
	 --policy-document file://iam-deployment-policy.json
   ```

2. **Attach the policy to your user:**
   ```bash
   aws iam attach-user-policy \
	 --user-name limited \
	 --policy-arn arn:aws:iam::663866322745:policy/ChatterboxDeploymentPolicy
   ```

### Option 2: Create a New Deployment User

1. **Create a new IAM user:**
   ```bash
   aws iam create-user --user-name chatterbox-deployer
   ```

2. **Create the policy** (same as Option 1 step 1)

3. **Attach the policy:**
   ```bash
   aws iam attach-user-policy \
	 --user-name chatterbox-deployer \
	 --policy-arn arn:aws:iam::663866322745:policy/ChatterboxDeploymentPolicy
   ```

4. **Create access keys:**
   ```bash
   aws iam create-access-key --user-name chatterbox-deployer
   ```

   Save the `AccessKeyId` and `SecretAccessKey` from the output.

5. **Configure AWS CLI with new credentials:**
   ```bash
   aws configure --profile chatterbox
   ```

   Then deploy using:
   ```powershell
   $env:AWS_PROFILE = "chatterbox"
   pulumi up
   ```

### Option 3: Use AWS Console (GUI)

1. Go to IAM Console → Policies → Create Policy
2. Choose JSON tab and paste the contents of `iam-deployment-policy.json`
3. Name it `ChatterboxDeploymentPolicy`
4. Go to Users → select your user (`limited`) → Add permissions → Attach policies
5. Select `ChatterboxDeploymentPolicy`

## What This Policy Allows

The policy grants minimum permissions to:

- **IAM**: Create/manage roles and policies with `chatterbox-*` prefix
- **Lambda**: Create/manage functions with `chatterbox-*` prefix
- **API Gateway**: Manage WebSocket APIs
- **DynamoDB**: Create/manage tables with `chatterbox-*` prefix
- **CloudWatch Logs**: Manage log groups for Lambda functions
- **CloudFormation**: Deploy and manage stacks with `chatterbox-*` prefix
- **S3**: Upload Lambda deployment packages

## Security Notes

- All resources are scoped to `chatterbox-*` naming prefix
- No wildcard permissions on sensitive operations
- Can't modify IAM roles/policies outside the `chatterbox-*` namespace
- Can't create Lambda functions or DynamoDB tables with other names
- S3 access is limited to uploading Lambda code packages

## Verification

After applying the policy, verify permissions:
```bash
aws iam simulate-principal-policy \
  --policy-source-arn arn:aws:iam::663866322745:user/limited \
  --action-names iam:GetRole iam:CreateRole lambda:CreateFunction \
  --resource-arns arn:aws:iam::663866322745:role/chatterbox-websocket-lambda-role
```

## Troubleshooting

If you still get permission errors:

1. **Check policy attachment:**
   ```bash
   aws iam list-attached-user-policies --user-name limited
   ```

2. **Wait for propagation** (can take up to 5 minutes)

3. **Verify account ID** in the policy matches your account: `663866322745`

4. **Check for restrictive SCPs** (Service Control Policies) if using AWS Organizations

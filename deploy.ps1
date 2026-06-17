# Chatterbox Deployment Script
# This script builds, packages, uploads, and deploys the Chatterbox backend to AWS

param(
	[Parameter(Mandatory=$true)]
	[string]$S3Bucket,

	[Parameter(Mandatory=$false)]
	[string]$StackName = "chatterbox-backend",

	[Parameter(Mandatory=$false)]
	[string]$StageName = "prod",

	[Parameter(Mandatory=$false)]
	[string]$Region = "us-east-1"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Chatterbox Deployment ===" -ForegroundColor Cyan
Write-Host "Stack: $StackName" -ForegroundColor Yellow
Write-Host "S3 Bucket: $S3Bucket" -ForegroundColor Yellow
Write-Host "Stage: $StageName" -ForegroundColor Yellow
Write-Host "Region: $Region" -ForegroundColor Yellow
Write-Host ""

# Step 1: Build Lambda
Write-Host "[1/4] Building Lambda function..." -ForegroundColor Green
Push-Location Chatterbox.Backend
try {
	dotnet publish -c Release -o publish
	if ($LASTEXITCODE -ne 0) {
		throw "Build failed"
	}
	Write-Host "  ✓ Build successful" -ForegroundColor Gray
}
finally {
	Pop-Location
}

# Step 2: Package Lambda
Write-Host "[2/4] Packaging Lambda ZIP..." -ForegroundColor Green
$zipPath = "Chatterbox.Backend\publish\lambda.zip"
if (Test-Path $zipPath) {
	Remove-Item $zipPath -Force
}
Compress-Archive -Path "Chatterbox.Backend\publish\*" -DestinationPath $zipPath -Force
Write-Host "  ✓ Package created: $zipPath" -ForegroundColor Gray

# Step 3: Upload to S3
Write-Host "[3/4] Uploading to S3..." -ForegroundColor Green
$s3Key = "chatterbox-backend/lambda.zip"
aws s3 cp $zipPath "s3://$S3Bucket/$s3Key" --region $Region
if ($LASTEXITCODE -ne 0) {
	throw "S3 upload failed"
}
Write-Host "  ✓ Uploaded to s3://$S3Bucket/$s3Key" -ForegroundColor Gray

# Step 4: Deploy CloudFormation
Write-Host "[4/4] Deploying CloudFormation stack..." -ForegroundColor Green
aws cloudformation deploy `
	--template-file template.yaml `
	--stack-name $StackName `
	--parameter-overrides `
		LambdaCodeBucket=$S3Bucket `
		LambdaCodeKey=$s3Key `
		StageName=$StageName `
	--capabilities CAPABILITY_NAMED_IAM `
	--region $Region

if ($LASTEXITCODE -ne 0) {
	throw "CloudFormation deployment failed"
}

Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Cyan

# Get outputs
Write-Host ""
Write-Host "Stack Outputs:" -ForegroundColor Yellow
$endpoint = aws cloudformation describe-stacks `
	--stack-name $StackName `
	--query 'Stacks[0].Outputs[?OutputKey==`WebSocketEndpoint`].OutputValue' `
	--output text `
	--region $Region

Write-Host "WebSocket Endpoint: $endpoint" -ForegroundColor Green
Write-Host ""
Write-Host "Connect to your WebSocket:" -ForegroundColor Cyan
Write-Host "  wss://$endpoint" -ForegroundColor White

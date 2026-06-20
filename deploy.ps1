# Chatterbox Deployment Script
# This script builds, packages, uploads, and deploys the Chatterbox backend to AWS

param(
	[Parameter(Mandatory=$true)]
	[string]$S3Bucket,

	[Parameter(Mandatory=$false)]
	[string]$Environment = "prod",

	[Parameter(Mandatory=$false)]
	[string]$Region = "ca-central-1"
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$BackendRoot = Join-Path $RepoRoot "Chatterbox.Backend"

# Derive stack and resource names from environment
$StackName = "chatterbox-$Environment"
$StageName = $Environment
$TableName = "chatterbox-connections-$Environment"

Write-Host "=== Chatterbox Deployment ===" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Yellow
Write-Host "Stack: $StackName" -ForegroundColor Yellow
Write-Host "S3 Bucket: $S3Bucket" -ForegroundColor Yellow
Write-Host "Stage: $StageName" -ForegroundColor Yellow
Write-Host "Table: $TableName" -ForegroundColor Yellow
Write-Host "Region: $Region" -ForegroundColor Yellow
Write-Host ""

# Step 1: Build Lambda
Write-Host "[1/5] Building Lambda function..." -ForegroundColor Green
Push-Location $BackendRoot
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
Write-Host "[2/5] Packaging Lambda ZIP..." -ForegroundColor Green
$zipPath = Join-Path $BackendRoot "publish\lambda.zip"
if (Test-Path $zipPath) {
	Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $BackendRoot "publish\*") -DestinationPath $zipPath -Force
Write-Host "  ✓ Package created: $zipPath" -ForegroundColor Gray

# Step 3: Apply S3 lifecycle policy
Write-Host "[3/5] Applying S3 lifecycle policy..." -ForegroundColor Green
$artifactPrefix = "chatterbox-"
$lifecycleConfiguration = @{
	Rules = @(
		@{
			ID = "ExpireDeploymentArtifacts"
			Status = "Enabled"
			Filter = @{
				Prefix = $artifactPrefix
			}
			Expiration = @{
				Days = 30
			}
			AbortIncompleteMultipartUpload = @{
				DaysAfterInitiation = 7
			}
		}
	)
} | ConvertTo-Json -Depth 10

$tempLifecyclePath = Join-Path $env:TEMP "chatterbox-lifecycle-$Environment.json"
Set-Content -Path $tempLifecyclePath -Value $lifecycleConfiguration -Encoding utf8
$tempLifecycleUri = "file://" + ($tempLifecyclePath -replace '\\', '/')
try {
	aws s3api put-bucket-lifecycle-configuration `
		--bucket $S3Bucket `
		--lifecycle-configuration $tempLifecycleUri `
		--region $Region
	if ($LASTEXITCODE -ne 0) {
		throw "S3 lifecycle configuration failed"
	}
	Write-Host "  ✓ Lifecycle policy applied" -ForegroundColor Gray
}
finally {
	if (Test-Path $tempLifecyclePath) {
		Remove-Item $tempLifecyclePath -Force
	}
}

# Step 4: Upload to S3
Write-Host "[4/5] Uploading to S3..." -ForegroundColor Green
$artifactInputs = Get-ChildItem -Path $BackendRoot -Recurse -File |
	Where-Object {
		$_.FullName -notmatch '\\(bin|obj|publish)\\' -and
		$_.Extension -in @('.cs', '.csproj', '.json', '.props', '.targets', '.config', '.resx')
	} |
	Sort-Object FullName

$hashBuilder = [System.Text.StringBuilder]::new()
foreach ($file in $artifactInputs) {
	[void]$hashBuilder.AppendLine($file.FullName.Substring($BackendRoot.Length + 1))
	[void]$hashBuilder.AppendLine([System.IO.File]::ReadAllText($file.FullName))
}

$artifactHash = [System.BitConverter]::ToString(
	[System.Security.Cryptography.SHA256]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($hashBuilder.ToString()))
) -replace '-', ''
$artifactHash = $artifactHash.Substring(0, 12).ToLowerInvariant()

$s3Key = "$artifactPrefix$Environment/$artifactHash/lambda.zip"
aws s3 cp $zipPath "s3://$S3Bucket/$s3Key" --region $Region
if ($LASTEXITCODE -ne 0) {
	throw "S3 upload failed"
}
Write-Host "  ✓ Uploaded to s3://$S3Bucket/$s3Key" -ForegroundColor Gray

# Step 5: Deploy CloudFormation
Write-Host "[5/5] Deploying CloudFormation stack..." -ForegroundColor Green
aws cloudformation deploy `
	--template-file template.yaml `
	--stack-name $StackName `
	--parameter-overrides `
		Environment=$Environment `
		TableName=$TableName `
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

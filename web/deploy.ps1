param(
    [Parameter(Mandatory = $true)]
    [string]$S3Bucket,

    [Parameter(Mandatory = $false)]
    [string]$Environment = "prod",

    [Parameter(Mandatory = $false)]
    [string]$Region = "ca-central-1",

    [Parameter(Mandatory = $false)]
    [string]$WebSocketUrl = "",

    [Parameter(Mandatory = $false)]
    [string]$DomainName = "",

    [Parameter(Mandatory = $false)]
    [string]$CertificateArn = "",

    [Parameter(Mandatory = $false)]
    [string]$AwsServiceUrl = ""
)

$ErrorActionPreference = "Stop"

if (($DomainName -and -not $CertificateArn) -or (-not $DomainName -and $CertificateArn)) {
    throw "Set both DomainName and CertificateArn together, or leave both blank to use the default CloudFront hostname."
}

if ($DomainName -and $CertificateArn) {
    $certificateArnMatch = [regex]::Match($CertificateArn, '^arn:aws:acm:([^:]+):')
    if (-not $certificateArnMatch.Success) {
        throw "Certificate ARN '$CertificateArn' is not in the expected ACM format."
    }

    $certificateRegion = $certificateArnMatch.Groups[1].Value
    if ($certificateRegion -ne 'us-east-1') {
        throw "CloudFront custom domains require the ACM certificate to be in us-east-1. The supplied certificate is in '$certificateRegion'. Please request or import the certificate in us-east-1 and pass that ARN here."
    }
}

$WebRoot = $PSScriptRoot
$BuildRoot = Join-Path $WebRoot "dist"
$StackName = "chatterbox-web-$Environment"

Write-Host "=== Chatterbox Web Deployment ===" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Yellow
Write-Host "Region: $Region" -ForegroundColor Yellow
Write-Host "Bucket: $S3Bucket" -ForegroundColor Yellow
Write-Host "Custom domain: $DomainName" -ForegroundColor Yellow
Write-Host "Certificate ARN: $CertificateArn" -ForegroundColor Yellow
if ($WebSocketUrl) {
    Write-Host "WebSocket URL: $WebSocketUrl" -ForegroundColor Yellow
}
Write-Host ""

# Step 1: install dependencies and build the site
Write-Host "[1/4] Installing and building the web app..." -ForegroundColor Green
Push-Location $WebRoot
try {
    if (-not (Test-Path (Join-Path $WebRoot "node_modules"))) {
        npm install --no-fund --no-audit
        if ($LASTEXITCODE -ne 0) { throw "npm install failed" }
    }

    if ($WebSocketUrl) {
        $env:VITE_CHATTERBOX_WS_URL = $WebSocketUrl
    }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }
}
finally {
    Pop-Location
}

if (-not (Test-Path $BuildRoot)) {
    throw "Build output not found at '$BuildRoot'."
}

# Step 2: ensure the S3 bucket exists
Write-Host "[2/4] Checking S3 bucket..." -ForegroundColor Green
$headArgs = @('s3api', 'head-bucket', '--bucket', $S3Bucket, '--region', $Region)
if ($AwsServiceUrl) {
    $headArgs += @('--endpoint-url', $AwsServiceUrl)
}

& aws @headArgs 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ! Bucket '$S3Bucket' does not exist. Creating it..." -ForegroundColor Yellow

    $createArgs = @('s3api', 'create-bucket', '--bucket', $S3Bucket, '--region', $Region)
    if ($Region -ne 'us-east-1') {
        $createArgs += @('--create-bucket-configuration', "LocationConstraint=$Region")
    }
    if ($AwsServiceUrl) {
        $createArgs += @('--endpoint-url', $AwsServiceUrl)
    }

    & aws @createArgs
    if ($LASTEXITCODE -ne 0) {
        throw "S3 bucket creation failed"
    }
}

# Step 3: sync static files to S3
Write-Host "[3/4] Uploading static files..." -ForegroundColor Green
$syncArgs = @('s3', 'sync', $BuildRoot, "s3://$S3Bucket/", '--delete', '--region', $Region)
if ($AwsServiceUrl) {
    $syncArgs += @('--endpoint-url', $AwsServiceUrl)
}

& aws @syncArgs
if ($LASTEXITCODE -ne 0) {
    throw "S3 sync failed"
}

# Step 4: deploy CloudFormation
Write-Host "[4/4] Deploying CloudFormation stack..." -ForegroundColor Green
$parameterOverrides = @(
    "Environment=$Environment",
    "BucketName=$S3Bucket",
    "DomainName=$DomainName",
    "CertificateArn=$CertificateArn"
)

$deployArgs = @(
    'cloudformation', 'deploy',
    '--template-file', (Join-Path $WebRoot 'template.yaml'),
    '--stack-name', $StackName,
    '--parameter-overrides',
    $parameterOverrides,
    '--capabilities', 'CAPABILITY_IAM',
    '--region', $Region
)

if ($AwsServiceUrl) {
    $deployArgs += @('--endpoint-url', $AwsServiceUrl)
}

& aws @deployArgs
if ($LASTEXITCODE -ne 0) {
    throw "CloudFormation deployment failed"
}

Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Cyan

$cloudFrontArgs = @(
    'cloudformation', 'describe-stacks',
    '--stack-name', $StackName,
    '--query', 'Stacks[0].Outputs[?OutputKey==`CloudFrontUrl`].OutputValue',
    '--output', 'text',
    '--region', $Region
)

if ($AwsServiceUrl) {
    $cloudFrontArgs += @('--endpoint-url', $AwsServiceUrl)
}

$cloudFrontUrl = & aws @cloudFrontArgs
if (-not $cloudFrontUrl) {
    $cloudFrontUrl = "https://$S3Bucket.s3.$Region.amazonaws.com"
}

$websiteArgs = @(
    'cloudformation', 'describe-stacks',
    '--stack-name', $StackName,
    '--query', 'Stacks[0].Outputs[?OutputKey==`WebsiteUrl`].OutputValue',
    '--output', 'text',
    '--region', $Region
)

if ($AwsServiceUrl) {
    $websiteArgs += @('--endpoint-url', $AwsServiceUrl)
}

$websiteUrl = & aws @websiteArgs
if (-not $websiteUrl) {
    $websiteUrl = if ($DomainName) { "https://$DomainName" } else { $cloudFrontUrl }
}

Write-Host "CloudFront URL: $cloudFrontUrl" -ForegroundColor Green
Write-Host "Website URL: $websiteUrl" -ForegroundColor Green
Write-Host ""
Write-Host "For a custom domain CNAME, point your DNS record to the CloudFront URL above (for example: d111111abcdef8.cloudfront.net)." -ForegroundColor Yellow

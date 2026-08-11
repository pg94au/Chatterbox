using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using NUnit.Framework;

namespace Chatterbox.Backend.Tests;

[TestFixture]
public class ExperimentalTests
{
    private const string TemplateBody = @"
AWSTemplateFormatVersion: '2010-09-09'
Parameters:
  BucketName:
    Type: String
    Description: 'The name of the bucket to create'
    MinLength: 3

Resources:
  MySimpleBucket:
    Type: AWS::S3::Bucket
    Properties:
      BucketName: !Ref BucketName

Outputs:
  CreatedBucketName:
    Description: ""The name of your new S3 bucket""
    Value: !Ref MySimpleBucket
        ";

    private readonly AmazonCloudFormationClient _cfClient;

    public ExperimentalTests()
    {
        var config = new AmazonCloudFormationConfig
        {
            RegionEndpoint = Amazon.RegionEndpoint.USEast1,
            ServiceURL = "http://localhost:4566"
        };

        _cfClient = new AmazonCloudFormationClient(config);
    }

    [Test]
    public async Task Foo()
    {
        var stackName = $"test-stack-{Guid.NewGuid():N}"; // Unique name per test run

        var createRequest = new CreateStackRequest
        {
            StackName = stackName,
            TemplateBody = TemplateBody,
            Parameters =
            [
                new Parameter { ParameterKey = "BucketName", ParameterValue = $"some-test-bucket-{Guid.NewGuid():N}" }
            ],
            OnFailure = OnFailure.ROLLBACK, // Auto-cleanup if creation fails
        };

        // Trigger the creation in AWS
        await _cfClient.CreateStackAsync(createRequest);

        // Wait in-process until CloudFormation finishes deploying the infrastructure
        await WaitForStackStatusAsync(stackName, StackStatus.CREATE_COMPLETE);

        await DisplayStackOutputsAsync(stackName);
    }


    // Helper method to poll the CloudFormation API in-process
    private async Task WaitForStackStatusAsync(string stackName, StackStatus targetStatus)
    {
        while (true)
        {
            try
            {
                var response = await _cfClient.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
                var currentStatus = response.Stacks[0].StackStatus;

                if (currentStatus == targetStatus) break;

                // If it transitions into a failure state, throw immediately to fail the test fast
                if (currentStatus.Value.EndsWith("_FAILED") || currentStatus == StackStatus.ROLLBACK_COMPLETE)
                {
                    throw new Exception($"Stack entered an unexpected state: {currentStatus}");
                }
            }
            catch (AmazonCloudFormationException ex) when (ex.ErrorCode == "ValidationError" &&
                                                           targetStatus == StackStatus.DELETE_COMPLETE)
            {
                // The stack is successfully deleted and no longer exists in AWS
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(5)); // Poll every 5 seconds
        }
    }

    private async Task DisplayStackOutputsAsync(string stackName)
    {
        var response = await _cfClient.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
        var outputs = response.Stacks[0].Outputs;

        foreach (var output in outputs)
        {
            Console.WriteLine($"Output Key: {output.OutputKey}, Value: {output.OutputValue}");
        }
    }
}

using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using AwesomeAssertions;
using Chatterbox.Rag;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Testcontainers.Floci;

namespace Chatterbox.Backend.Tests;

[TestFixture]
[NonParallelizable]
public class DynamoDbRagRepositoryTests
{
    private const string RagTableName = "chatterbox-rag-test";
    private const string RagVectorIndexName = "embedding-index";
    private const int EmbeddingDimensions = 2;

    private FlociContainer _flociContainer = null!;
    private IAmazonDynamoDB _dynamoDbClient = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        var sessionId = ResourceReaper.DefaultSessionId;

        var network = new NetworkBuilder()
            .WithName($"floci-rag-network-{Guid.NewGuid():N}")
            .Build();

        _flociContainer = new FlociBuilder("floci/floci:2.3.0")
            .WithCleanUp(true)
            .WithName($"floci-rag-{Guid.NewGuid():N}")
            .WithNetwork(network)
            .WithNetworkAliases("floci")
            .WithBindMount("/var/run/docker.sock", "/var/run/docker.sock", AccessMode.ReadWrite)
            .WithPortBinding(4566, true)
            .WithEnvironment("LOG_LEVEL", "DEBUG")
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__KEY", "org.testcontainers.resource-reaper-session")
            .WithEnvironment("FLOCI_DOCKER_EXTRA_LABELS_0__VALUE", sessionId.ToString())
            .WithWaitStrategy(
                Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request =>
                    request.ForPort(4566).ForPath("/_floci/health")))
            .Build();

        await _flociContainer.StartAsync();

        _dynamoDbClient = new AmazonDynamoDBClient(new AmazonDynamoDBConfig
        {
            RegionEndpoint = RegionEndpoint.USEast1,
            AuthenticationRegion = "us-east-1",
            ServiceURL = _flociContainer.GetConnectionString(),
        });

        await _dynamoDbClient.CreateTableAsync(new CreateTableRequest
        {
            TableName = RagTableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S)
            ],
            KeySchema =
            [
                new KeySchemaElement("pk", KeyType.HASH),
                new KeySchemaElement("sk", KeyType.RANGE)
            ]
        });

        await _dynamoDbClient.UpdateTableAsync(new UpdateTableRequest
        {
            TableName = RagTableName,
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });

        await _dynamoDbClient.UpdateTableAsync(new UpdateTableRequest
        {
            TableName = RagTableName,
            VectorIndexUpdates =
            [
                new VectorIndexUpdate
                {
                    Create = new CreateVectorIndexAction
                    {
                        IndexName = RagVectorIndexName,
                        Dimensions = EmbeddingDimensions,
                        DistanceFunction = "COSINE",
                        VectorAttribute = new VectorAttributeDefinition
                        {
                            AttributeName = "embedding"
                        },
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.ALL
                        }
                    }
                }
            ]
        });

        await WaitForVectorIndexToBecomeActiveAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await ClearTableAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_flociContainer is not null)
        {
            await _flociContainer.StopAsync();
            await _flociContainer.DisposeAsync();
        }

        _dynamoDbClient.Dispose();
    }

    [Test]
    public async Task SearchSimilarAsync_WhenChunksExist_ReturnsMostRelevantChunkFirst()
    {
        var repository = CreateRepository();

        var now = DateTimeOffset.UtcNow;
        var chunks = new[]
        {
            new RagChunk("doc-1", "chunk-1", "doc1.pdf", 0, 1, "chunk one text", [1f, 0f], "h1", now),
            new RagChunk("doc-1", "chunk-2", "doc1.pdf", 1, 1, "chunk two text", [0f, 1f], "h1", now),
            new RagChunk("doc-2", "chunk-3", "doc2.pdf", 0, 1, "chunk three text", [0.7f, 0.7f], "h2", now),
        };

        await repository.UpsertChunksAsync(chunks);

        var results = await repository.SearchSimilarAsync([0.95f, 0.05f], topK: 2);

        results.Should().HaveCount(2);
        results[0].ChunkId.Should().Be("chunk-1");
        results[0].SourceFileName.Should().Be("doc1.pdf");
        results[1].ChunkId.Should().Be("chunk-3");
    }

    private DynamoDbRagRepository CreateRepository()
    {
        var options = Options.Create(new RagOptions
        {
            TableName = RagTableName,
            EmbeddingDimensions = EmbeddingDimensions,
            TopK = 5
        });

        return new DynamoDbRagRepository(_dynamoDbClient, options, NullLogger<DynamoDbRagRepository>.Instance);
    }

    private async Task WaitForVectorIndexToBecomeActiveAsync()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var response = await _dynamoDbClient.DescribeTableAsync(RagTableName);
            var vectorIndex = response.Table.VectorIndexes?.FirstOrDefault(index => string.Equals(index.IndexName, RagVectorIndexName, StringComparison.Ordinal));
            if (vectorIndex?.IndexStatus?.Value == "ACTIVE")
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException($"Vector index '{RagVectorIndexName}' did not become ACTIVE in time.");
    }

    private async Task ClearTableAsync()
    {
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;

        do
        {
            var response = await _dynamoDbClient.ScanAsync(new ScanRequest
            {
                TableName = RagTableName,
                ExclusiveStartKey = lastEvaluatedKey,
                ProjectionExpression = "pk, sk",
            });

            foreach (var item in response.Items)
            {
                await _dynamoDbClient.DeleteItemAsync(new DeleteItemRequest
                {
                    TableName = RagTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["pk"] = item["pk"],
                        ["sk"] = item["sk"]
                    }
                });
            }

            lastEvaluatedKey = response.LastEvaluatedKey;
        }
        while (lastEvaluatedKey is { Count: > 0 });
    }
}

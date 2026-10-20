using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Chatterbox.Rag;

public sealed class DynamoDbRagRepository : IRagRepository
{
    private const string VectorIndexName = "embedding-index";

    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly RagOptions _options;
    private readonly ILogger<DynamoDbRagRepository> _logger;

    public DynamoDbRagRepository(
        IAmazonDynamoDB dynamoDb,
        IOptions<RagOptions> options,
        ILogger<DynamoDbRagRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(dynamoDb);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _dynamoDb = dynamoDb;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Upserts a collection of RAG chunks into DynamoDB.
    /// </summary>
    /// <param name="chunks">Chunks to upsert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UpsertChunksAsync(IEnumerable<RagChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (string.IsNullOrWhiteSpace(_options.TableName))
        {
            throw new InvalidOperationException("RAG table name is not configured.");
        }

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = new PutItemRequest
            {
                TableName = _options.TableName,
                Item = ToItem(chunk)
            };

            await _dynamoDb.PutItemAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Searches similar chunks using DynamoDB native vector index search.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="topK">Maximum matches to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Top matching chunks.</returns>
    public async Task<IReadOnlyList<RagSearchResult>> SearchSimilarAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (queryEmbedding.Length == 0)
        {
            return [];
        }

        if (topK <= 0)
        {
            throw new ArgumentException("TopK must be greater than zero.", nameof(topK));
        }

        if (string.IsNullOrWhiteSpace(_options.TableName))
        {
            throw new InvalidOperationException("RAG table name is not configured.");
        }

        if (_options.EmbeddingDimensions <= 0)
        {
            throw new InvalidOperationException("RAG embedding dimensions must be greater than zero.");
        }

        if (queryEmbedding.Length != _options.EmbeddingDimensions)
        {
            throw new InvalidOperationException($"Query embedding length {queryEmbedding.Length} does not match configured vector index dimensions {_options.EmbeddingDimensions}.");
        }

        var request = new SearchVectorsRequest
        {
            TableName = _options.TableName,
            IndexName = VectorIndexName,
            TopK = topK,
            SearchVector = ToSearchVector(queryEmbedding),
            ProjectionExpression = "documentId, chunkId, sourceFileName, chunkIndex, pageNumber, #text",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#text"] = "text"
            }
        };

        var response = await _dynamoDb.SearchVectorsAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.SearchResults is null || response.SearchResults.Count == 0)
        {
            return [];
        }

        _logger.LogDebug("SearchVectors returned {ResultCount} results from {TableName}/{VectorIndexName}.", response.SearchResults.Count, _options.TableName, VectorIndexName);

        return response.SearchResults
            .Select(result => ToSearchResult(result.Item, (float)(result.Score ?? 0d)))
            .ToArray();
    }

    private static List<AttributeValue> ToSearchVector(IEnumerable<float> embedding) =>
        embedding.Select(value => new AttributeValue
        {
            N = value.ToString(CultureInfo.InvariantCulture)
        }).ToList();

    private static Dictionary<string, AttributeValue> ToItem(RagChunk chunk)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = $"DOC#{chunk.DocumentId}" },
            ["sk"] = new AttributeValue { S = $"CHUNK#{chunk.ChunkId}" },
            ["documentId"] = new AttributeValue { S = chunk.DocumentId },
            ["chunkId"] = new AttributeValue { S = chunk.ChunkId },
            ["sourceFileName"] = new AttributeValue { S = chunk.SourceFileName },
            ["chunkIndex"] = new AttributeValue { N = chunk.ChunkIndex.ToString(CultureInfo.InvariantCulture) },
            ["pageNumber"] = new AttributeValue { N = chunk.PageNumber.ToString(CultureInfo.InvariantCulture) },
            ["text"] = new AttributeValue { S = chunk.Text },
            ["contentHash"] = new AttributeValue { S = chunk.ContentHash },
            ["createdAtUtc"] = new AttributeValue { S = chunk.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture) },
            ["embedding"] = new AttributeValue
            {
                L = chunk.Embedding.Select(value => new AttributeValue
                {
                    N = value.ToString(CultureInfo.InvariantCulture)
                }).ToList()
            }
        };

        return item;
    }

    private static int ParseInt(IReadOnlyDictionary<string, AttributeValue> item, string key)
    {
        if (!item.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value.N))
        {
            return 0;
        }

        return int.TryParse(value.N, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static RagSearchResult ToSearchResult(IReadOnlyDictionary<string, AttributeValue> item, float score) =>
        new(
            DocumentId: item.GetValueOrDefault("documentId")?.S ?? string.Empty,
            ChunkId: item.GetValueOrDefault("chunkId")?.S ?? string.Empty,
            SourceFileName: item.GetValueOrDefault("sourceFileName")?.S ?? string.Empty,
            ChunkIndex: ParseInt(item, "chunkIndex"),
            PageNumber: ParseInt(item, "pageNumber"),
            Text: item.GetValueOrDefault("text")?.S ?? string.Empty,
            Score: score);

    private static float CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;

        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return (float)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
    }
}

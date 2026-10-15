using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Chatterbox.Rag;

public sealed class RagRetrievalService
{
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly IRagRepository _repository;
    private readonly RagOptions _options;
    private readonly ILogger<RagRetrievalService> _logger;

    public RagRetrievalService(
        IEmbeddingGenerator embeddingGenerator,
        IRagRepository repository,
        IOptions<RagOptions> options,
        ILogger<RagRetrievalService> logger)
    {
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _embeddingGenerator = embeddingGenerator;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves relevant chunks and formats context text for prompt injection.
    /// </summary>
    /// <param name="userPrompt">Incoming user prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A context string and matched chunk metadata.</returns>
    public async Task<(string Context, IReadOnlyList<RagSearchResult> Matches)> BuildContextAsync(string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        if (string.IsNullOrWhiteSpace(_options.TableName) || _options.TopK <= 0)
        {
            return (string.Empty, []);
        }

        var queryEmbedding = await _embeddingGenerator.GenerateEmbeddingAsync(userPrompt, cancellationToken).ConfigureAwait(false);
        if (queryEmbedding.Length == 0)
        {
            return (string.Empty, []);
        }

        var matches = await _repository.SearchSimilarAsync(queryEmbedding, _options.TopK, cancellationToken).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            _logger.LogInformation("RAG retrieval returned no matches.");
            return (string.Empty, matches);
        }

        var builder = new StringBuilder();
        builder.AppendLine("Use the following context when answering. If context is not relevant, answer normally.");
        builder.AppendLine();

        foreach (var match in matches)
        {
            builder.AppendLine($"[Source: {match.SourceFileName}, Page: {match.PageNumber}, Chunk: {match.ChunkIndex}]");
            builder.AppendLine(match.Text);
            builder.AppendLine();
        }

        return (builder.ToString().Trim(), matches);
    }
}

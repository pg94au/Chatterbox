using Microsoft.Extensions.Logging;

namespace Chatterbox.Rag;

/// <summary>
/// Provides an <see cref="IEmbeddingGenerator"/> implementation that logs when no embedding generator is configured and returns a
/// fixed fallback response.
/// </summary>
/// <remarks>Use as a safe default when embedding generation is unavailable or disabled.</remarks>
public sealed class NullEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly ILogger<NullEmbeddingGenerator> _logger;

    public NullEmbeddingGenerator(ILogger<NullEmbeddingGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation("Embedding generator is not configured. Returning a dummy embedding.");
        return Task.FromResult(Array.Empty<float>());
    }
}

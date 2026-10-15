using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace Chatterbox.Rag;

public sealed class SemanticKernelEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<SemanticKernelEmbeddingGenerator> _logger;

    public SemanticKernelEmbeddingGenerator(
        IOptions<EmbeddingOptions> options,
        ILogger<SemanticKernelEmbeddingGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates an embedding using the configured OpenAI-compatible service.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector.</returns>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.ApiKey)
            || string.IsNullOrWhiteSpace(_options.Model))
        {
            _logger.LogWarning("Embedding options are not fully configured.");
            return [];
        }

        try
        {
            var kernelBuilder = Kernel.CreateBuilder();
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(_options.BaseUrl)
            };

            kernelBuilder.AddOpenAITextEmbeddingGeneration(
                modelId: _options.Model,
                apiKey: _options.ApiKey,
                httpClient: httpClient);

            var kernel = kernelBuilder.Build();
            var embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();

            var embedding = await embeddingService.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken).ConfigureAwait(false);
            return embedding.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate embedding from configured provider.");
            return [];
        }
    }
}

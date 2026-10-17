using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using System.Net;

namespace Chatterbox.Rag;

public sealed class SemanticKernelEmbeddingGenerator : IEmbeddingGenerator
{
    private const int MaxRetries = 3;

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

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (TryGetStatusCode(ex, out var statusCode) && IsNonRetryableStatus(statusCode))
            {
                throw new InvalidOperationException(
                    $"Embedding request failed with non-retryable HTTP status {(int)statusCode} ({statusCode}).",
                    ex);
            }
            catch (Exception ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(250 * attempt, 1000));
                _logger.LogWarning(ex, "Embedding request failed on attempt {Attempt}/{MaxRetries}. Retrying in {DelayMs}ms.", attempt, MaxRetries, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Embedding request failed after {MaxRetries} attempts.", ex);
            }
        }

        return [];
    }

    private static bool IsNonRetryableStatus(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        if (status is 408 or 429)
        {
            return false;
        }

        return status is >= 400 and < 500;
    }

    private static bool TryGetStatusCode(Exception exception, out HttpStatusCode statusCode)
    {
        statusCode = default;

        if (exception is HttpRequestException { StatusCode: { } httpRequestStatusCode })
        {
            statusCode = httpRequestStatusCode;
            return true;
        }

        var statusCodeProperty = exception.GetType().GetProperty("StatusCode");
        if (statusCodeProperty?.GetValue(exception) is HttpStatusCode typedStatusCode)
        {
            statusCode = typedStatusCode;
            return true;
        }

        if (statusCodeProperty?.GetValue(exception) is int numericStatusCode
            && Enum.IsDefined(typeof(HttpStatusCode), numericStatusCode))
        {
            statusCode = (HttpStatusCode)numericStatusCode;
            return true;
        }

        if (exception.InnerException is not null)
        {
            return TryGetStatusCode(exception.InnerException, out statusCode);
        }

        return false;
    }
}

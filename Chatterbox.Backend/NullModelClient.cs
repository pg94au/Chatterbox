using Microsoft.Extensions.Logging;

namespace Chatterbox.Backend;

/// <summary>
/// Provides an <see cref="IModelClient"/> implementation that logs when no model client is configured and returns a
/// fixed fallback response.
/// </summary>
/// <remarks>Use as a safe default when model-backed responses are unavailable or disabled.</remarks>
public sealed class NullModelClient : IModelClient
{
    private readonly ILogger<NullModelClient> _logger;

    public NullModelClient(ILogger<NullModelClient> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<string> GetResponseFromAgentAsync(string request, string requesterName, string? retrievedContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterName);

        _logger.LogInformation("Model client is not configured. Returning fallback response for requester {RequesterName}.", requesterName);
        return Task.FromResult($"I'm sorry, {requesterName}.  I'm afraid I can't do that.");
    }
}

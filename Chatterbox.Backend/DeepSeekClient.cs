using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chatterbox.Backend;

/// <summary>
/// A client for interacting with the DeepSeek API to obtain responses from our AI agent.
/// </summary>
public class DeepSeekClient
{
    private static readonly HttpClient HttpClient = new();
    private readonly DeepSeekOptions _deepSeekOptions;
    private readonly ILogger<DeepSeekClient> _logger;

    public DeepSeekClient(IOptions<DeepSeekOptions> options, ILogger<DeepSeekClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _deepSeekOptions = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends the given message to DeepSeek and returns the agent's response text.
    /// </summary>
    /// <param name="request">The end-user message to send to the agent.</param>
    /// <param name="requesterName">The display name of the user who requested the response.</param>
    /// <returns>The agent response text, or a fallback response when unavailable.</returns>
    public async Task<string> GetResponseFromAgentAsync(string request, string requesterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requesterName);

        _logger.LogInformation("Requesting response from DeepSeek for requester {RequesterName}", requesterName);

        var fallbackResponse = $"I'm sorry, {requesterName}.  I'm afraid I can't do that.";

        if (string.IsNullOrWhiteSpace(_deepSeekOptions.BaseUrl)
            || string.IsNullOrWhiteSpace(_deepSeekOptions.ApiKey)
            || string.IsNullOrWhiteSpace(_deepSeekOptions.Model))
        {
            _logger.LogInformation("DeepSeek options are not fully configured.");
            return fallbackResponse;
        }

        var endpoint = _deepSeekOptions.BaseUrl.TrimEnd('/') + "/chat/completions";
        var payload = new
        {
            model = _deepSeekOptions.Model,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = request
                }
            }
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"
            );
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deepSeekOptions.ApiKey);

            using var response = await HttpClient.SendAsync(httpRequest);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("DeepSeek request failed with status code {StatusCode}", response.StatusCode);
                return fallbackResponse;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);

            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                _logger.LogWarning("DeepSeek response does not contain any choices.");
                return fallbackResponse;
            }

            var firstChoice = choices[0];
            if (!firstChoice.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                _logger.LogWarning("DeepSeek response does not contain a valid message content.");
                return fallbackResponse;
            }

            var text = content.GetString();
            _logger.LogInformation("Returning response from DeepSeek.");

            return string.IsNullOrWhiteSpace(text) ? fallbackResponse : text;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An error occurred while requesting a response from DeepSeek.");
            return fallbackResponse;
        }
    }
}

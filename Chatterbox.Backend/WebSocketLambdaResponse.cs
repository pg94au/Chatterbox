using System.Text.Json.Serialization;

namespace Chatterbox.Backend;

public sealed class WebSocketLambdaResponse
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;
}
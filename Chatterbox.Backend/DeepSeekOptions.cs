namespace Chatterbox.Backend;

/// <summary>
/// Represents the configuration options for DeepSeek integration.
/// </summary>
public class DeepSeekOptions
{
    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
    public required string Model { get; init; }
}

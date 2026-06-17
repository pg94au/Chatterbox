namespace Chatterbox.Backend;

public sealed class WebSocketRequestContext
{
    public string? RouteKey { get; init; }

    public string? ConnectionId { get; init; }
}